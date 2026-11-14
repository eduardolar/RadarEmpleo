using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Api.Controllers;

/// <summary>
/// Recordatorios: los sueltos se crean aquí y los de una candidatura, en
/// <c>POST /api/candidaturas/{id}/recordatorios</c>. El día y la hora son siempre de Madrid.
/// </summary>
[ApiController]
[Route("api/recordatorios")]
[Produces("application/json")]
public class RecordatoriosController(RecordatoriosService recordatorios) : ControllerBase
{
    public const string RutaObtener = "ObtenerRecordatorio";

    /// <summary>Un recordatorio por su id.</summary>
    [HttpGet("{id:int}", Name = RutaObtener)]
    [ProducesResponseType<RecordatorioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecordatorioDto>> Obtener(int id) =>
        await recordatorios.ObtenerAsync(id) is { } recordatorio ? recordatorio : NotFound();

    /// <summary>Apunta un recordatorio suelto (por ejemplo, una publicación de LinkedIn). Sin hora, las 09:00.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<RecordatorioDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<RecordatorioDto>> Crear(GuardarRecordatorioRequest datos)
    {
        var recordatorio = (await recordatorios.CrearAsync(datos))!;
        return CreatedAtRoute(RutaObtener, new { id = recordatorio.Id }, recordatorio);
    }

    /// <summary>
    /// Cambia el tipo, el título, el día, la hora y las notas. No cambia si está hecho ni de qué
    /// candidatura es. Devuelve 409 si otra petición lo ha cambiado entre medias.
    /// </summary>
    [HttpPut("{id:int}")]
    [Consumes("application/json")]
    [ProducesResponseType<RecordatorioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<RecordatorioDto>> Editar(int id, GuardarRecordatorioRequest datos) =>
        await recordatorios.EditarAsync(id, datos) is { } recordatorio ? recordatorio : NotFound();

    /// <summary>
    /// Lo marca como hecho (<c>{"hecho": true}</c>) o lo deja pendiente otra vez (<c>false</c>). Repetirlo
    /// no cambia nada. Devuelve 409 si otra petición lo ha cambiado entre medias.
    /// </summary>
    [HttpPost("{id:int}/hecho")]
    [Consumes("application/json")]
    [ProducesResponseType<RecordatorioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<RecordatorioDto>> MarcarHecho(int id, MarcarHechoRequest peticion) =>
        await recordatorios.MarcarHechoAsync(id, peticion) is { } recordatorio ? recordatorio : NotFound();

    /// <summary>
    /// Lo pasa a otro día (de Madrid), a la misma hora. Admite días pasados, para poder deshacerlo.
    /// Devuelve 409 si ya está hecho o si otra petición lo ha cambiado entre medias.
    /// </summary>
    [HttpPost("{id:int}/posponer")]
    [Consumes("application/json")]
    [ProducesResponseType<RecordatorioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<RecordatorioDto>> Posponer(int id, PosponerRecordatorioRequest peticion) =>
        await recordatorios.PosponerAsync(id, peticion) is { } recordatorio ? recordatorio : NotFound();

    /// <summary>Borra un recordatorio.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Borrar(int id) =>
        await recordatorios.BorrarAsync(id) ? NoContent() : NotFound();
}
