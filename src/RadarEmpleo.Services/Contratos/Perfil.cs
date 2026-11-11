namespace RadarEmpleo.Services.Contratos;

/// <summary>El perfil de búsqueda (<c>perfil-busqueda.md</c>) partido en secciones. Es de solo lectura.</summary>
/// <param name="Titulo">El del primer «# », o «Perfil de búsqueda» si no hay.</param>
/// <param name="FechaModificacion">Última vez que se cambió el archivo (UTC).</param>
/// <param name="Introduccion">Lo que va entre el título y la primera sección (Markdown), o null.</param>
/// <param name="Secciones">Una por cada «## », en el orden del archivo.</param>
public sealed record PerfilDto(
    string Titulo,
    DateTime FechaModificacion,
    string? Introduccion,
    IReadOnlyList<SeccionPerfilDto> Secciones);

/// <summary>Una sección «## » del perfil con su Markdown (sin el título).</summary>
public sealed record SeccionPerfilDto(string Titulo, string Markdown);
