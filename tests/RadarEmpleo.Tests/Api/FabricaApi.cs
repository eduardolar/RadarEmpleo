using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// Arranca la API real en memoria (<see cref="WebApplicationFactory{TEntryPoint}"/>) con una base de
/// datos SQLite en un archivo temporal propio, de modo que también se prueba que las migraciones se
/// aplican al arrancar. Al terminar borra el archivo.
/// </summary>
public sealed class FabricaApi : WebApplicationFactory<Program>
{
    private readonly string _rutaBd = Path.Combine(Path.GetTempPath(), $"radar-tests-{Guid.NewGuid():N}.db");

    /// <summary>Opciones JSON como las de la API (camelCase y enums como texto).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RadarEmpleo:RutaBaseDatos"] = _rutaBd,
            ["RadarEmpleo:Modo"] = "Local",
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
    }
}
