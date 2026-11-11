using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Api.Controllers;

/// <summary>El perfil de búsqueda (<c>perfil-busqueda.md</c>), solo para leerlo: la app no lo cambia.</summary>
[ApiController]
[Route("api/perfil")]
[Produces("application/json")]
public class PerfilController(PerfilService perfil) : ControllerBase
{
    /// <summary>El perfil partido en secciones «## ». Devuelve 409 si falta la carpeta Curriculum o el perfil.</summary>
    [HttpGet]
    [ProducesResponseType<PerfilDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<PerfilDto> Obtener(CancellationToken cancelacion) => perfil.ObtenerAsync(cancelacion);
}
