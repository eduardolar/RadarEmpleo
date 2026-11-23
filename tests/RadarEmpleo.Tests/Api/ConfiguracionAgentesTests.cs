using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RadarEmpleo.Api.Configuracion;
using RadarEmpleo.Api.Seguridad;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// Lo que se comprueba al arrancar (ruta de la BD, ejecutor falso) y la regla de <c>Sec-Fetch-Site</c> según el entorno.
/// La «carpeta de datos de verdad» es aquí una carpeta temporal: nunca se usa la real.
/// </summary>
public sealed class ConfiguracionAgentesTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), $"radar-config-{Guid.NewGuid():N}");
    private readonly string _datos;

    public ConfiguracionAgentesTests()
    {
        _datos = Directory.CreateDirectory(Path.Combine(_raiz, "RadarEmpleo")).FullName;
        File.WriteAllText(Path.Combine(_datos, "radar.db"), "BD de mentira");
    }

    public void Dispose()
    {
        foreach (var enlace in Directory.EnumerateDirectories(_raiz).Where(d => new DirectoryInfo(d).LinkTarget is not null
                     || (new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0))
            Directory.Delete(enlace);
        Directory.Delete(_raiz, recursive: true);
    }

    [Theory]
    [InlineData("x.db;Data Source=C:\\otra\\radar.db")]
    [InlineData("x.db=1")]
    [InlineData("\"x.db\"")]
    [InlineData("x'.db")]
    public void RutaDeLaBd_ConAlgoDeCadenaDeConexion_NoArranca(string ruta)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new OpcionesRadar { RutaBaseDatos = Path.Combine(Path.GetTempPath(), ruta) }.ExigirRutaBaseDatosValida());
        Assert.Contains("RutaBaseDatos", error.Message);
    }

    [Fact]
    public void RutaDeLaBd_Normal_Vale()
    {
        new OpcionesRadar { RutaBaseDatos = Path.Combine(Path.GetTempPath(), "radar-x", "radar.db") }.ExigirRutaBaseDatosValida();
        new OpcionesRadar().ExigirRutaBaseDatosValida();
    }

    [Fact]
    public void EsLaDeVerdad_DentroDeLaCarpetaDeDatos()
    {
        Assert.True(ConfiguracionAgentes.EsLaDeVerdad(Path.Combine(_datos, "radar.db"), _datos));
        Assert.True(ConfiguracionAgentes.EsLaDeVerdad(Path.Combine(_datos.ToUpperInvariant(), "otra.db"), _datos));
        Assert.False(ConfiguracionAgentes.EsLaDeVerdad(Path.Combine(_raiz, "temporal.db"), _datos));
    }

    [Fact]
    public void EsLaDeVerdad_PorUnaJunctionHaciaLaCarpetaDeDatos()
    {
        if (!OperatingSystem.IsWindows()) return;
        var junction = Path.Combine(_raiz, "atajo");
        if (!Mklink("/J", junction, _datos)) return;

        Assert.True(ConfiguracionAgentes.EsLaDeVerdad(Path.Combine(junction, "radar.db"), _datos));
        Assert.True(ConfiguracionAgentes.EsLaDeVerdad(Path.Combine(junction, "nueva.db"), _datos));
    }

    [Fact]
    public void EsLaDeVerdad_UnEnlaceDuro()
    {
        if (!OperatingSystem.IsWindows()) return;
        var original = Path.Combine(_raiz, "original.db");
        File.WriteAllText(original, "x");
        var enlace = Path.Combine(_raiz, "enlace.db");
        if (!Mklink("/H", enlace, original)) return;

        Assert.True(ConfiguracionAgentes.EsLaDeVerdad(enlace, _datos));
        Assert.True(ConfiguracionAgentes.EsLaDeVerdad(original, _datos));
    }

    [Fact]
    public void EsLaDeVerdad_ElMismoArchivoQueLaBdPorDefecto()
    {
        if (!OperatingSystem.IsWindows()) return;
        // La BD «por defecto» de la carpeta de datos de mentira, alcanzada por otra ruta (una junction a su carpeta, fuera
        // de la comprobación por texto): la identidad del archivo la delata.
        var junction = Path.Combine(_raiz, "otra-ruta");
        if (!Mklink("/J", junction, _datos)) return;
        Assert.True(ConfiguracionAgentes.EsLaDeVerdad(Path.Combine(junction, "radar.db"), Path.Combine(_raiz, "RADAREMPLEO")));
    }

    [Fact]
    public void CarpetaDatosPorDefecto_NoSaleDeLasVariablesDeEntorno()
    {
        var antes = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        try
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", _raiz);
            Assert.False(ConfiguracionAgentes.CarpetaDatosPorDefecto().StartsWith(_raiz, StringComparison.OrdinalIgnoreCase));
            Assert.EndsWith("RadarEmpleo", ConfiguracionAgentes.CarpetaDatosPorDefecto());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", antes);
        }
    }

    [FactSoloWindows("la carpeta del perfil registrado sale del registro de Windows")]
    public void CarpetasDatosPosibles_ConUserprofileCambiado_SigueIncluyendoLaDelPerfilRegistrado()
    {
        var antes = Environment.GetEnvironmentVariable("USERPROFILE");
        var real = ConfiguracionAgentes.CarpetasDatosPosibles();
        try
        {
            // Quien arranca la API cambia %USERPROFILE%: la carpeta del perfil registrado no se mueve.
            Environment.SetEnvironmentVariable("USERPROFILE", _raiz);
            var conCambio = ConfiguracionAgentes.CarpetasDatosPosibles();
            Assert.Equal(2, real.Count);
            Assert.Contains(real[1], conCambio, StringComparer.OrdinalIgnoreCase);
            Assert.False(real[1].StartsWith(_raiz, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", antes);
        }
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public void SameSiteDesdeElFrontDeDesarrollo_SoloEnDevelopment(string entorno, bool pasa)
    {
        var disponibilidad = new DisponibilidadAgentes(new CanalFijo(), Options.Create(new OpcionesAgentes { Habilitado = true }),
            Options.Create(new OpcionesRadar()), new Entorno(entorno));
        var http = new DefaultHttpContext();
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("localhost", 5180);
        http.Request.Headers["Sec-Fetch-Site"] = "same-site";
        http.Request.Headers.Origin = "http://localhost:4200";

        Assert.Equal(pasa, disponibilidad.Motivo(http) is null);
        // La propia API sigue valiendo en cualquier entorno.
        http.Request.Headers.Origin = "http://localhost:5180";
        Assert.Null(disponibilidad.Motivo(http));
    }

    private static bool Mklink(string tipo, string enlace, string destino)
    {
        var inicio = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argumento in new[] { "/c", "mklink", tipo, enlace, destino }) inicio.ArgumentList.Add(argumento);
        using var proceso = Process.Start(inicio)!;
        proceso.WaitForExit(10_000);
        // Sin permiso para crear enlaces en este PC: el test no se puede hacer aquí.
        return proceso.ExitCode == 0 && (File.Exists(enlace) || Directory.Exists(enlace));
    }

    private sealed class CanalFijo : IClasificadorCanal
    {
        public CanalPeticion Canal(HttpContext http) => CanalPeticion.Local;
    }

    private sealed class Entorno(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;
        public string ApplicationName { get; set; } = "RadarEmpleo";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
