using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Api.Errores;
using RadarEmpleo.Api.Seguridad;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Agentes;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Api.Controllers;

/// <summary>
/// Agentes de Claude: analizar el encaje de una candidatura, extraer el texto libre del registro, estado y gasto de hoy,
/// lista y detalle de las ejecuciones, cancelar y reintentar. Solo desde el PC (endpoint «Local»), con <c>Agentes:Habilitado</c> y en modo Local: si no, 403.
/// Los POST solo admiten <c>{}</c> (415 con un formulario o <c>text/plain</c>) y nada de lo que llega va al prompt. Sin
/// <c>[Consumes]</c> a propósito: es una restricción de enrutado y daría el 415 antes que el 403; el 415 lo da el enlace
/// de <see cref="PeticionVacia"/>, que ya va después del filtro. El rastreo llega en la F7c.
/// </summary>
[ApiController]
[Route("api/agentes")]
[Produces("application/json")]
[SoloPcConAgentes]
public class AgentesController(AgentesService agentes, DisponibilidadAgentes disponibilidad) : ControllerBase
{
    public const string RutaObtener = "ObtenerEjecucion";

    /// <summary>El <c>detail</c> del 404 de una ejecución que no existe (verla, cancelarla o reintentarla).</summary>
    public const string MensajeEjecucionNoEncontrada = "Ya no se encuentra esta tarea de Claude.";

    /// <summary>Si están disponibles, la versión de Claude Code, el gasto de hoy y lo que está en curso y en cola.</summary>
    [HttpGet("estado")]
    [ProducesResponseType<EstadoAgentesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<EstadoAgentesDto> Estado(CancellationToken cancelacion)
    {
        var motivo = disponibilidad.Motivo(HttpContext);
        return await agentes.EstadoAsync(motivo is null, motivo, cancelacion);
    }

    /// <summary>
    /// Analiza con Claude el encaje de una candidatura: 202 con la ejecución y su <c>Location</c>, o 200 con la que ya
    /// estaba en cola o en curso para ella. 404 si no existe; 409 si está descartada o cerrada, sin carpeta Curriculum o
    /// con el tope de hoy. Lo que va al prompt sale de la BD al prepararla, nunca de la petición.
    /// </summary>
    [HttpPost("analizar/{candidaturaId:int}")]
    [ProducesResponseType<EjecucionAgenteDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<EjecucionAgenteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<EjecucionAgenteDto>> Analizar(int candidaturaId, PeticionVacia cuerpo, CancellationToken cancelacion) =>
        Encolada(await agentes.EncolarAsync(TareaAgente.AnalizarEncaje, candidaturaId, cancelacion));

    /// <summary>
    /// Extrae con Claude las candidaturas del texto libre del registro, sin la tabla ni los rastreos: 202 con la ejecución
    /// y su <c>Location</c>, o 200 con la que ya estaba en cola o en curso. 409 sin carpeta Curriculum, sin registro o con
    /// el tope de hoy. No crea nada: lo que encuentra se ve y se elige en la vista previa de la importación.
    /// </summary>
    [HttpPost("extraer-registro")]
    [ProducesResponseType<EjecucionAgenteDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<EjecucionAgenteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<EjecucionAgenteDto>> ExtraerRegistro(PeticionVacia cuerpo, CancellationToken cancelacion) =>
        Encolada(await agentes.EncolarAsync(TareaAgente.ExtraerRegistro, cancelacion: cancelacion));

    /// <summary>Las últimas ejecuciones, de la más reciente a la más antigua. <paramref name="limite"/> va de 1 a 200 (si no, 400).</summary>
    [HttpGet("ejecuciones")]
    [ProducesResponseType<List<EjecucionAgenteDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<List<EjecucionAgenteDto>> Listar([FromQuery] TareaAgente? tarea, [FromQuery] int? candidaturaId,
        [FromQuery] int limite = AgentesService.LimitePorDefecto, CancellationToken cancelacion = default) =>
        agentes.ListarAsync(tarea, candidaturaId, limite, cancelacion);

    /// <summary>Una ejecución (el front la pide cada 5 s mientras no ha terminado).</summary>
    [HttpGet("ejecuciones/{id:int}", Name = RutaObtener)]
    [ProducesResponseType<EjecucionAgenteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EjecucionAgenteDto>> Obtener(int id, CancellationToken cancelacion) =>
        await agentes.ObtenerAsync(id, cancelacion) is { } ejecucion ? ejecucion : EjecucionNoEncontrada();

    /// <summary>
    /// Cancela: en cola queda Cancelada al momento; en curso se corta el proceso y queda Cancelada en unos segundos
    /// (mientras, <c>puedeCancelar</c> es false). 409 si ya ha terminado.
    /// </summary>
    [HttpPost("ejecuciones/{id:int}/cancelar")]
    [ProducesResponseType<EjecucionAgenteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<EjecucionAgenteDto>> Cancelar(int id, PeticionVacia cuerpo, CancellationToken cancelacion) =>
        await agentes.CancelarAsync(id, cancelacion) is { } ejecucion ? ejecucion : EjecucionNoEncontrada();

    /// <summary>
    /// Lanza otra vez la misma tarea con los mismos parámetros (revalidados): 202 con la ejecución nueva y su
    /// <c>Location</c>, o 200 con la que ya estaba activa. 409 si todavía no ha terminado o ya no se puede.
    /// </summary>
    [HttpPost("ejecuciones/{id:int}/reintentar")]
    [ProducesResponseType<EjecucionAgenteDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<EjecucionAgenteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<EjecucionAgenteDto>> Reintentar(int id, PeticionVacia cuerpo, CancellationToken cancelacion) =>
        await agentes.ReintentarAsync(id, cancelacion) is { } encolada ? Encolada(encolada) : EjecucionNoEncontrada();

    private static ActionResult EjecucionNoEncontrada() => RespuestasValidacion.NoEncontrado(MensajeEjecucionNoEncontrada);

    /// <summary>202 con <c>Location</c> si es nueva; 200 si ya había una activa; 404 si lo pedido no existe.</summary>
    private ActionResult<EjecucionAgenteDto> Encolada((EjecucionAgenteDto Ejecucion, bool Nueva)? encolada) => encolada switch
    {
        null => NotFound(),
        { Nueva: true } nueva => AcceptedAtRoute(RutaObtener, new { id = nueva.Ejecucion.Id }, nueva.Ejecucion),
        { } activa => Ok(activa.Ejecucion)
    };
}
