using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RadarEmpleo.Api.Seguridad;
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
    private readonly string _carpetaDatos = Path.Combine(Path.GetTempPath(), $"radar-tests-datos-{Guid.NewGuid():N}");
    private readonly string _modo;
    private readonly bool _carpetaPropia;
    private readonly CanalPeticion? _canal;
    private readonly bool _agentes;
    private readonly IReadOnlyDictionary<string, string?> _configuracion;
    private readonly Action<IServiceCollection>? _servicios;

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
    /// <param name="canal">Por qué endpoint llegan las peticiones (el PC o el móvil). En memoria no hay Kestrel: lo dice
    /// un <see cref="IClasificadorCanal"/> de prueba. Null deja el de verdad (para los tests con Kestrel real).</param>
    /// <param name="agentes">Agentes:Habilitado. Siempre con el ejecutor falso: nunca se lanza Claude.</param>
    /// <param name="configuracion">Claves de configuración de más (pisan a las de por defecto).</param>
    /// <param name="servicios">Registros de más (por ejemplo, el <c>ManejadorDePrueba</c>).</param>
    public FabricaApi(string modo = "Local", string? carpetaCurriculum = null, CanalPeticion? canal = CanalPeticion.Local,
        bool agentes = false, IReadOnlyDictionary<string, string?>? configuracion = null, Action<IServiceCollection>? servicios = null)
    {
        _modo = modo;
        _canal = canal;
        _agentes = agentes;
        _configuracion = configuracion ?? new Dictionary<string, string?>();
        _servicios = servicios;
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
        var configuracion = new Dictionary<string, string?>
        {
            ["RadarEmpleo:RutaBaseDatos"] = _rutaBd,
            ["RadarEmpleo:CarpetaCurriculum"] = CarpetaCurriculum,
            ["RadarEmpleo:Modo"] = _modo,
            ["Agentes:Habilitado"] = _agentes ? "true" : "false",
            // El falso solo vale en modo Local; en la demo, un Cli que no puede lanzar nada (la ruta no existe).
            ["Agentes:Ejecutor"] = _modo == "Local" ? "Falso" : "Cli",
            ["Agentes:RutaClaude"] = Path.Combine(_carpetaDatos, "no-existe", "claude.exe"),
            ["Agentes:CarpetaDatos"] = _carpetaDatos
        };
        foreach (var (clave, valor) in _configuracion) configuracion[clave] = valor;
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuracion));
        builder.ConfigureTestServices(servicios =>
        {
            if (_canal is { } canal)
            {
                servicios.RemoveAll<IClasificadorCanal>();
                servicios.AddSingleton<IClasificadorCanal>(new ClasificadorFijo(canal));
            }
            _servicios?.Invoke(servicios);
        });
    }

    /// <summary>Clasificador de prueba: todas las peticiones llegan por el mismo canal.</summary>
    private sealed class ClasificadorFijo(CanalPeticion canal) : IClasificadorCanal
    {
        public CanalPeticion Canal(Microsoft.AspNetCore.Http.HttpContext http) => canal;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        // También la exclusión de la cola de agentes («<bd>.agentes.lock»), que la API deja al cerrarse.
        foreach (var archivo in new[] { _rutaBd, _rutaBd + "-shm", _rutaBd + "-wal", _rutaBd + ".agentes.lock" })
        {
            try { File.Delete(archivo); } catch (IOException) { /* temporal: si sigue abierto, ya lo limpiará el sistema */ }
        }
        try
        {
            if (_carpetaPropia && Directory.Exists(CarpetaCurriculum)) Directory.Delete(CarpetaCurriculum, recursive: true);
            if (Directory.Exists(_carpetaDatos)) Directory.Delete(_carpetaDatos, recursive: true);
        }
        catch (IOException) { /* temporal: si sigue abierto, ya lo limpiará el sistema */ }
    }
}
