using Microsoft.Extensions.DependencyInjection;
using RadarEmpleo.Services.Ofertas;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="DescargadorOfertasFalso"/> con <c>datos-ejemplo/agentes/descargas.json</c>, y que la API lo usa con el ejecutor
/// falso (decisión 9 de la F7b) y el de verdad con el Cli.
/// </summary>
public class DescargadorOfertasFalsoTests
{
    private static readonly string CarpetaEjemplo = Path.Combine(FabricaApi.CarpetaDatosEjemplo, "agentes");

    private static Task<DescargaOferta> Descargar(string enlace, TipoDescarga tipo = TipoDescarga.Pagina) =>
        new DescargadorOfertasFalso().CargarDe(CarpetaEjemplo).DescargarAsync(enlace, tipo, CancellationToken.None);

    [Fact]
    public async Task CargarDe_LosEjemplos_DaLaPaginaEnTextoYElListadoTalCual()
    {
        var pagina = await Descargar("https://empleo.ejemplo.es/ofertas/analista-datos-1234");
        var listado = await Descargar("https://boards-api.greenhouse.io/v1/boards/empresa-ficticia/jobs", TipoDescarga.ListadoJson);

        Assert.StartsWith("Analista de datos junior (oferta inventada)\n", pagina.Texto);
        Assert.DoesNotContain("seguimiento", pagina.Texto);
        Assert.Contains("\"id\":4000002", listado.Texto);
    }

    [Fact]
    public async Task CargarDe_LosEjemplos_TraenRedireccion404YFallosProgramados()
    {
        var cerrada = await Descargar("https://boards.greenhouse.io/empresa-ficticia/jobs/4000001");
        var noExiste = await Descargar("https://jobs.lever.co/empresa-ficticia/00000000-0000-4000-8000-000000000001");
        var lenta = await Descargar("https://empleo.ejemplo.es/ofertas/lenta-5678");

        Assert.Equal("https://boards.greenhouse.io/empresa-ficticia?error=true", cerrada.UrlFinal!.ToString());
        Assert.Equal(((FalloDescarga?)FalloDescarga.CodigoNoValido, (int?)404), (noExiste.Fallo, noExiste.Codigo));
        Assert.Equal(FalloDescarga.TiempoAgotado, lenta.Fallo);
    }

    [Fact]
    public async Task Descargar_ConLasReglasDelDeVerdad_JsonSoloEnListadosYSinProgramarComoSinRed()
    {
        var falso = new DescargadorOfertasFalso();
        const string Listado = "https://api.lever.co/v0/postings/empresa-ficticia?mode=json";
        falso.Programar(Listado, new RespuestaDescargaFalsa(TipoContenido: "application/json", Cuerpo: "{}"));

        Assert.Equal(FalloDescarga.TipoNoAdmitido, (await falso.DescargarAsync(Listado, TipoDescarga.Pagina, CancellationToken.None)).Fallo);
        Assert.True((await falso.DescargarAsync(Listado, TipoDescarga.ListadoJson, CancellationToken.None)).Ok);
        Assert.Equal(FalloDescarga.ErrorDeRed, (await falso.DescargarAsync("https://empleo.ejemplo.es/b", TipoDescarga.Pagina, CancellationToken.None)).Fallo);
        Assert.Equal(3, falso.Peticiones.Count);
    }

    [Fact]
    public async Task Descargar_UnUrlFinalQueNoEsAbsoluto_NoLanzaYElListadoSoloDeSusHosts()
    {
        var falso = new DescargadorOfertasFalso();
        falso.Programar("https://empleo.ejemplo.es/a", new RespuestaDescargaFalsa(UrlFinal: "/relativa", Cuerpo: "<p>Hola</p>"));
        falso.Programar("https://empleo.ejemplo.es/jobs.json", new RespuestaDescargaFalsa(TipoContenido: "application/json", Cuerpo: "[]"));

        var pagina = await falso.DescargarAsync("https://empleo.ejemplo.es/a", TipoDescarga.Pagina, CancellationToken.None);
        var listado = await falso.DescargarAsync("https://empleo.ejemplo.es/jobs.json", TipoDescarga.ListadoJson, CancellationToken.None);

        Assert.Equal(("Hola", (Uri?)null), (pagina.Texto, pagina.UrlFinal));
        Assert.Equal(FalloDescarga.DireccionNoPermitida, listado.Fallo);
    }

    [Theory]
    [InlineData("Local", typeof(DescargadorOfertasFalso))]
    [InlineData("Demo", typeof(DescargadorOfertasHttp))]
    public void Api_ConElEjecutorFalso_UsaLaDescargaFalsa(string modo, Type esperado)
    {
        // FabricaApi pone el ejecutor falso en modo Local y el Cli en la demo.
        using var fabrica = new FabricaApi(modo);

        Assert.IsType(esperado, fabrica.Services.GetRequiredService<IDescargadorOfertas>());
    }
}
