using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Api.Controllers;

/// <summary>
/// Documentos de la ficha: sugerencias, quitar el vínculo y el archivo para verlo (se vinculan desde
/// <c>POST /api/candidaturas/{id}/documentos</c>). La app nunca borra ni cambia un archivo.
/// </summary>
[ApiController]
[Route("api/documentos")]
[Produces("application/json")]
public class DocumentosController(DocumentosService documentos) : ControllerBase
{
    /// <summary>
    /// Archivos de Curriculum que se proponen para la candidatura (máximo 10, el más probable primero).
    /// Devuelve 400 sin <c>candidaturaId</c>, 404 si la candidatura no existe y 409 sin carpeta Curriculum.
    /// </summary>
    [HttpGet("sugeridos")]
    [ProducesResponseType<List<DocumentoSugeridoDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<List<DocumentoSugeridoDto>>> Sugeridos([FromQuery] int? candidaturaId)
    {
        if (candidaturaId is not { } id)
            throw new ValidacionException("Falta la candidatura.", "candidaturaId");
        return await documentos.SugeridosAsync(id) is { } sugeridos ? sugeridos : NotFound();
    }

    /// <summary>Quita el documento de la ficha. El archivo NO se borra: sigue en su carpeta.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Quitar(int id) =>
        await documentos.QuitarAsync(id) ? NoContent() : NotFound();

    /// <summary>
    /// El archivo del documento: un PDF para verlo dentro de la ficha (inline) o un .docx para descargarlo
    /// (attachment). Admite rangos. Da 404, sin más detalle, si el documento no existe, falta el archivo o
    /// la ruta ya no es segura.
    /// </summary>
    [HttpGet("{id:int}/archivo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archivo(int id)
    {
        var leido = await documentos.AbrirArchivoAsync(id);
        if (leido is null) return NotFound();

        var esPdf = leido.TipoContenido == "application/pdf";
        var disposicion = new ContentDispositionHeaderValue(esPdf ? "inline" : "attachment") { FileNameStar = leido.NombreArchivo };
        var cabeceras = Response.Headers;
        cabeceras.ContentDisposition = disposicion.ToString();
        cabeceras.XContentTypeOptions = "nosniff";
        // Sin «sandbox»: Chrome no enseña un PDF en un iframe con sandbox.
        cabeceras.ContentSecurityPolicy = "frame-ancestors 'self'";
        cabeceras.XFrameOptions = "SAMEORIGIN";
        cabeceras["Cross-Origin-Resource-Policy"] = "same-origin";
        cabeceras.CacheControl = "no-store";

        return File(leido.Contenido, leido.TipoContenido, enableRangeProcessing: true);
    }
}
