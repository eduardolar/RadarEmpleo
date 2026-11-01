using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Api.Controllers;

/// <summary>Empresas para el autocompletado del alta.</summary>
[ApiController]
[Route("api/empresas")]
[Produces("application/json")]
public class EmpresasController(EmpresasService empresas) : ControllerBase
{
    /// <summary>Empresas cuyo nombre contiene el texto (sin distinguir mayúsculas, tildes ni S.L./S.A.).</summary>
    [HttpGet]
    public Task<List<EmpresaDto>> Buscar([FromQuery] string? texto) => empresas.BuscarAsync(texto);
}
