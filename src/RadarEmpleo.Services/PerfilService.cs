using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Perfil;

namespace RadarEmpleo.Services;

/// <summary>
/// El perfil de búsqueda de Eduardo, de solo lectura: lo escribe el equipo /empleo en
/// <c>perfil-busqueda.md</c> y la app solo lo enseña.
/// </summary>
public sealed class PerfilService
{
    private readonly ILectorCurriculum _lector;

    public PerfilService(ILectorCurriculum lector) => _lector = lector;

    /// <summary>El perfil partido en secciones, con la fecha de su última modificación.</summary>
    /// <exception cref="ReglaNegocioException">Falta la carpeta Curriculum o el perfil, o no se puede leer (409).</exception>
    public async Task<PerfilDto> ObtenerAsync(CancellationToken cancelacion = default)
    {
        var leido = await _lector.LeerPerfilAsync(cancelacion);
        var perfil = AnalizadorPerfil.Analizar(leido.Texto);
        return new PerfilDto(perfil.Titulo, leido.FechaModificacion, perfil.Introduccion, perfil.Secciones);
    }
}
