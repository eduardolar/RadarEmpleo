using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Api.Seguridad;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Importacion;

namespace RadarEmpleo.Api.Controllers;

/// <summary>
/// Importación del registro de candidaturas: vista previa de la tabla o de la tabla más una extracción de Claude, y
/// aplicar. El registro nunca se escribe.
/// </summary>
[ApiController]
[Route("api/importacion")]
[Produces("application/json")]
public class ImportacionController(ImportacionService importacion) : ControllerBase
{
    /// <summary>
    /// Lee la tabla del registro y dice qué pasaría con cada fila. No escribe nada. Devuelve 409 si falta
    /// la carpeta Curriculum, el registro o la tabla.
    /// </summary>
    [HttpPost("tabla/previsualizar")]
    [ProducesResponseType<VistaPreviaImportacionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<VistaPreviaImportacionDto> PrevisualizarTabla() => importacion.PrevisualizarTablaAsync();

    /// <summary>
    /// Une la tabla y lo que una ExtraerRegistro ha leído del texto libre, y dice qué pasaría con cada fila. No escribe
    /// nada. Solo desde el PC con los agentes encendidos (403) y con <c>{}</c> (415). 404 si la ejecución no existe; 409
    /// si no es una extracción terminada bien, si el registro ha cambiado desde que la hizo o si falta el registro. Sin
    /// <c>[Consumes]</c>, como en <see cref="AgentesController"/>: el 403 va antes que el 415.
    /// </summary>
    [HttpPost("extraccion/{ejecucionId:int}/previsualizar")]
    [SoloPcConAgentes]
    [ProducesResponseType<VistaPreviaImportacionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<VistaPreviaImportacionDto>> PrevisualizarExtraccion(int ejecucionId, PeticionVacia cuerpo) =>
        await importacion.PrevisualizarExtraccionAsync(ejecucionId) is { } vista ? vista : NotFound();

    /// <summary>
    /// Vuelve a leer el registro, rehace el lote, lo compara otra vez y aplica las claves pedidas. Los conflictos no
    /// dan error: se saltan y se cuentan. Devuelve 400 si el origen, la ejecución, el hash o las claves no son válidos,
    /// y 409 si el registro ha cambiado desde la vista previa (por su hash), si la extracción ya no vale o si otra
    /// aplicación se ha adelantado (en esos casos no cambia nada). No lanza nada: no lleva <c>[SoloPcConAgentes]</c>.
    /// </summary>
    [HttpPost("lote/aplicar")]
    [ProducesResponseType<ResultadoAplicacionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ResultadoAplicacionDto> AplicarLote(AplicarImportacionRequest peticion) => importacion.AplicarAsync(peticion);
}
