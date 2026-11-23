using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadarEmpleo.Domain;

/// <summary>
/// Contenido del archivo de bloqueo compartido <c>Curriculum\radar\.en-curso</c> (una línea JSON en UTF-8). Lo
/// ponen la app, la tarea de los lunes y, desde la F10, <c>/empleo</c>, para no lanzar Claude a la vez.
/// </summary>
/// <param name="Quien">«RadarEmpleo», «TareaLunes» o «Empleo».</param>
/// <param name="Pid">Proceso que lo tiene: si ya no existe, el bloqueo está caducado.</param>
/// <param name="CaducaUtc">A partir de aquí está caducado aunque el proceso siga vivo.</param>
public sealed record BloqueoLeido(
    int Version,
    string Quien,
    string? Tarea,
    int? EjecucionId,
    int? Pid,
    DateTime? InicioUtc,
    DateTime CaducaUtc,
    bool UsaChrome);

/// <summary>
/// Reglas del bloqueo compartido, sin disco ni procesos: el formato del archivo y cuándo está caducado. Se lee con
/// tolerancia y nada es eterno: un archivo ilegible, uno sin pid o uno cuyo proceso no deja leer su hora de inicio
/// cuentan como ocupados como mucho <see cref="DuracionMaxima"/> desde su última modificación, aunque digan que caducan
/// más tarde; y uno con la fecha de modificación en el futuro cuenta como caducado.
/// </summary>
public static class BloqueoEjecucion
{
    public const int Version = 1;
    public const string QuienRadar = "RadarEmpleo";
    public const string QuienTareaLunes = "TareaLunes";
    public const string QuienEmpleo = "Empleo";

    /// <summary>Lo que se añade al tiempo máximo de la tarea para fijar <see cref="BloqueoLeido.CaducaUtc"/>.</summary>
    public static readonly TimeSpan MargenCaducidad = TimeSpan.FromMinutes(5);

    /// <summary>Cuánto cuenta como ocupado un archivo que no se puede leer, desde su última modificación.</summary>
    public static readonly TimeSpan CaducidadIlegible = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Lo máximo que puede durar cualquier bloqueo desde su última modificación: la tarea más larga de la app (Rastreo,
    /// 30 min) más el margen cabe de sobra, y también la tarea de los lunes (45 min). Es la misma hora que espera la app.
    /// </summary>
    public static readonly TimeSpan DuracionMaxima = TimeSpan.FromMinutes(60);

    /// <summary>Largo máximo de <see cref="BloqueoLeido.Quien"/> al apuntarlo en el log.</summary>
    private const int LargoQuienVisible = 40;

    /// <summary>
    /// Holgura al comparar la hora de inicio del proceso con la del bloqueo: el proceso empieza antes de
    /// escribirlo, pero los relojes y su redondeo no son exactos.
    /// </summary>
    private static readonly TimeSpan HolguraInicio = TimeSpan.FromSeconds(2);

    /// <summary>Lo que se admite de adelanto en la fecha de modificación (relojes de otro equipo, redondeos).</summary>
    private static readonly TimeSpan HolguraReloj = TimeSpan.FromMinutes(2);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>El bloqueo que pone la app para una ejecución.</summary>
    public static BloqueoLeido DeLaApp(TareaAgente tarea, int ejecucionId, int pid, DateTime inicioUtc, TimeSpan tiempoMaximo) =>
        new(Version, QuienRadar, tarea.ToString(), ejecucionId, pid, inicioUtc, inicioUtc + tiempoMaximo + MargenCaducidad, UsaChrome: false);

    /// <summary>Una línea JSON con el formato del apartado 4.6 del plan de la F7.</summary>
    public static string Serializar(BloqueoLeido b) => JsonSerializer.Serialize(new
    {
        version = b.Version,
        quien = b.Quien,
        tarea = b.Tarea,
        ejecucionId = b.EjecucionId,
        pid = b.Pid,
        inicioUtc = b.InicioUtc is { } inicio ? FechaIso(inicio) : null,
        caducaUtc = FechaIso(b.CaducaUtc),
        usaChrome = b.UsaChrome
    }, Json);

    /// <summary>Lee el archivo; null si no es un JSON con al menos <c>quien</c> y <c>caducaUtc</c>.</summary>
    public static BloqueoLeido? Leer(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        try
        {
            using var documento = JsonDocument.Parse(texto.Trim().TrimStart('﻿'));
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return null;
            var quien = Texto(raiz, "quien");
            var caduca = Fecha(raiz, "caducaUtc");
            if (string.IsNullOrWhiteSpace(quien) || caduca is null) return null;
            return new BloqueoLeido(
                Entero(raiz, "version") ?? Version,
                quien,
                Texto(raiz, "tarea"),
                Entero(raiz, "ejecucionId"),
                Entero(raiz, "pid"),
                Fecha(raiz, "inicioUtc"),
                caduca.Value,
                raiz.TryGetProperty("usaChrome", out var chrome) && chrome.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// ¿Se puede dar por libre? Sí si ya pasó <see cref="BloqueoLeido.CaducaUtc"/> (como mucho, la última modificación más
    /// <see cref="DuracionMaxima"/>), si su proceso no existe, si existe pero empezó después que el bloqueo (es otro
    /// proceso con el mismo pid) o si es de esta misma API (<paramref name="pidPropio"/>) pero de una ejecución que ya no
    /// es la actual.
    /// </summary>
    /// <param name="modificadoUtc">Última modificación del archivo.</param>
    /// <param name="inicioDelProceso">Hora de inicio (UTC) del proceso con ese pid, o null si no existe.</param>
    /// <param name="pidPropio">El pid de quien pregunta (0 si no importa).</param>
    /// <param name="ejecucionActual">La ejecución que quiere el bloqueo, o null si ninguna (al arrancar).</param>
    public static bool EstaCaducado(BloqueoLeido b, DateTime modificadoUtc, DateTime ahoraUtc, Func<int, DateTime?> inicioDelProceso,
        int pidPropio = 0, int? ejecucionActual = null)
    {
        if (EsFutura(modificadoUtc, ahoraUtc)) return true;
        var tope = modificadoUtc + DuracionMaxima;
        if ((b.CaducaUtc < tope ? b.CaducaUtc : tope) < ahoraUtc) return true;
        if (b.Pid is not { } pid) return false;
        if (pidPropio != 0 && pid == pidPropio && b.Quien == QuienRadar && b.EjecucionId != ejecucionActual) return true;
        if (inicioDelProceso(pid) is not { } inicioProceso) return true;
        return b.InicioUtc is { } inicioBloqueo && inicioProceso > inicioBloqueo + HolguraInicio;
    }

    /// <summary>Un archivo que no se puede leer: ocupado hasta 60 min después de su última modificación; una fecha de modificación en el futuro (más allá de la holgura) lo da por caducado.</summary>
    public static bool IlegibleCaducado(DateTime fechaModificacionUtc, DateTime ahoraUtc) =>
        EsFutura(fechaModificacionUtc, ahoraUtc) || fechaModificacionUtc + CaducidadIlegible < ahoraUtc;

    /// <summary>
    /// Una fecha de modificación en el futuro (más allá de la holgura del reloj) no es de nadie que esté trabajando ahora:
    /// cuenta como caducado. Si contara como «ahora», volvería a contar como ahora en cada sondeo y el bloqueo sería eterno.
    /// </summary>
    private static bool EsFutura(DateTime modificadoUtc, DateTime ahoraUtc) => modificadoUtc > ahoraUtc + HolguraReloj;

    /// <summary>Quien tiene el bloqueo cuando no es uno de los tres conocidos (o no se puede leer).</summary>
    public const string NombreOtroPrograma = "otro programa";

    /// <summary>
    /// Cómo se le enseña a Eduardo quién tiene el bloqueo, para «Esperando: {quien} está usando Claude». Solo los tres
    /// conocidos tienen nombre propio; cualquier otra cosa es <see cref="NombreOtroPrograma"/>, porque el archivo lo
    /// puede escribir cualquiera (su texto, saneado con <see cref="NombreSaneado"/>, solo va al log).
    /// </summary>
    public static string NombreVisible(string? quien) => quien switch
    {
        QuienTareaLunes => "la tarea de los lunes",
        QuienEmpleo => "tu sesión de /empleo en Claude Code",
        QuienRadar => "otra ventana de RadarEmpleo",
        _ => NombreOtroPrograma
    };

    /// <summary>
    /// El texto tal cual del archivo, saneado para el log (solo letras, números, espacios y «._-/», como mucho 40);
    /// «(vacío)» si no queda nada. Nunca se enseña en la app ni sale por la API.
    /// </summary>
    public static string NombreSaneado(string? texto)
    {
        var limpio = new string((texto ?? "").Where(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '_' or '-' or '/').ToArray()).Trim();
        if (limpio.Length > LargoQuienVisible) limpio = limpio[..LargoQuienVisible].TrimEnd();
        return limpio.Length == 0 ? "(vacío)" : limpio;
    }

    private static string FechaIso(DateTime fecha) => Fechas.AUtc(fecha).ToString("yyyy-MM-ddTHH:mm:ss'Z'");

    private static string? Texto(JsonElement raiz, string nombre) =>
        raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static int? Entero(JsonElement raiz, string nombre) =>
        raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.Number && valor.TryGetInt32(out var n) ? n : null;

    // PowerShell escribe las fechas con «o» (7 decimales y «Z»); se admiten también con desfase.
    private static DateTime? Fecha(JsonElement raiz, string nombre) =>
        raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(valor.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var fecha)
            ? fecha.UtcDateTime
            : null;
}
