using RadarEmpleo.Domain;
using RadarEmpleo.Services.Ofertas;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="ComprobadorOfertaAbierta"/> con el descargador falso y <c>datos-ejemplo/agentes/descargas.json</c>: una sola
/// descarga de la página, el listado solo para los sistemas de selección y nada sin enlace.
/// </summary>
public class ComprobadorOfertaAbiertaTests
{
    private readonly DescargadorOfertasFalso _descargador = new DescargadorOfertasFalso().CargarDe(Path.Combine(FabricaApi.CarpetaDatosEjemplo, "agentes"));

    [Theory]
    [InlineData("https://empleo.ejemplo.es/ofertas/analista-datos-1234", EstadoOferta.Abierta)]
    [InlineData("https://boards.greenhouse.io/empresa-ficticia/jobs/4000001", EstadoOferta.Cerrada)]
    [InlineData("https://jobs.lever.co/empresa-ficticia/00000000-0000-4000-8000-000000000001", EstadoOferta.Cerrada)]
    [InlineData("https://www.linkedin.com/jobs/view/1000000001", EstadoOferta.NoSeSabe)]
    [InlineData("https://empleo.ejemplo.es/ofertas/lenta-5678", EstadoOferta.NoSeSabe)]
    [InlineData("https://empleo.ejemplo.es/ofertas/sin-programar", EstadoOferta.NoSeSabe)]
    public async Task Comprobar_LosEjemplos_DanSuEstadoConUnaSolaDescargaDeLaPagina(string enlace, EstadoOferta estado)
    {
        var comprobada = await new ComprobadorOfertaAbierta(_descargador).ComprobarAsync(enlace, CancellationToken.None);

        Assert.Equal(estado, comprobada.Comprobacion.Estado);
        Assert.Equal([enlace], _descargador.Peticiones.Where(p => p.Tipo == TipoDescarga.Pagina).Select(p => p.Enlace));
    }

    [Fact]
    public async Task Comprobar_UnaOfertaAbiertaDeGreenhouse_MiraElListadoYDevuelveLaPaginaParaElPrompt()
    {
        const string Enlace = "https://boards.greenhouse.io/empresa-ficticia/jobs/4000002";
        _descargador.Programar(Enlace, new RespuestaDescargaFalsa(Cuerpo: "<h1>Técnico de soporte</h1><p>Turno de mañana.</p>"));

        var comprobada = await new ComprobadorOfertaAbierta(_descargador).ComprobarAsync(Enlace, CancellationToken.None);

        Assert.Equal(new ComprobacionOferta(EstadoOferta.Abierta, "Sigue en el listado público de Greenhouse."), comprobada.Comprobacion);
        Assert.Equal("Técnico de soporte\n\nTurno de mañana.", comprobada.Pagina.Texto);
        Assert.Equal([(Enlace, TipoDescarga.Pagina), ("https://boards-api.greenhouse.io/v1/boards/empresa-ficticia/jobs", TipoDescarga.ListadoJson)],
            _descargador.Peticiones);
    }

    [Fact]
    public async Task Comprobar_UnEnlaceHttp_SeDescargaPorHttps()
    {
        var comprobada = await new ComprobadorOfertaAbierta(_descargador).ComprobarAsync("http://empleo.ejemplo.es/ofertas/analista-datos-1234", CancellationToken.None);

        Assert.Equal(EstadoOferta.Abierta, comprobada.Comprobacion.Estado);
        Assert.Equal(["https://empleo.ejemplo.es/ofertas/analista-datos-1234"], _descargador.Peticiones.Select(p => p.Enlace));
    }

    [Fact]
    public async Task Comprobar_SinEsquema_SeNormalizaComoEnLaFicha()
    {
        var comprobada = await new ComprobadorOfertaAbierta(_descargador).ComprobarAsync("empleo.ejemplo.es/ofertas/analista-datos-1234", CancellationToken.None);

        Assert.Equal(EstadoOferta.Abierta, comprobada.Comprobacion.Estado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mailto:rrhh@ejemplo.es")]
    public async Task Comprobar_SinEnlace_NoDescargaNada(string? enlace)
    {
        var comprobada = await new ComprobadorOfertaAbierta(_descargador).ComprobarAsync(enlace, CancellationToken.None);

        Assert.Equal(new ComprobacionOferta(EstadoOferta.NoSeSabe, "La candidatura no tiene un enlace a la oferta."), comprobada.Comprobacion);
        Assert.Empty(_descargador.Peticiones);
    }

    [Fact]
    public async Task Comprobar_Cancelada_LanzaLaCancelacion()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ComprobadorOfertaAbierta(_descargador).ComprobarAsync("https://empleo.ejemplo.es/ofertas/analista-datos-1234", new CancellationToken(true)));
    }
}
