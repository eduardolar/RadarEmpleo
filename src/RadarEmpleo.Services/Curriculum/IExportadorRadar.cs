namespace RadarEmpleo.Services.Curriculum;

/// <summary>
/// Genera <c>radar\candidaturas.md</c>: lo ÚNICO que la app escribe en la carpeta Curriculum. Se llama
/// después de guardar, fuera de la transacción: si falla, lo guardado sigue siendo válido.
/// </summary>
public interface IExportadorRadar
{
    /// <summary>Reescribe el radar con lo que hay en la BD. Nunca lanza por un error de disco.</summary>
    /// <returns>True si el archivo ha quedado actualizado. False si no hay carpeta o ha habido un error.</returns>
    Task<bool> ExportarAsync();

    /// <summary>
    /// Borra de <c>radar\</c> (sin bajar a subcarpetas) los temporales «*.md.&lt;32 hex&gt;.tmp» de más de
    /// 10 minutos que dejó una exportación cortada (la API se cerró a medias). Nunca lanza.
    /// </summary>
    /// <returns>Cuántos ha borrado.</returns>
    int LimpiarTemporalesHuerfanos();
}
