using System.Text;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Perfil;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="PerfilService"/> con el lector de verdad sobre una carpeta temporal: lo que los tests de la API
/// (<c>PerfilApiTests</c>) no miran, que es la carpeta que falta, el perfil vacío o raro y la fecha del archivo.
/// </summary>
public sealed class PerfilServiceTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"radar-perfil-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private string RutaPerfil => Path.Combine(_carpeta, RutasCurriculum.ArchivoPerfil);

    private PerfilService Servicio(string? carpeta) => new(new LectorCurriculum(new RutasCurriculum(carpeta)));

    private PerfilService Servicio() => Servicio(_carpeta);

    [Fact]
    public async Task Obtener_SinCarpetaConfiguradaOSiNoExiste_DiceQueNoEncuentraLaCarpetaSinLaRuta()
    {
        var noExiste = Path.Combine(_carpeta, "no-existe");

        var sinConfigurar = await Assert.ThrowsAsync<ReglaNegocioException>(() => Servicio(null).ObtenerAsync());
        var sinCarpeta = await Assert.ThrowsAsync<ReglaNegocioException>(() => Servicio(noExiste).ObtenerAsync());

        Assert.Equal(RutasCurriculum.MensajeSinCarpeta, sinConfigurar.Message);
        Assert.Equal(RutasCurriculum.MensajeSinCarpeta, sinCarpeta.Message);
        Assert.DoesNotContain(_carpeta, sinCarpeta.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n \r\n")]
    public async Task Obtener_ConElPerfilVacio_DaElTituloPorDefectoSinIntroduccionNiSecciones(string contenido)
    {
        File.WriteAllText(RutaPerfil, contenido);

        var perfil = await Servicio().ObtenerAsync();

        Assert.Equal(AnalizadorPerfil.TituloPorDefecto, perfil.Titulo);
        Assert.Null(perfil.Introduccion);
        Assert.Empty(perfil.Secciones);
    }

    [Fact]
    public async Task Obtener_ConBomYSaltosDeWindows_DaElTituloLimpioYLasSeccionesConSaltosNormales()
    {
        File.WriteAllText(RutaPerfil, "# Mi perfil\r\n\r\nPara empezar.\r\n\r\n## Filtros\r\n\r\n- Málaga\r\n- Remoto\r\n\r\n### Detalle\r\n\r\nMás.\r\n\r\n## Idiomas\r\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var perfil = await Servicio().ObtenerAsync();

        Assert.Equal("Mi perfil", perfil.Titulo);
        Assert.Equal("Para empezar.", perfil.Introduccion);
        Assert.Equal(["Filtros", "Idiomas"], perfil.Secciones.Select(s => s.Titulo));
        Assert.Equal("- Málaga\n- Remoto\n\n### Detalle\n\nMás.", perfil.Secciones[0].Markdown);
        Assert.Equal("", perfil.Secciones[1].Markdown);
    }

    [Fact]
    public async Task Obtener_LaFechaEsLaDeLaUltimaModificacionDelArchivoEnUtc()
    {
        File.WriteAllText(RutaPerfil, "# Perfil\n");
        // Un instante de verano: si se guardase en hora local saldría dos horas movido.
        var modificado = new DateTime(2026, 7, 15, 21, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(RutaPerfil, modificado);

        var perfil = await Servicio().ObtenerAsync();

        Assert.Equal(modificado, perfil.FechaModificacion);
        Assert.Equal(DateTimeKind.Utc, perfil.FechaModificacion.Kind);
    }

    [Fact]
    public async Task Obtener_NoTocaElArchivoNiSuFecha_YLoLeeAunqueOtroProgramaLoTengaAbiertoParaEscribir()
    {
        File.WriteAllText(RutaPerfil, "# Perfil\n\n## Filtros\n\nMálaga\n");
        var modificado = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(RutaPerfil, modificado);
        var antes = File.ReadAllBytes(RutaPerfil);

        PerfilDtoLeido leido;
        // Como lo tiene un editor mientras se escribe en él.
        await using (new FileStream(RutaPerfil, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var perfil = await Servicio().ObtenerAsync();
            leido = new(perfil.Titulo, perfil.Secciones.Count);
        }

        Assert.Equal(new PerfilDtoLeido("Perfil", 1), leido);
        Assert.Equal(antes, File.ReadAllBytes(RutaPerfil));
        Assert.Equal(modificado, File.GetLastWriteTimeUtc(RutaPerfil));
    }

    [Fact]
    public async Task Obtener_YaCancelado_NoDevuelveNada()
    {
        File.WriteAllText(RutaPerfil, "# Perfil\n");
        using var cancelar = new CancellationTokenSource();
        cancelar.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Servicio().ObtenerAsync(cancelar.Token));
    }

    private sealed record PerfilDtoLeido(string Titulo, int Secciones);
}
