using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Agentes;

/// <summary>Dónde y con qué log trabaja una ejecución (lo calcula el procesador).</summary>
/// <param name="CarpetaTrabajo">Curriculum o <c>ejecuciones\&lt;id&gt;\</c>, según la definición de la tarea.</param>
/// <param name="RutaLog">Ruta absoluta del log.</param>
/// <param name="CarpetaDatos">Carpeta de datos de la app, que la tarea nunca puede leer (<see cref="ValoresArgumentos"/>).</param>
/// <param name="HashRegistroAntes">El hash del registro que se guarda al empezar (<see cref="EjecucionAgente.HashRegistroAntes"/>),
/// o null si no se ha podido leer. El <c>ejecucion</c> que recibe el manejador es de antes de empezar y aún no lo tiene.</param>
public sealed record ContextoTarea(string CarpetaTrabajo, string RutaLog, string CarpetaDatos, string? HashRegistroAntes = null)
{
    /// <summary>La petición con lo fijo de la definición (tiempo máximo, salida privada) y lo que pone el manejador.</summary>
    public PeticionClaude Peticion(DefinicionTarea definicion, IReadOnlyList<string> argumentos, string entrada) =>
        Peticion(definicion, argumentos, entrada, definicion.TiempoMaximo, null);

    /// <summary>Una parte de una tarea por partes: con su tiempo máximo (<see cref="DefinicionTarea.TiempoParte"/>) y cuál es («2 de 4»).</summary>
    public PeticionClaude Peticion(DefinicionTarea definicion, IReadOnlyList<string> argumentos, string entrada, TimeSpan tiempoMaximo,
        string? parte) =>
        new(definicion.Tarea, argumentos, CarpetaTrabajo, entrada, tiempoMaximo, RutaLog, definicion.SalidaConDatosPersonales, parte);
}

/// <summary>Lo que deja una tarea al terminar bien: la frase para enseñar y la salida ya validada.</summary>
public sealed record ResultadoTarea(string Resumen, string? ResultadoJson);

/// <summary>
/// Lo que sale de preparar una ejecución: o la petición para Claude, o un resultado sin lanzarlo (por ejemplo, la
/// oferta ya no está publicada y no hay nada que analizar). Sin Claude, la ejecución termina en Ok con coste 0.
/// </summary>
public sealed record PreparacionTarea
{
    private PreparacionTarea(IReadOnlyList<PeticionClaude> peticiones, ResultadoTarea? sinClaude)
    {
        Peticiones = peticiones;
        SinClaude = sinClaude;
    }

    /// <summary>Lo que hay que mandar a Claude, en serie y en este orden (una por parte), o nada si la tarea ya ha terminado sin él.</summary>
    public IReadOnlyList<PeticionClaude> Peticiones { get; }

    /// <summary>El resultado de la tarea si no hace falta Claude, o null si hay que lanzarlo.</summary>
    public ResultadoTarea? SinClaude { get; }

    /// <summary>Hay que lanzar Claude con esta petición.</summary>
    public static PreparacionTarea ConClaude(PeticionClaude peticion) => new([peticion], null);

    /// <summary>Hay que lanzar Claude una vez por petición, una detrás de otra, dentro de la misma ejecución.</summary>
    /// <exception cref="ArgumentException">No hay ninguna petición.</exception>
    public static PreparacionTarea ConClaude(IReadOnlyList<PeticionClaude> peticiones) =>
        peticiones.Count > 0 ? new([.. peticiones], null) : throw new ArgumentException("Hace falta al menos una petición.", nameof(peticiones));

    /// <summary>La tarea ya ha terminado: no se lanza Claude y no gasta nada.</summary>
    public static PreparacionTarea Terminada(ResultadoTarea resultado) => new([], resultado);
}

/// <summary>
/// Lo propio de cada tarea: qué parámetros admite, qué prompt monta y qué hace con la respuesta. Se registra uno por
/// tarea (scoped); una tarea sin manejador no se puede encolar. Los de AnalizarEncaje y ExtraerRegistro llegan en la
/// F7b y el de Rastreo en la F7c.
/// </summary>
public interface IManejadorTarea
{
    /// <summary>Presupuesto, tiempo, carpeta y herramientas, fijos en código.</summary>
    DefinicionTarea Definicion { get; }

    /// <summary>
    /// Comprueba al encolar lo que se pide. Devuelve false si no existe (404). Lanza
    /// <see cref="ValidacionException"/> (400) o <see cref="ReglaNegocioException"/> (409) si no se puede.
    /// </summary>
    Task<bool> ValidarAsync(int? candidaturaId, CancellationToken cancelacion);

    /// <summary>
    /// Monta la petición con datos de la BD (nunca del cuerpo de una petición HTTP), o termina la tarea sin Claude
    /// (<see cref="PreparacionTarea.Terminada"/>). Corre ya en curso y con el bloqueo cogido.
    /// </summary>
    Task<PreparacionTarea> ConstruirAsync(EjecucionAgente ejecucion, ContextoTarea contexto, CancellationToken cancelacion);

    /// <summary>
    /// Valida y aplica una respuesta correcta de Claude. Lanza <see cref="ReglaNegocioException"/> con el mensaje para
    /// Eduardo si no se puede (por ejemplo, <see cref="MensajesEjecucion.SalidaNoValida"/>): la ejecución queda en Error.
    /// </summary>
    Task<ResultadoTarea> AplicarAsync(EjecucionAgente ejecucion, RespuestaClaude respuesta, CancellationToken cancelacion);

    /// <summary>
    /// Valida y aplica las respuestas de una tarea lanzada con <see cref="PreparacionTarea.ConClaude(IReadOnlyList{PeticionClaude})"/>:
    /// una por petición y en su orden, al menos una correcta. Las que no lo son (por tiempo, por presupuesto, salida no
    /// válida o un error de Claude; o sin lanzar, como <see cref="FalloClaude.NoInstalado"/>, si una anterior no encontró
    /// Claude) las decide el manejador. Lanza <see cref="ReglaNegocioException"/> como <see cref="AplicarAsync"/>. Por
    /// defecto solo admite una petición, y la aplica con <see cref="AplicarAsync"/>.
    /// </summary>
    Task<ResultadoTarea> AplicarPartesAsync(EjecucionAgente ejecucion,
        IReadOnlyList<(PeticionClaude Peticion, RespuestaClaude Respuesta)> partes, CancellationToken cancelacion) =>
        partes.Count == 1
            ? AplicarAsync(ejecucion, partes[0].Respuesta, cancelacion)
            : throw new InvalidOperationException("Esta tarea no se lanza por partes.");
}
