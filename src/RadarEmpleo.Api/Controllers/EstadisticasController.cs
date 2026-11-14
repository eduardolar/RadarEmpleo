using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Estadisticas;

namespace RadarEmpleo.Api.Controllers;

/// <summary>Cómo va la búsqueda. Solo lee.</summary>
[ApiController]
[Route("api/estadisticas")]
[Produces("application/json")]
public class EstadisticasController(EstadisticasService estadisticas) : ControllerBase
{
    /// <summary>
    /// Total y por estado, enviadas en las 8 últimas semanas (de lunes a domingo de Madrid, la actual la
    /// última), respuestas con los días medios hasta la respuesta y motivos de descarte de las descartadas.
    /// Va todo en una petición porque la página lo pinta junto.
    /// </summary>
    [HttpGet]
    public Task<EstadisticasDto> Obtener() => estadisticas.EstadisticasAsync();
}
