using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// Arranca la API real en memoria (<see cref="WebApplicationFactory{TEntryPoint}"/>) con una base de
/// datos SQLite en un archivo temporal propio, de modo que también se prueba que las migraciones se
/// aplican al arrancar, y con una carpeta Curriculum temporal propia (con el registro, el perfil y un CV
/// inventados) o la que diga el test. Así los tests nunca tocan la carpeta real, aunque exista un
/// appsettings.Local.json. Al terminar borra la BD y, si es suya, la carpeta.
/// </summary>
public sealed class FabricaApi : WebApplicationFactory<Program>
{
    private readonly string _rutaBd = Path.Combine(Path.GetTempPath(), $"radar-tests-{Guid.NewGuid():N}.db");
    private readonly string _modo;
    private readonly bool _carpetaPropia;

    /// <summary>Opciones JSON como las de la API (camelCase y enums como texto).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Carpeta Curriculum de este test: la temporal con los datos inventados (se puede vaciar o borrar para
    /// probar los 409) o la que se pasó al constructor.
    /// </summary>
    public string CarpetaCurriculum { get; }

    /// <summary>Carpeta con los datos inventados que acompañan a los tests.</summary>
    public static string CarpetaDatosEjemplo => Path.Combine(AppContext.BaseDirectory, "datos-ejemplo");

    /// <summary>Ruta del registro inventado que acompaña a los tests.</summary>
    public static string RutaRegistroEjemplo => Path.Combine(CarpetaDatosEjemplo, "registro-ejemplo.md");

    /// <summary>Ruta del perfil inventado que acompaña a los tests.</summary>
    public static string RutaPerfilEjemplo => Path.Combine(CarpetaDatosEjemplo, "perfil-ejemplo.md");

    /// <param name="modo">Modo de la API (Local o Demo).</param>
    /// <param name="carpetaCurriculum">Null para una carpeta temporal con los datos inventados; "" para
    /// arrancar sin carpeta; cualquier otra ruta se usa tal cual (y no se crea ni se borra).</param>
    public FabricaApi(string modo = "Local", string? carpetaCurriculum = null)
    {
        _modo = modo;
        _carpetaPropia = carpetaCurriculum is null;
        CarpetaCurriculum = carpetaCurriculum ?? Path.Combine(Path.GetTempPath(), $"radar-tests-curriculum-{Guid.NewGuid():N}");
        if (_carpetaPropia) CopiarDatosEjemplo(CarpetaCurriculum);
    }

    /// <summary>
    /// Deja en <paramref name="carpeta"/> una Curriculum de mentira: el registro y el perfil con sus
    /// nombres reales y las carpetas <c>Candidaturas\</c> y <c>rastreos\</c> de los datos de ejemplo.
    /// </summary>
    public static void CopiarDatosEjemplo(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        var registro = Path.Combine(carpeta, RutasCurriculum.ArchivoRegistro);
        File.Copy(RutaRegistroEjemplo, registro);
        // La copia conserva la fecha del archivo del repositorio, y de ella sale el año de los «DD/MM».
        File.SetLastWriteTimeUtc(registro, DateTime.UtcNow);
        File.Copy(RutaPerfilEjemplo, Path.Combine(carpeta, RutasCurriculum.ArchivoPerfil));
        CopiarCarpeta(RutasCurriculum.CarpetaCandidaturas, carpeta);
        CopiarCarpeta(RutasCurriculum.CarpetaInformesRastreo, carpeta);
    }

    private static void CopiarCarpeta(string nombre, string carpeta)
    {
        var origen = Path.Combine(CarpetaDatosEjemplo, nombre);
        foreach (var archivo in Directory.GetFiles(origen, "*", SearchOption.AllDirectories))
        {
            var destino = Path.Combine(carpeta, nombre, Path.GetRelativePath(origen, archivo));
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.Copy(archivo, destino);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RadarEmpleo:RutaBaseDatos"] = _rutaBd,
            ["RadarEmpleo:CarpetaCurriculum"] = CarpetaCurriculum,
            ["RadarEmpleo:Modo"] = _modo,
            ["Agentes:Habilitado"] = "false"
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var archivo in new[] { _rutaBd, _rutaBd + "-shm", _rutaBd + "-wal" })
        {
            try { File.Delete(archivo); } catch (IOException) { /* temporal: si sigue abierto, ya lo limpiará el sistema */ }
        }
        try
        {
            if (_carpetaPropia && Directory.Exists(CarpetaCurriculum)) Directory.Delete(CarpetaCurriculum, recursive: true);
        }
        catch (IOException) { /* temporal: si sigue abierto, ya lo limpiará el sistema */ }
    }
}
