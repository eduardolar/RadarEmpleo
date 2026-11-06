using System.Text;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary><see cref="LectorCurriculum"/> y <see cref="RutasCurriculum"/> sobre una carpeta temporal.</summary>
public sealed class LectorCurriculumTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"radar-lector-{Guid.NewGuid():N}");
    private readonly string _registro;
    private readonly LectorCurriculum _lector;

    public LectorCurriculumTests()
    {
        Directory.CreateDirectory(_carpeta);
        var rutas = new RutasCurriculum(_carpeta);
        _registro = rutas.Registro!;
        _lector = new LectorCurriculum(rutas);
    }

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    [Fact]
    public void Contrato_SoloTieneMetodosDeLectura()
    {
        var metodos = typeof(ILectorCurriculum).GetMethods().Concat(typeof(LectorCurriculum).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));

        Assert.All(metodos, m => Assert.StartsWith("Leer", m.Name));
        Assert.Empty(typeof(ILectorCurriculum).GetProperties());
    }

    [Fact]
    public async Task LeerRegistroAsync_FicheroUtf8SinBomConCrlf_LoDevuelveTalCual()
    {
        var texto = "# Candidaturas\r\n\r\nInformática en Málaga, señor\r\n";
        await File.WriteAllTextAsync(_registro, texto, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.Equal(texto, await _lector.LeerRegistroAsync());
    }

    [Fact]
    public async Task LeerRegistroAsync_FicheroAbiertoPorOtroQueEscribe_LoLeeIgual()
    {
        await File.WriteAllTextAsync(_registro, "contenido");
        await using var otro = new FileStream(_registro, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        Assert.Equal("contenido", await _lector.LeerRegistroAsync());
    }

    [Fact]
    public async Task LeerRegistroAsync_NoCambiaElFichero()
    {
        File.Copy(FabricaApi.RutaRegistroEjemplo, _registro);
        var bytes = await File.ReadAllBytesAsync(_registro);
        var fecha = File.GetLastWriteTimeUtc(_registro);

        await _lector.LeerRegistroAsync();

        Assert.Equal(bytes, await File.ReadAllBytesAsync(_registro));
        Assert.Equal(fecha, File.GetLastWriteTimeUtc(_registro));
    }

    [Fact]
    public async Task LeerRegistroAsync_SinElRegistro_LanzaReglaNegocioConMensajeClaro()
    {
        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => _lector.LeerRegistroAsync());

        Assert.Contains(RutasCurriculum.ArchivoRegistro, ex.Message);
        Assert.DoesNotContain(_carpeta, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LeerRegistroAsync_SinCarpetaConfigurada_LanzaReglaNegocio(string? carpeta)
    {
        var lector = new LectorCurriculum(new RutasCurriculum(carpeta));

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => lector.LeerRegistroAsync());
        Assert.Contains("carpeta Curriculum", ex.Message);
    }

    [Fact]
    public async Task LeerRegistroAsync_CarpetaQueNoExiste_LanzaReglaNegocio()
    {
        var lector = new LectorCurriculum(new RutasCurriculum(Path.Combine(_carpeta, "no-existe")));

        await Assert.ThrowsAsync<ReglaNegocioException>(() => lector.LeerRegistroAsync());
    }

    [Fact]
    public void Rutas_ConCarpeta_ApuntanAlRegistroYAlRadarDentroDeElla()
    {
        var rutas = new RutasCurriculum(_carpeta);

        Assert.True(rutas.HayCarpeta);
        Assert.Equal(Path.Combine(_carpeta, "ofertas-enviadas.md"), rutas.Registro);
        Assert.Equal(Path.Combine(_carpeta, "radar", "candidaturas.md"), rutas.Radar);
    }

    [Fact]
    public void Rutas_SinCarpeta_SonNull()
    {
        var rutas = new RutasCurriculum(null);

        Assert.False(rutas.HayCarpeta);
        Assert.Null(rutas.Carpeta);
        Assert.Null(rutas.Registro);
        Assert.Null(rutas.Radar);
    }
}
