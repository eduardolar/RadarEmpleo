using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Importacion;

namespace RadarEmpleo.Api.Controllers;

/// <summary>Importación del registro de candidaturas: vista previa y aplicar. El registro nunca se escribe.</summary>
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
    /// Vuelve a comparar el lote y aplica las claves pedidas. Los conflictos no dan error: se saltan y se
    /// cuentan. Devuelve 400 si el lote o las claves no son válidos, y 409 si otra aplicación se ha
    /// adelantado (en ese caso no cambia nada).
    /// </summary>
    [HttpPost("lote/aplicar")]
    [ProducesResponseType<ResultadoAplicacionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ResultadoAplicacionDto> AplicarLote(AplicarLoteRequest peticion) => importacion.AplicarAsync(peticion);
}
