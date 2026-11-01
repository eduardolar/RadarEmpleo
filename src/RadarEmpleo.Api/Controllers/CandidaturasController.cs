using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Api.Controllers;

/// <summary>Candidaturas: listado, ficha, alta, edición, borrado, cambio de estado y notas.</summary>
[ApiController]
[Route("api/candidaturas")]
[Produces("application/json")]
public class CandidaturasController(CandidaturasService candidaturas) : ControllerBase
{
    /// <summary>Lista las candidaturas (más recientes primero) con filtros opcionales.</summary>
    /// <param name="estado">Solo las de este estado.</param>
    /// <param name="texto">Busca en puesto, empresa, ubicación, portal y pega (sin distinguir tildes).</param>
    /// <param name="desde">Solo las detectadas desde este momento. Una fecha sola (2026-10-27) es desde la medianoche de Madrid.</param>
    [HttpGet]
    public Task<List<CandidaturaResumenDto>> Listar(
        [FromQuery] EstadoCandidatura? estado, [FromQuery] string? texto, [FromQuery] DateTime? desde) =>
        candidaturas.ListarAsync(new FiltroCandidaturas(estado, texto, desde));

    /// <summary>Ficha completa con historial, notas y recordatorios.</summary>
    [HttpGet("{id:int}", Name = "ObtenerCandidatura")]
    [ProducesResponseType<CandidaturaDetalleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CandidaturaDetalleDto>> Obtener(int id) =>
        await candidaturas.ObtenerAsync(id) is { } detalle ? detalle : NotFound();

    /// <summary>Da de alta una candidatura en «Detectada» (crea o reutiliza la empresa por nombre).</summary>
    [HttpPost]
    [ProducesResponseType<CandidaturaDetalleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CandidaturaDetalleDto>> Crear(GuardarCandidaturaRequest datos)
    {
        var detalle = await candidaturas.CrearAsync(datos);
        return CreatedAtRoute("ObtenerCandidatura", new { id = detalle.Id }, detalle);
    }

    /// <summary>Edita los datos de la candidatura (el estado se cambia con <c>POST {id}/estado</c>).</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<CandidaturaDetalleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CandidaturaDetalleDto>> Editar(int id, GuardarCandidaturaRequest datos) =>
        await candidaturas.EditarAsync(id, datos) is { } detalle ? detalle : NotFound();

    /// <summary>Borra la candidatura con su historial, notas y recordatorios.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Borrar(int id) =>
        await candidaturas.BorrarAsync(id) ? NoContent() : NotFound();

    /// <summary>
    /// Cambia el estado. Devuelve 400 si falta el estado o no existe, y 409 si la transición no está
    /// permitida o si se descarta sin motivo. Al pasar a «Enviada» se rellena la fecha de envío y se
    /// programa un seguimiento a las 09:00 (Madrid) a +7 días; al descartar, cerrar o reabrir, los
    /// seguimientos pendientes se dan por hechos.
    /// </summary>
    [HttpPost("{id:int}/estado")]
    [ProducesResponseType<CandidaturaDetalleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CandidaturaDetalleDto>> CambiarEstado(int id, CambiarEstadoRequest peticion) =>
        await candidaturas.CambiarEstadoAsync(id, peticion) is { } detalle ? detalle : NotFound();

    /// <summary>Añade una nota con la fecha de ahora.</summary>
    [HttpPost("{id:int}/notas")]
    [ProducesResponseType<NotaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotaDto>> AnadirNota(int id, CrearNotaRequest peticion)
    {
        var nota = await candidaturas.AnadirNotaAsync(id, peticion);
        if (nota is null) return NotFound();
        return CreatedAtRoute("ObtenerCandidatura", new { id }, nota);
    }
}
