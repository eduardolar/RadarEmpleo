namespace RadarEmpleo.Services.Agentes;

/// <summary>
/// Que solo una API procese la cola de una misma base de datos: un archivo <c>&lt;bd&gt;.agentes.lock</c> junto a la BD,
/// abierto en exclusiva mientras vive la API. La exclusión la da el sistema sobre ese archivo, así que vale aunque la
/// carpeta se escriba de otra forma (<c>\\?\</c>, mayúsculas, una junction o un nombre 8.3 de la carpeta), no depende
/// del hilo y se suelta sola si el proceso muere. NO vale si se llega a la BD por otro nombre del propio archivo (su
/// nombre 8.3 o un enlace duro): darían otro archivo de exclusión. Por eso el ejecutor falso rechaza las BD con más de
/// un enlace duro, y la de verdad siempre se abre por su ruta normal.
/// </summary>
public sealed class ExclusionBaseDatos : IDisposable
{
    public const string Sufijo = ".agentes.lock";

    // ERROR_SHARING_VIOLATION y ERROR_LOCK_VIOLATION: otro proceso tiene el archivo abierto.
    private const int CompartidoOtro = unchecked((int)0x80070020);
    private const int BloqueadoOtro = unchecked((int)0x80070021);

    private readonly FileStream _flujo;

    private ExclusionBaseDatos(FileStream flujo) => _flujo = flujo;

    /// <summary>
    /// La exclusión de esa base de datos, o null con el motivo para Eduardo: otra copia de RadarEmpleo la tiene, o el
    /// archivo no se puede abrir (es una carpeta, es de solo lectura, no hay permiso…).
    /// </summary>
    public static ExclusionBaseDatos? Intentar(string rutaBaseDatos, out string? motivo)
    {
        var ruta = rutaBaseDatos + Sufijo;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
            motivo = null;
            return new ExclusionBaseDatos(new FileStream(ruta, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException error) when (error.HResult is CompartidoOtro or BloqueadoOtro)
        {
            motivo = MensajesEjecucion.OtraCopiaLlevaLaCola;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            motivo = Directory.Exists(ruta)
                ? MensajesEjecucion.SinExclusion("en su lugar hay una carpeta")
                : MensajesEjecucion.SinExclusion("no se puede abrir (¿solo lectura o sin permiso?)");
        }
        return null;
    }

    public void Dispose() => _flujo.Dispose();
}

/// <summary>
/// Si esta API lleva la cola de agentes de su base de datos (singleton). La toma el <see cref="ProcesadorEjecuciones"/>;
/// la cola y el estado solo la miran: sin ella no se encola ni se confirma una cancelación que lleva otra API.
/// </summary>
public sealed class GuardiaCola : IDisposable
{
    private readonly string? _rutaExclusion;
    private readonly object _cerrojo = new();
    private ExclusionBaseDatos? _exclusion;

    public GuardiaCola(OpcionesEjecutor opciones)
    {
        _rutaExclusion = opciones.RutaExclusion;
        if (_rutaExclusion is null) Motivo = null;
    }

    /// <summary>¿La lleva esta API? Sin ruta de exclusión (los tests del procesador), siempre.</summary>
    public bool LlevaLaCola => _rutaExclusion is null || _exclusion is not null;

    /// <summary>Por qué no la lleva (en español, para Eduardo); null si la lleva. Antes del primer intento: «todavía no».</summary>
    public string? Motivo { get; private set; } = MensajesEjecucion.ColaSinArrancar;

    /// <summary>Intenta tomarla (si no la tiene ya). Devuelve si la lleva.</summary>
    public bool Intentar()
    {
        lock (_cerrojo)
        {
            if (LlevaLaCola)
            {
                Motivo = null;
                return true;
            }
            _exclusion = ExclusionBaseDatos.Intentar(_rutaExclusion!, out var motivo);
            Motivo = motivo;
            return _exclusion is not null;
        }
    }

    public void Dispose()
    {
        lock (_cerrojo)
        {
            _exclusion?.Dispose();
            _exclusion = null;
            if (_rutaExclusion is not null) Motivo = MensajesEjecucion.ColaSinArrancar;
        }
    }
}
