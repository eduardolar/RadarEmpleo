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
/// Lo fijo de una tarea: presupuesto, tiempo, carpeta y herramientas. Vive en código: el cliente nunca lo cambia, y de la
/// configuración solo sale el modelo de ExtraerRegistro (<see cref="OpcionesEjecutor.ModeloExtraerRegistro"/>), que no da
/// más presupuesto, tiempo ni herramientas. En las listas, las marcas <see cref="DefinicionesTareas.MarcaNombreInforme"/> y
/// <see cref="DefinicionesTareas.MarcaCarpetaDatos"/> las sustituye <see cref="ConstructorArgumentos"/>.
/// </summary>
/// <param name="Herramientas">El valor de <c>--tools</c> ("" para ninguna).</param>
/// <param name="Permitidas">El de <c>--allowedTools</c>, o null para no pasarlo.</param>
/// <param name="Denegadas">El de <c>--disallowedTools</c>, o null para no pasarlo.</param>
/// <param name="Modelo">El de <c>--model</c>, o null para el de por defecto.</param>
/// <param name="PresupuestoUsd">El de toda la ejecución: lo que se reserva al encolar y, si va por partes, la suma de todas.</param>
/// <param name="TiempoMaximo">El de toda la ejecución (también por partes): la caducidad del bloqueo sale de él.</param>
/// <param name="SalidaConDatosPersonales">Lo que responde son datos de Eduardo (las líneas de su registro): el log no guarda
/// la salida, solo su tamaño, su hash, el subtype, el coste y los turnos.</param>
/// <param name="Partes">Si la tarea puede lanzarse en varias consultas a Claude, en serie y dentro de la misma ejecución
/// (<see cref="PreparacionTarea.ConClaude(IReadOnlyList{PeticionClaude})"/>); null si va siempre de una vez.</param>
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
    bool SalidaConDatosPersonales = false,
    PartesTarea? Partes = null)
{
    /// <summary>
    /// El presupuesto de cada una de <paramref name="partes"/> partes: el total repartido (redondeado hacia abajo al
    /// céntimo) y como mucho <see cref="PartesTarea.PresupuestoMaximoParte"/>. Así la suma nunca pasa del total. Sin
    /// <see cref="Partes"/>, el total.
    /// </summary>
    /// <exception cref="ReglaNegocioException">El reparto no llega a un céntimo por parte
    /// (<see cref="MensajesEjecucion.PresupuestoNoLlegaParaLasPartes"/>).</exception>
    public decimal PresupuestoParte(int partes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partes);
        if (Partes is null) return PresupuestoUsd;
        var parte = Math.Min(Partes.PresupuestoMaximoParte, Math.Floor(PresupuestoUsd * 100 / partes) / 100);
        return parte > 0 ? parte : throw new ReglaNegocioException(MensajesEjecucion.PresupuestoNoLlegaParaLasPartes);
    }

    /// <summary>
    /// Las peticiones que ha preparado el manejador caben en la tarea: una sola siempre (como antes de las partes); varias,
    /// solo si la tarea va por partes, no pasan de <see cref="PartesTarea.MaximoPartes"/>, cada una lleva su
    /// <c>--max-budget-usd</c> y ni la suma de los tiempos ni la de los presupuestos pasan de los de la tarea (el bloqueo
    /// caduca con <see cref="TiempoMaximo"/> y al encolar se reserva <see cref="PresupuestoUsd"/>).
    /// </summary>
    public bool CabenLasPartes(IReadOnlyList<PeticionClaude> peticiones)
    {
        if (peticiones.Count <= 1) return true;
        if (Partes is null || peticiones.Count > Partes.MaximoPartes) return false;
        if (peticiones.Aggregate(TimeSpan.Zero, (suma, p) => suma + p.TiempoMaximo) > TiempoMaximo) return false;
        var presupuestos = peticiones.Select(p => ConstructorArgumentos.PresupuestoDe(p.Argumentos)).ToList();
        return presupuestos.All(p => p is > 0) && presupuestos.Sum() <= PresupuestoUsd;
    }

    /// <summary>El tiempo máximo de cada parte: el total repartido y como mucho <see cref="PartesTarea.TiempoMaximoParte"/>. Sin <see cref="Partes"/>, el total.</summary>
    public TimeSpan TiempoParte(int partes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partes);
        if (Partes is null) return TiempoMaximo;
        var repartido = TimeSpan.FromTicks(TiempoMaximo.Ticks / partes);
        return repartido < Partes.TiempoMaximoParte ? repartido : Partes.TiempoMaximoParte;
    }
}

/// <summary>Cómo se reparte en consultas una tarea que lee un texto largo (decisión de Eduardo del 24/11).</summary>
/// <param name="LargoParte">Lo que mide como mucho el texto de cada parte en el prompt.</param>
/// <param name="MaximoPartes">Con más partes no se lanza.</param>
/// <param name="PresupuestoMaximoParte">El tope de <c>--max-budget-usd</c> de una parte, aunque el reparto dé más.</param>
/// <param name="TiempoMaximoParte">El tiempo máximo de una parte, aunque el reparto dé más.</param>
public sealed record PartesTarea(int LargoParte, int MaximoPartes, decimal PresupuestoMaximoParte, TimeSpan TiempoMaximoParte);

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

    // Por partes (decisión de Eduardo del 24/11, tras cortarse a los 10 min la prueba B con unos 64 KB de texto libre): hasta
    // 6 partes de unos 16.000 caracteres. Con «haiku» (decisión del 24/11, tras cortarse la prueba B2 por presupuesto en la
    // primera parte con «sonnet»); se cambia con Agentes:ModeloExtraerRegistro. Con la salida corta (solo el número de línea
    // y lo que hay que interpretar), 1,50 y 30 min en total y como mucho 0,50 y 5 min cada una: con 4 partes, 0,37 y 5 min;
    // con 6, 0,25 y 5 min. El bloqueo caduca a los 35 min. El corte por presupuesto no ahorra nada (llega tras el turno, que
    // se paga entero), así que los topes llevan margen.
    public static readonly DefinicionTarea ExtraerRegistro = new(
        TareaAgente.ExtraerRegistro, 1.50m, TimeSpan.FromMinutes(30), CarpetaTrabajo.Ejecucion,
        Herramientas: "",
        Permitidas: null,
        Denegadas: null,
        Modelo: "haiku",
        NombrePrompt: "extraer-registro",
        SalidaConDatosPersonales: true,
        Partes: new PartesTarea(LargoParte: 16_000, MaximoPartes: 6, PresupuestoMaximoParte: 0.50m, TiempoMaximoParte: TimeSpan.FromMinutes(5)));

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
