using Microsoft.Extensions.Configuration;
using RadarEmpleo.Api.Configuracion;

namespace RadarEmpleo.Tests.Api;

/// <summary>Orden de las fuentes de configuración y ruta por defecto de la base de datos.</summary>
public sealed class ConfiguracionTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"radar-config-{Guid.NewGuid():N}");

    public ConfiguracionTests() => Directory.CreateDirectory(_carpeta);

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    /// <summary>
    /// Monta la configuración como la de WebApplication (appsettings.json → variables de entorno →
    /// línea de comandos) y añade appsettings.Local.json con <see cref="ConfiguracionLocal.Anadir"/>.
    /// </summary>
    private IConfigurationRoot Construir(string[] argumentos)
    {
        File.WriteAllText(Path.Combine(_carpeta, "appsettings.json"),
            """{ "RadarEmpleo": { "RutaBaseDatos": "", "CarpetaCurriculum": "", "Modo": "Local" } }""");
        File.WriteAllText(Path.Combine(_carpeta, ConfiguracionLocal.Archivo),
            """{ "RadarEmpleo": { "RutaBaseDatos": "desde-local.db", "CarpetaCurriculum": "carpeta-local" } }""");

        var configuracion = new ConfigurationManager();
        configuracion.SetBasePath(_carpeta);
        configuracion.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        configuracion.AddEnvironmentVariables(prefix: "RADAR_TEST_NO_EXISTE_");
        configuracion.AddCommandLine(argumentos);

        ConfiguracionLocal.Anadir(configuracion);
        return configuracion;
    }

    [Fact]
    public void Anadir_ConArgumentoDeLineaDeComandos_ElArgumentoMandaSobreElLocal()
    {
        var configuracion = Construir(["--RadarEmpleo:RutaBaseDatos=desde-argumento.db"]);

        Assert.Equal("desde-argumento.db", configuracion["RadarEmpleo:RutaBaseDatos"]);
        Assert.Equal("carpeta-local", configuracion["RadarEmpleo:CarpetaCurriculum"]); // el local sí pisa a appsettings.json
        Assert.Equal("Local", configuracion["RadarEmpleo:Modo"]);
    }

    [Fact]
    public void Anadir_SinArgumentos_ElLocalPisaAAppsettings()
    {
        var configuracion = Construir([]);

        Assert.Equal("desde-local.db", configuracion["RadarEmpleo:RutaBaseDatos"]);
    }

    [Fact]
    public void RutaBaseDatosEfectiva_Vacia_UsaLaCarpetaDeDatosLocalesDelUsuario()
    {
        var opciones = new OpcionesRadar { RutaBaseDatos = "" };

        var esperada = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadarEmpleo", "radar.db");
        Assert.Equal(esperada, opciones.RutaBaseDatosEfectiva());
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\RadarEmpleo\radar.db"),
                opciones.RutaBaseDatosEfectiva(), ignoreCase: true);
        }
    }

    [Fact]
    public void AppsettingsDelProyecto_RutaBaseDatosVacia()
    {
        // Así la ruta sale del código y funciona igual en Windows y en Linux (Render).
        var configuracion = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        Assert.Equal("", configuracion["RadarEmpleo:RutaBaseDatos"]);
    }
}
