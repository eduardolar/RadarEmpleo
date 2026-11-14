using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Api.Controllers;

/// <summary>Lo que pinta la pantalla Hoy.</summary>
[ApiController]
[Route("api/panel")]
[Produces("application/json")]
public class PanelController(RecordatoriosService recordatorios) : ControllerBase
{
    /// <summary>
    /// Recordatorios para hoy (con los vencidos), de los próximos 7 días, de más adelante y hechos hoy, y
    /// las candidaturas enviadas hace entre 7 y 30 días que siguen sin respuesta. En <c>resumen</c> van las
    /// candidaturas activas, las entrevistas de esta semana, la tasa de respuesta y los pendientes de cada
    /// día de la semana (de lunes a domingo), además del día del primer pendiente posterior a esa semana.
    /// Cada recordatorio de una candidatura lleva el enlace a su oferta. Los días son los de Madrid.
    /// </summary>
    [HttpGet("hoy")]
    public Task<PanelHoyDto> Hoy() => recordatorios.PanelHoyAsync();
}
