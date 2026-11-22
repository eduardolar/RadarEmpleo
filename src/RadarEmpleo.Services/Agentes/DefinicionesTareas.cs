using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Agentes;

/// <summary>Dónde trabaja Claude en una tarea.</summary>
public enum CarpetaTrabajo
{
    /// <summary>La carpeta Curriculum (solo puede leer o escribir lo que dejen sus reglas).</summary>
    Curriculum,
    /// <summary>Una carpeta vacía de la ejecución, <c>ejecuciones\&lt;id&gt;\</c>.</summary>
    Ejecucion
}

/// <summary>
/// Lo fijo de una tarea: presupuesto, tiempo, carpeta y herramientas. Vive en código: ni la configuración ni el
/// cliente pueden ampliarlo. En las listas, las marcas <see cref="DefinicionesTareas.MarcaNombreInforme"/> y
/// <see cref="DefinicionesTareas.MarcaCarpetaDatos"/> las sustituye <see cref="ConstructorArgumentos"/>.
/// </summary>
/// <param name="Herramientas">El valor de <c>--tools</c> ("" para ninguna).</param>
/// <param name="Permitidas">El de <c>--allowedTools</c>, o null para no pasarlo.</param>
/// <param name="Denegadas">El de <c>--disallowedTools</c>, o null para no pasarlo.</param>
/// <param name="Modelo">El de <c>--model</c>, o null para el de por defecto.</param>
/// <param name="SalidaConDatosPersonales">Lo que responde son datos de Eduardo (las líneas de su registro): el log no guarda
/// la salida, solo su tamaño, su hash, el subtype, el coste y los turnos.</param>
public sealed record DefinicionTarea(
    TareaAgente Tarea,
    decimal PresupuestoUsd,
    TimeSpan TiempoMaximo,
    CarpetaTrabajo Carpeta,
    string Herramientas,
    string? Permitidas,
    string? Denegadas,
    string? Modelo,
    string NombrePrompt,
    bool SalidaConDatosPersonales = false);

/// <summary>
/// Las tres tareas de la F7 (apartado 4.4 del plan, con lo que fijó el paso 0). AnalizarEncaje y ExtraerRegistro van
/// sin ninguna herramienta (<c>--tools ""</c>): la app les da todo en el prompt (decisión de Eduardo del 14/11: la app
/// descarga la oferta en la F7b y el agente no abre webs ni lee archivos). Rastreo lee solo de la carpeta de
/// trabajo (<c>Read(./**)</c>, que también vale para Grep y Glob; fuera de ella Claude las deniega), y además se
/// deniegan expresamente <c>~/.claude</c>, la carpeta de datos de la app, <c>appsettings*.json</c> y las carpetas
/// personales. Ninguna tarea tiene Bash, Agent ni Skill. Redactar, Revisar, RellenarEnvio y ConfirmarEnvio todavía
/// no tienen definición.
/// </summary>
public static class DefinicionesTareas
{
    /// <summary>Se sustituye por <c>rastreo_AAAA-MM-DD(_N).md</c>.</summary>
    public const string MarcaNombreInforme = "<nombre>";

    /// <summary>Se sustituye por la carpeta de datos de la app en la forma de las reglas de Claude (<c>//c/Users/…</c>).</summary>
    public const string MarcaCarpetaDatos = "<datos>";

    /// <summary>Lo que ninguna tarea puede leer, aunque esté dentro de su carpeta de trabajo.</summary>
    public const string LecturasDenegadas =
        $"Read(~/.claude/**),Read({MarcaCarpetaDatos}/**),Read(./**/appsettings*.json),Read(./Documentos personales/**),Read(./Marca personal/**)";

    // Sin herramientas: el texto de la oferta (descargado por la app en la F7b), el perfil y el CV van como DATOS en el
    // prompt. Trabaja en su carpeta vacía de ejecución, como ExtraerRegistro.
    public static readonly DefinicionTarea AnalizarEncaje = new(
        TareaAgente.AnalizarEncaje, 0.50m, TimeSpan.FromMinutes(5), CarpetaTrabajo.Ejecucion,
        Herramientas: "",
        Permitidas: null,
        Denegadas: null,
        Modelo: null,
        NombrePrompt: "analizar-encaje");

    public static readonly DefinicionTarea ExtraerRegistro = new(
        TareaAgente.ExtraerRegistro, 1.00m, TimeSpan.FromMinutes(10), CarpetaTrabajo.Ejecucion,
        Herramientas: "",
        Permitidas: null,
        Denegadas: null,
        Modelo: "sonnet",
        NombrePrompt: "extraer-registro",
        SalidaConDatosPersonales: true);

    // F7c: el alcance web del rastreo (WebFetch y WebSearch sin límite de dominio) se decide al llegar a la F7c.
    public static readonly DefinicionTarea Rastreo = new(
        TareaAgente.Rastreo, 3.00m, TimeSpan.FromMinutes(30), CarpetaTrabajo.Curriculum,
        Herramientas: "Read,Grep,Glob,Write,WebFetch,WebSearch,ToolSearch",
        Permitidas: $"Read(./**),WebFetch,WebSearch,ToolSearch,Edit(./rastreos/{MarcaNombreInforme})",
        Denegadas: $"Bash,NotebookEdit,Agent,Skill,Edit(./ofertas-enviadas.md),Edit(./perfil-busqueda.md),{LecturasDenegadas}",
        Modelo: null,
        NombrePrompt: "rastreo");

    /// <summary>Las que tienen definición.</summary>
    public static readonly IReadOnlyList<DefinicionTarea> Todas = [AnalizarEncaje, ExtraerRegistro, Rastreo];

    /// <summary>La definición de una tarea, o null si todavía no tiene (F8-F10).</summary>
    public static DefinicionTarea? De(TareaAgente tarea) => Todas.FirstOrDefault(d => d.Tarea == tarea);
}
