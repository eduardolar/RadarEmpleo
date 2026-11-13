using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Api.Controllers;

/// <summary>Candidaturas: listado, ficha, alta, edición, borrado, cambio de estado, notas y documentos.</summary>
[ApiController]
[Route("api/candidaturas")]
[Produces("application/json")]
public class CandidaturasController(CandidaturasService candidaturas, DocumentosService documentos) : ControllerBase
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

    /// <summary>
    /// Deshace el cambio de estado <c>cambioId</c> (el apunte del historial que creó el movimiento) si sigue
    /// siendo el último y tiene menos de 10 minutos: vuelve al estado anterior (aunque el flujo normal no
    /// deje retroceder), restaura las fechas y el motivo, y borra el apunte del historial y el seguimiento
    /// que programó (o reabre el que cerró). Devuelve 400 sin <c>cambioId</c>, 404 si la candidatura no
    /// existe y 409 si ese cambio ya no es el último, si solo tiene el alta, si ya ha pasado el tiempo o si
    /// otra petición la ha cambiado entre medias.
    /// </summary>
    [HttpPost("{id:int}/estado/deshacer")]
    [Consumes("application/json")]
    [ProducesResponseType<CandidaturaDetalleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<CandidaturaDetalleDto>> DeshacerCambioEstado(int id, DeshacerCambioEstadoRequest peticion) =>
        await candidaturas.DeshacerCambioEstadoAsync(id, peticion) is { } detalle ? detalle : NotFound();

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

    /// <summary>
    /// Vincula a la ficha un archivo de Curriculum (normalmente uno de <c>GET /api/documentos/sugeridos</c>)
    /// y lo deja como el vigente de su tipo. Devuelve 400 si la ruta no es válida, 404 si la candidatura no
    /// existe y 409 si no está el archivo, ya está vinculado o falta la carpeta Curriculum.
    /// </summary>
    [HttpPost("{id:int}/documentos")]
    [ProducesResponseType<DocumentoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentoDto>> VincularDocumento(int id, VincularDocumentoRequest peticion)
    {
        var documento = await documentos.VincularAsync(id, peticion);
        if (documento is null) return NotFound();
        return CreatedAtRoute("ObtenerCandidatura", new { id }, documento);
    }
}
