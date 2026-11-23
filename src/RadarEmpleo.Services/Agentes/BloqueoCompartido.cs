using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Services.Agentes;

/// <summary>Lo que da <see cref="BloqueoCompartido.Intentar"/>: el bloqueo cogido (se suelta con Dispose) o quién lo tiene.</summary>
public sealed class ResultadoBloqueo : IDisposable
{
    private static readonly TimeSpan PausaBorrado = TimeSpan.FromMilliseconds(100);
    private const int IntentosBorrado = 5;

    private readonly FileStream? _flujo;
    private readonly string? _ruta;

    private ResultadoBloqueo(FileStream? flujo, string? ruta, string? ocupadoPor)
    {
        _flujo = flujo;
        _ruta = ruta;
        OcupadoPor = ocupadoPor;
    }

    internal static ResultadoBloqueo Tomado(FileStream flujo, string ruta) => new(flujo, ruta, null);
    internal static ResultadoBloqueo Ocupado(string quien) => new(null, null, quien);

    public bool Cogido => _flujo is not null;

    /// <summary>Quién lo tiene, para enseñarlo («la tarea de los lunes»); null si se ha cogido.</summary>
    public string? OcupadoPor { get; }

    /// <summary>
    /// Suelta el bloqueo: cierra el archivo y lo borra, con unos reintentos (un antivirus o la tarea de los lunes
    /// leyéndolo justo entonces). Si aun así se queda, lleva el pid de la API y la ejecución, y el siguiente lo da por
    /// caducado (<see cref="BloqueoEjecucion.EstaCaducado"/>). No se usa <c>DeleteOnClose</c> porque obligaría a quien lo
    /// lee a compartir el borrado, y la tarea de los lunes lo lee con <c>Get-Content</c>.
    /// </summary>
    public void Dispose()
    {
        if (_flujo is null) return;
        _flujo.Dispose();
        for (var intento = 1; ; intento++)
        {
            try
            {
                File.Delete(_ruta!);
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException && intento < IntentosBorrado)
            {
                Thread.Sleep(PausaBorrado);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}

/// <summary>
/// El archivo de bloqueo compartido <c>Curriculum\radar\.en-curso</c> (apartado 4.6 del plan de la F7): impide que
/// la app lance Claude a la vez que la tarea de los lunes o <c>/empleo</c>. Se crea con <see cref="FileMode.CreateNew"/>
/// (atómico) y se mantiene abierto mientras dura la ejecución. Uno caducado (<see cref="BloqueoEjecucion.EstaCaducado"/>)
/// se borra y se vuelve a intentar una vez. Todo lo raro (no se puede leer ni crear, o en su sitio hay una carpeta)
/// cuenta como ocupado: la ejecución espera y, si sigue así una hora, falla con su mensaje.
/// </summary>
public sealed class BloqueoCompartido
{
    public const string NombreArchivo = ".en-curso";

    private static readonly UTF8Encoding Utf8SinBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly RutasCurriculum _rutas;
    private readonly ILogger<BloqueoCompartido> _log;
    private readonly Func<int, DateTime?> _inicioDelProceso;
    private readonly int _pidPropio;

    /// <param name="inicioDelProceso">Hora de inicio (UTC) del proceso con ese pid, o null si no existe. Por defecto, la real.</param>
    /// <param name="pidPropio">El pid de esta API (por defecto, el real).</param>
    public BloqueoCompartido(RutasCurriculum rutas, ILogger<BloqueoCompartido> log, Func<int, DateTime?>? inicioDelProceso = null,
        int? pidPropio = null)
    {
        _rutas = rutas;
        _log = log;
        _inicioDelProceso = inicioDelProceso ?? InicioDelProceso;
        _pidPropio = pidPropio ?? Environment.ProcessId;
    }

    /// <summary>Ruta del archivo, o null si no hay carpeta configurada.</summary>
    public string? Ruta => _rutas.CarpetaRadar is { } radar ? Path.Combine(radar, NombreArchivo) : null;

    /// <summary>Intenta cogerlo con ese contenido.</summary>
    /// <exception cref="ReglaNegocioException">No hay carpeta Curriculum.</exception>
    public ResultadoBloqueo Intentar(BloqueoLeido contenido, DateTime ahoraUtc)
    {
        if (!_rutas.HayCarpeta || Ruta is not { } ruta) throw new ReglaNegocioException(RutasCurriculum.MensajeSinCarpeta);
        try
        {
            Directory.CreateDirectory(_rutas.CarpetaRadar!);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning("No se puede crear la carpeta radar: el bloqueo cuenta como ocupado.");
            return ResultadoBloqueo.Ocupado(BloqueoEjecucion.NombreVisible(null));
        }

        for (var intento = 0; ; intento++)
        {
            if (Crear(ruta, contenido) is { } tomado) return tomado;
            if (Directory.Exists(ruta))
            {
                _log.LogWarning("En lugar del bloqueo hay una carpeta: cuenta como ocupado.");
                return ResultadoBloqueo.Ocupado(BloqueoEjecucion.NombreVisible(null));
            }

            var (leido, caducado) = Leer(ruta, ahoraUtc, contenido.EjecucionId);
            if (!caducado || intento > 0)
            {
                // Uno que no es de los conocidos se enseña como «otro programa»; lo que dice el archivo, solo aquí.
                if (leido is not null && BloqueoEjecucion.NombreVisible(leido.Quien) == BloqueoEjecucion.NombreOtroPrograma)
                    _log.LogInformation("Bloqueo ocupado por «{Quien}» ({Tarea}, pid {Pid}).",
                        BloqueoEjecucion.NombreSaneado(leido.Quien), BloqueoEjecucion.NombreSaneado(leido.Tarea), leido.Pid);
                return ResultadoBloqueo.Ocupado(BloqueoEjecucion.NombreVisible(leido?.Quien));
            }
            _log.LogInformation("Bloqueo caducado de {Quien} ({Tarea}, pid {Pid}): se borra.",
                BloqueoEjecucion.NombreSaneado(leido?.Quien), BloqueoEjecucion.NombreSaneado(leido?.Tarea), leido?.Pid);
            if (!Borrar(ruta)) return ResultadoBloqueo.Ocupado(BloqueoEjecucion.NombreVisible(leido?.Quien));
        }
    }

    /// <summary>Al arrancar: borra el bloqueo si es de la app y está caducado (lo dejó una API que se cerró). Devuelve si lo ha borrado.</summary>
    public bool LimpiarPropioCaducado(DateTime ahoraUtc)
    {
        if (!_rutas.HayCarpeta || Ruta is not { } ruta || !File.Exists(ruta)) return false;
        var (leido, caducado) = Leer(ruta, ahoraUtc, ejecucionActual: null);
        if (leido?.Quien != BloqueoEjecucion.QuienRadar || !caducado) return false;
        _log.LogInformation("Bloqueo propio caducado (ejecución {Ejecucion}): se borra al arrancar.", leido.EjecucionId);
        return Borrar(ruta);
    }

    /// <summary>
    /// Hora de inicio (UTC) del proceso con ese pid, o null si no existe. Si existe pero no deja leer su hora, se toma
    /// como vivo (<see cref="DateTime.MinValue"/>); <see cref="BloqueoEjecucion.DuracionMaxima"/> impide que sea eterno.
    /// </summary>
    public static DateTime? InicioDelProceso(int pid)
    {
        try
        {
            using var proceso = Process.GetProcessById(pid);
            return proceso.HasExited ? null : proceso.StartTime.ToUniversalTime();
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Lo crea con el contenido; null si no se puede (ya existe, está en uso, sin permiso o hay una carpeta).</summary>
    private static ResultadoBloqueo? Crear(string ruta, BloqueoLeido contenido)
    {
        FileStream flujo;
        try
        {
            flujo = new FileStream(ruta, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            flujo.Write(Utf8SinBom.GetBytes(BloqueoEjecucion.Serializar(contenido)));
            flujo.Flush(flushToDisk: true);
            return ResultadoBloqueo.Tomado(flujo, ruta);
        }
        catch
        {
            ResultadoBloqueo.Tomado(flujo, ruta).Dispose();
            throw;
        }
    }

    private (BloqueoLeido? Leido, bool Caducado) Leer(string ruta, DateTime ahoraUtc, int? ejecucionActual)
    {
        string texto;
        DateTime modificado;
        try
        {
            using var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var lector = new StreamReader(flujo, Utf8SinBom, detectEncodingFromByteOrderMarks: true);
            texto = lector.ReadToEnd();
            modificado = File.GetLastWriteTimeUtc(ruta);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // No se puede leer (o se ha borrado entre medias): ocupado; el siguiente sondeo lo vuelve a mirar.
            return (null, false);
        }

        var leido = BloqueoEjecucion.Leer(texto);
        var caducado = leido is null
            ? BloqueoEjecucion.IlegibleCaducado(modificado, ahoraUtc)
            : BloqueoEjecucion.EstaCaducado(leido, modificado, ahoraUtc, _inicioDelProceso, _pidPropio, ejecucionActual);
        return (leido, caducado);
    }

    private bool Borrar(string ruta)
    {
        try
        {
            // Uno caducado de solo lectura (lo dejó así otro programa) también se borra.
            var atributos = File.GetAttributes(ruta);
            if ((atributos & FileAttributes.ReadOnly) != 0) File.SetAttributes(ruta, atributos & ~FileAttributes.ReadOnly);
            File.Delete(ruta);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.LogInformation("No se ha podido borrar el bloqueo caducado: sigue abierto.");
            return false;
        }
    }
}
