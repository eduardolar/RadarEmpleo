using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Curriculum;

/// <summary>
/// Acceso de SOLO LECTURA a la carpeta Curriculum. A propósito no tiene ningún método de escritura: lo
/// único que la app escribe ahí pasa por <see cref="IExportadorRadar"/>.
/// </summary>
public interface ILectorCurriculum
{
    /// <summary>Texto completo del registro de candidaturas.</summary>
    /// <exception cref="ReglaNegocioException">Falta la carpeta o el registro, o no se puede leer.</exception>
    Task<string> LeerRegistroAsync(CancellationToken cancelacion = default);

    /// <summary>Texto completo del registro y su fecha de modificación (para el año de los rastreos «DD/MM»).</summary>
    /// <exception cref="ReglaNegocioException">Falta la carpeta o el registro, o no se puede leer.</exception>
    Task<TextoLeido> LeerRegistroConFechaAsync(CancellationToken cancelacion = default);

    /// <summary>Texto completo del perfil de búsqueda y su fecha de modificación.</summary>
    /// <exception cref="ReglaNegocioException">Falta la carpeta o el perfil, o no se puede leer.</exception>
    Task<TextoLeido> LeerPerfilAsync(CancellationToken cancelacion = default);

    /// <summary>
    /// Archivos .pdf y .docx que se pueden vincular: los sueltos de la raíz, los de cada
    /// <c>Candidaturas\Semana …\</c> y los de <c>Archivo\</c> hasta dos niveles. Salta los enlaces y
    /// <c>__pycache__</c>.
    /// </summary>
    /// <exception cref="ReglaNegocioException">Falta la carpeta Curriculum.</exception>
    IReadOnlyList<ArchivoDisponible> LeerDocumentosDisponibles();

    /// <summary>
    /// Abre un documento para leerlo sin bloquearlo y comprueba el archivo ya abierto
    /// (<see cref="RutaSegura.ComprobarAbierto"/>). Devuelve null si no hay carpeta, si el archivo no está o
    /// si no se puede abrir. Quien lo recibe cierra el flujo.
    /// </summary>
    /// <exception cref="ValidacionException">La ruta o el archivo abierto no son seguros (<see cref="RutaSegura"/>).</exception>
    DocumentoLeido? LeerDocumento(string rutaRelativa);

    /// <summary>
    /// Informes <c>rastreos\rastreo_*.md</c> de la tarea de los lunes, por nombre. Los <c>rastreo_*.json</c>
    /// (los generará la app en la F7) todavía no se leen: salen en <see cref="InformesRastreo.Json"/>. Sin
    /// carpeta <c>rastreos\</c>, todo vacío.
    /// </summary>
    /// <exception cref="ReglaNegocioException">Falta la carpeta Curriculum.</exception>
    Task<InformesRastreo> LeerInformesRastreoAsync(CancellationToken cancelacion = default);
}

/// <summary>Un informe de rastreo con su ruta dentro de Curriculum («rastreos/rastreo_2026-11-09.md»).</summary>
public sealed record InformeRastreoLeido(string RutaRelativa, string Texto);

/// <summary>Lo que hay en <c>rastreos\</c>.</summary>
/// <param name="Json">Rutas de los <c>.json</c>, que se dejan sin leer.</param>
/// <param name="NoLeidos">Rutas de los <c>.md</c> que no se han podido abrir (en uso, sin permiso…).</param>
public sealed record InformesRastreo(
    IReadOnlyList<InformeRastreoLeido> Informes,
    IReadOnlyList<string> Json,
    IReadOnlyList<string> NoLeidos);

/// <summary>Texto de un archivo de Curriculum con su fecha de modificación (UTC).</summary>
public sealed record TextoLeido(string Texto, DateTime FechaModificacion);

/// <summary>Un archivo que se puede vincular.</summary>
/// <param name="RutaRelativa">Ruta dentro de Curriculum con «/».</param>
/// <param name="Carpeta">Carpeta que lo contiene, con «/» («» para la raíz).</param>
/// <param name="FechaModificacion">Última modificación (UTC).</param>
public sealed record ArchivoDisponible(string RutaRelativa, string NombreArchivo, string Carpeta, DateTime FechaModificacion);

/// <summary>Documento abierto para servirlo: el flujo es de solo lectura y lo cierra quien lo recibe.</summary>
/// <param name="RutaRelativa">La ruta real del archivo abierto, con «/» y las mayúsculas del disco.</param>
/// <param name="NombreArchivo">El nombre real del archivo abierto.</param>
public sealed record DocumentoLeido(Stream Contenido, string NombreArchivo, string TipoContenido, string RutaRelativa);
