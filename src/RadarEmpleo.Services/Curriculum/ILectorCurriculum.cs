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
}
