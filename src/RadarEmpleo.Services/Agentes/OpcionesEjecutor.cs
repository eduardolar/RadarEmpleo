namespace RadarEmpleo.Services.Agentes;

/// <summary>
/// Lo que los agentes necesitan de la configuración (<c>Agentes</c> en appsettings). Va aparte de las opciones de
/// la API porque Services no referencia a Api: <c>Program.cs</c> la construye, como <see cref="Curriculum.RutasCurriculum"/>.
/// </summary>
/// <param name="RutaClaude">Ruta completa de <c>claude.exe</c> (o de <c>dotnet</c> en los tests, con <paramref name="ArgumentosPrevios"/>).</param>
/// <param name="ArgumentosPrevios">Van antes de los de la tarea. Solo los usan los tests (<c>ClaudeSimulado.dll</c>).</param>
/// <param name="CarpetaDatos">Carpeta de la app (<c>%LOCALAPPDATA%\RadarEmpleo</c>): ahí van <c>logs\</c> y <c>ejecuciones\</c>,
/// que tienen datos personales y nunca se sirven por la API.</param>
/// <param name="TopeGastoDiarioUsd">Tope de gasto de un día de Madrid.</param>
/// <param name="VersionProbada">Versión de Claude Code con la que se han comprobado los argumentos.</param>
public sealed record OpcionesEjecutor(
    string RutaClaude,
    IReadOnlyList<string> ArgumentosPrevios,
    string CarpetaDatos,
    decimal TopeGastoDiarioUsd,
    string VersionProbada)
{
    /// <summary>Cada cuánto se vuelve a mirar la cola y el bloqueo ocupado. Los tests lo bajan.</summary>
    public TimeSpan IntervaloSondeo { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Cuánto se espera a que se libere el bloqueo antes de dar la ejecución por fallida.</summary>
    public TimeSpan EsperaMaximaBloqueo { get; init; } = TimeSpan.FromMinutes(60);

    /// <summary>Los logs y las carpetas de ejecución más antiguos se borran al arrancar.</summary>
    public TimeSpan Retencion { get; init; } = TimeSpan.FromDays(60);

    /// <summary>
    /// Base de datos cuya cola lleva esta API (<see cref="ExclusionBaseDatos"/>): solo una API por BD recupera y lanza.
    /// Null (los tests del procesador) es sin exclusión.
    /// </summary>
    public string? RutaExclusion { get; init; }

    /// <summary>
    /// El <c>--model</c> de ExtraerRegistro si no es el de su definición («haiku»); null o vacío, el de la definición. Lo
    /// comprueba la API al arrancar (<c>ConfiguracionAgentes.ExigirModeloValido</c>).
    /// </summary>
    public string? ModeloExtraerRegistro { get; init; }

    /// <summary>Logs de las ejecuciones (<c>ejecucion-&lt;id&gt;.log</c>).</summary>
    public string CarpetaLogs => Path.Combine(CarpetaDatos, "logs");

    /// <summary>Carpetas de trabajo de las tareas que no trabajan en Curriculum (<c>ejecuciones\&lt;id&gt;\</c>).</summary>
    public string CarpetaEjecuciones => Path.Combine(CarpetaDatos, "ejecuciones");
}
