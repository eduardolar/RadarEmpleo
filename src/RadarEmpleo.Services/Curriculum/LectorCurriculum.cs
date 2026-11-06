using System.Text;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Curriculum;

/// <summary>
/// Lee el registro sin bloquearlo: lo abre con <see cref="FileAccess.Read"/> y compartido para que
/// Eduardo o un agente puedan tenerlo abierto, guardarlo o sustituirlo mientras tanto. Los mensajes no
/// incluyen la ruta para no enseñarla en la demo pública.
/// </summary>
public sealed class LectorCurriculum : ILectorCurriculum
{
    private const int TamanoBufer = 4096;

    private readonly RutasCurriculum _rutas;

    public LectorCurriculum(RutasCurriculum rutas) => _rutas = rutas;

    public async Task<string> LeerRegistroAsync(CancellationToken cancelacion = default)
    {
        if (!_rutas.HayCarpeta)
            throw new ReglaNegocioException("No se encuentra la carpeta Curriculum. Revisa la opción «CarpetaCurriculum» de la configuración.");

        var ruta = _rutas.Registro!;
        if (!File.Exists(ruta))
            throw new ReglaNegocioException($"No se encuentra {RutasCurriculum.ArchivoRegistro} en la carpeta Curriculum.");

        try
        {
            await using var fichero = new FileStream(ruta, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, TamanoBufer, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var lector = new StreamReader(fichero, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await lector.ReadToEndAsync(cancelacion);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ReglaNegocioException($"No se ha podido leer {RutasCurriculum.ArchivoRegistro}. Vuelve a intentarlo en un momento.");
        }
    }
}
