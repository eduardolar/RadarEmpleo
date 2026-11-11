using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Rastreos;

namespace RadarEmpleo.Api.Controllers;

/// <summary>
/// Bandeja de rastreos: los informes leídos de Curriculum y sus ofertas, para pasarlas al tablero o dejarlas
/// de lado. Los POST piden el cuerpo <c>{}</c> en JSON: un formulario de otra web abierta en el navegador
/// recibe un 415 y no cambia nada.
/// </summary>
[ApiController]
[Route("api/rastreos")]
[Produces("application/json")]
public class RastreosController(RastreosService rastreos) : ControllerBase
{
    /// <summary>Los rastreos, del más reciente al más antiguo.</summary>
    [HttpGet]
    public Task<List<RastreoResumenDto>> Listar() => rastreos.ListarAsync();

    /// <summary>Un rastreo con su informe y sus ofertas.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<RastreoDetalleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RastreoDetalleDto>> Obtener(int id) =>
        await rastreos.ObtenerAsync(id) is { } detalle ? detalle : NotFound();

    /// <summary>
    /// Lee los informes de Curriculum y guarda lo nuevo. Repetirlo no duplica nada ni cambia el estado de
    /// ninguna oferta. Devuelve 409 si falta la carpeta Curriculum.
    /// </summary>
    [HttpPost("escanear")]
    [Consumes("application/json")]
    [ProducesResponseType<ResultadoEscaneoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public Task<ResultadoEscaneoDto> Escanear(PeticionVacia cuerpo) => rastreos.EscanearAsync();

    /// <summary>
    /// «Pasar al tablero»: crea la candidatura en Detectada (o enlaza la que ya tenga ese enlace, con
    /// <c>yaExistia</c>). Repetirlo no crea otra. Devuelve 404 si la oferta no existe.
    /// </summary>
    [HttpPost("ofertas/{id:int}/detectar")]
    [Consumes("application/json")]
    [ProducesResponseType<ResultadoDeteccionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<ResultadoDeteccionDto>> Detectar(int id, PeticionVacia cuerpo) =>
        await rastreos.DetectarAsync(id) is { } resultado ? resultado : NotFound();

    /// <summary>«No me interesa». Devuelve 404 si la oferta no existe y 409 si ya está en el tablero.</summary>
    [HttpPost("ofertas/{id:int}/ignorar")]
    [Consumes("application/json")]
    [ProducesResponseType<OfertaRastreoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<OfertaRastreoDto>> Ignorar(int id, PeticionVacia cuerpo) =>
        await rastreos.IgnorarAsync(id) is { } oferta ? oferta : NotFound();
}
