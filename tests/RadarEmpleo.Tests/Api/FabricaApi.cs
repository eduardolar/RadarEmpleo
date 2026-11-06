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
/// aplican al arrancar, y con una carpeta Curriculum temporal propia. Así los tests nunca tocan la
/// carpeta real, aunque exista un appsettings.Local.json. Al terminar borra los dos.
/// </summary>
public sealed class FabricaApi : WebApplicationFactory<Program>
{
    private readonly string _rutaBd = Path.Combine(Path.GetTempPath(), $"radar-tests-{Guid.NewGuid():N}.db");
    private readonly string _modo;

    /// <summary>Opciones JSON como las de la API (camelCase y enums como texto).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Carpeta Curriculum de este test, con el registro inventado. Se puede vaciar o borrar para probar los 409.</summary>
    public string CarpetaCurriculum { get; } = Path.Combine(Path.GetTempPath(), $"radar-tests-curriculum-{Guid.NewGuid():N}");

    /// <summary>Ruta del registro inventado que acompaña a los tests.</summary>
    public static string RutaRegistroEjemplo => Path.Combine(AppContext.BaseDirectory, "datos-ejemplo", "registro-ejemplo.md");

    public FabricaApi(string modo = "Local")
    {
        _modo = modo;
        Directory.CreateDirectory(CarpetaCurriculum);
        File.Copy(RutaRegistroEjemplo, Path.Combine(CarpetaCurriculum, RutasCurriculum.ArchivoRegistro));
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
            if (Directory.Exists(CarpetaCurriculum)) Directory.Delete(CarpetaCurriculum, recursive: true);
        }
        catch (IOException) { /* temporal: si sigue abierto, ya lo limpiará el sistema */ }
    }
}
