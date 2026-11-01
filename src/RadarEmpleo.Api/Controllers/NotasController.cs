using Microsoft.AspNetCore.Mvc;
using RadarEmpleo.Services;

namespace RadarEmpleo.Api.Controllers;

/// <summary>Notas sueltas por id (se crean desde <c>POST /api/candidaturas/{id}/notas</c>).</summary>
[ApiController]
[Route("api/notas")]
[Produces("application/json")]
public class NotasController(CandidaturasService candidaturas) : ControllerBase
{
    /// <summary>Borra una nota.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Borrar(int id) =>
        await candidaturas.BorrarNotaAsync(id) ? NoContent() : NotFound();
}
