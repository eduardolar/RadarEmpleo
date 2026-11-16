using RadarEmpleo.Domain;
using RadarEmpleo.Services.Ofertas;

namespace RadarEmpleo.Tests;

/// <summary><see cref="ReglasOfertaAbierta"/>: abierta, cerrada o no se sabe, con su motivo. Solo ofertas inventadas.</summary>
public class ReglasOfertaAbiertaTests
{
    private static readonly OfertaEnSistema Greenhouse = new(SistemaSeleccion.Greenhouse, "empresa-ficticia", "4000001");
    private static readonly OfertaEnSistema Workable = new(SistemaSeleccion.Workable, "empresa-ficticia", "AB12CD34EF");
    private static readonly OfertaEnSistema Lever = new(SistemaSeleccion.Lever, "empresa-ficticia", "00000000-0000-4000-8000-000000000001");

    private static DescargaOferta Pagina(string texto, string url = "https://empleo.ejemplo.es/ofertas/1") => new(null, 200, new Uri(url), texto);

    private static DescargaOferta Listado(string json) => new(null, 200, new Uri("https://boards-api.greenhouse.io/v1/boards/empresa-ficticia/jobs"), json);

    private static readonly DescargaOferta PaginaNormal = Pagina("Analista de datos\nRequisitos: SQL.");

    private static ComprobacionOferta Decidir(OfertaEnSistema? sistema, DescargaOferta pagina, DescargaOferta? listado) =>
        ReglasOfertaAbierta.Decidir(new Uri("https://empleo.ejemplo.es/ofertas/1"), sistema, pagina, listado);

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public void Decidir_LaPaginaYaNoExiste_Cerrada(int codigo)
    {
        var resultado = Decidir(null, DescargaOferta.Fallida(FalloDescarga.CodigoNoValido, codigo), null);

        Assert.Equal(new ComprobacionOferta(EstadoOferta.Cerrada, $"La página de la oferta ya no existe ({codigo})."), resultado);
    }

    [Fact]
    public void Decidir_GreenhouseLlevaAlListadoConError_CerradaAunqueElListadoNoSeLea()
    {
        var pagina = Pagina("Ofertas abiertas", "https://job-boards.greenhouse.io/empresa-ficticia?error=true");

        var resultado = Decidir(Greenhouse, pagina, DescargaOferta.Fallida(FalloDescarga.TiempoAgotado));

        Assert.Equal(EstadoOferta.Cerrada, resultado.Estado);
        Assert.Contains("Greenhouse", resultado.Motivo);
    }

    [Theory]
    [InlineData("""{"jobs":[{"id":4000002},{"id":4000001,"title":"Analista"}]}""", EstadoOferta.Abierta, "Sigue en el listado público de Greenhouse.")]
    [InlineData("""{"jobs":[{"id":4000002}]}""", EstadoOferta.NoSeSabe, "No sale en el listado público de Greenhouse, pero la página se lee.")]
    [InlineData("""{"jobs":[]}""", EstadoOferta.NoSeSabe, "No sale en el listado público de Greenhouse, pero la página se lee.")]
    [InlineData("""{"jobs":[{"id":"40000010"}]}""", EstadoOferta.NoSeSabe, "No sale en el listado público de Greenhouse, pero la página se lee.")]
    public void Decidir_ConElListado_EstarDaAbiertaYNoEstarConLaPaginaLegibleNoSeSabe(string json, EstadoOferta estado, string motivo)
    {
        Assert.Equal(new ComprobacionOferta(estado, motivo), Decidir(Greenhouse, PaginaNormal, Listado(json)));
    }

    [Fact]
    public void Decidir_ConElListado_EstarMandaSobreLaPaginaYNoEstarSinPaginaLegibleEsCerrada()
    {
        const string Esta = """{"jobs":[{"id":4000001}]}""", NoEsta = """{"jobs":[{"id":4000002}]}""";
        var cerrada = new ComprobacionOferta(EstadoOferta.Cerrada, "Ya no está en el listado público de Greenhouse.");

        Assert.Equal(EstadoOferta.Abierta, Decidir(Greenhouse, Pagina("Oferta cerrada"), Listado(Esta)).Estado);
        Assert.Equal(cerrada, Decidir(Greenhouse, Pagina("Oferta cerrada"), Listado(NoEsta)));
        Assert.Equal(cerrada, Decidir(Greenhouse, DescargaOferta.Fallida(FalloDescarga.CodigoNoValido, 403), Listado(NoEsta)));
        Assert.Equal(cerrada, Decidir(Greenhouse, Pagina("Sign in to view"), Listado(NoEsta)));
        Assert.Equal(cerrada, Decidir(Greenhouse, Pagina("Ofertas", "https://www.ejemplo-portal.es/"), Listado(NoEsta)));
    }

    [Theory]
    [InlineData("https://portal-generico.ejemplo.es/ofertas/1")]
    [InlineData("https://empleo.ejemplo.es/")]
    [InlineData("https://empleo.ejemplo.es")]
    [InlineData("https://otrogreenhouse.io/empresa-ficticia?error=true")]
    public void Decidir_LaPaginaAcabaEnOtraWebOEnLaPortada_NoSeSabe(string urlFinal)
    {
        Assert.Equal(new ComprobacionOferta(EstadoOferta.NoSeSabe, "La oferta lleva a otra página: no sé si sigue abierta."),
            Decidir(null, Pagina("Bienvenido a nuestra web", urlFinal), null));
    }

    [Theory]
    [InlineData("https://www.empleo.ejemplo.es/ofertas/1")]
    [InlineData("https://EMPLEO.ejemplo.es/ofertas/1-analista")]
    public void Decidir_ElMismoHostConOSinWww_SigueSiendoLaOferta(string urlFinal)
    {
        Assert.Equal(EstadoOferta.Abierta, Decidir(null, Pagina("Analista de datos", urlFinal), null).Estado);
    }

    [Theory]
    [InlineData("https://boards.greenhouse.io/empresa-ficticia?error=true")]
    [InlineData("https://greenhouse.io/empresa-ficticia?error=true")]
    public void Decidir_GreenhouseConError_SoloEnSusHosts(string urlFinal)
    {
        Assert.Equal(EstadoOferta.Cerrada, Decidir(null, Pagina("Ofertas", urlFinal), null).Estado);
    }

    [Theory]
    [InlineData("""[{"id":"00000000-0000-4000-8000-000000000001"}]""", EstadoOferta.Abierta)]
    [InlineData("""[{"id":"00000000-0000-4000-8000-000000000002"}]""", EstadoOferta.NoSeSabe)]
    public void Decidir_ElListadoDeLever_EsUnArray(string json, EstadoOferta estado)
    {
        Assert.Equal(estado, Decidir(Lever, PaginaNormal, Listado(json)).Estado);
    }

    [Theory]
    [InlineData("""{"jobs":[{"shortcode":"ab12cd34ef","id":"x"}]}""", EstadoOferta.Abierta)]
    [InlineData("""{"jobs":[{"id":"AB12CD34EF"}]}""", EstadoOferta.NoSeSabe)]
    public void Decidir_ElListadoDeWorkable_VaPorElShortcode(string json, EstadoOferta estado)
    {
        Assert.Equal(estado, Decidir(Workable, PaginaNormal, Listado(json)).Estado);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"ofertas":[]}""")]
    [InlineData("42")]
    public void Decidir_UnListadoConOtraForma_SeIgnoraYDecideLaPagina(string json)
    {
        Assert.Equal(EstadoOferta.Abierta, Decidir(Greenhouse, PaginaNormal, Listado(json)).Estado);
    }

    [Fact]
    public void Decidir_SinListado_DecideLaPagina()
    {
        var resultado = Decidir(Greenhouse, PaginaNormal, DescargaOferta.Fallida(FalloDescarga.DemasiadoGrande));

        Assert.Equal(new ComprobacionOferta(EstadoOferta.Abierta, "La página de la oferta se lee y no dice que esté cerrada."), resultado);
    }

    [Theory]
    [InlineData("Analista de datos\nEsta oferta ya no está disponible.")]
    [InlineData("ESTA OFERTA HA CADUCADO")]
    [InlineData("Lo sentimos: ya no se aceptan candidaturas para este puesto.")]
    [InlineData("Data analyst\nThis job is no longer available")]
    [InlineData("We are no longer accepting applications for this role.")]
    [InlineData("This position has been filled.")]
    public void Decidir_LaPaginaDiceQueEstaCerrada_EnEspanolOIngles(string texto)
    {
        var resultado = Decidir(null, Pagina(texto), null);

        Assert.Equal(new ComprobacionOferta(EstadoOferta.Cerrada, "La página dice que la oferta ya no admite candidaturas."), resultado);
    }

    [Theory]
    [InlineData("Analista de datos", "https://www.linkedin.com/authwall?sessionRedirect=x")]
    [InlineData("Analista de datos", "https://empleo.ejemplo.es/login?volver=/ofertas/1")]
    [InlineData("Inicia sesión para ver esta oferta. Oferta cerrada", "https://empleo.ejemplo.es/ofertas/1")]
    [InlineData("Sign in to view the full job description", "https://empleo.ejemplo.es/ofertas/1")]
    public void Decidir_UnMuroDeInicioDeSesion_NoSeSabe(string texto, string url)
    {
        var resultado = Decidir(null, Pagina(texto, url), null);

        Assert.Equal(new ComprobacionOferta(EstadoOferta.NoSeSabe, "La página pide iniciar sesión: no sé si sigue abierta."), resultado);
    }

    [Theory]
    [InlineData(FalloDescarga.CodigoNoValido, 403, "No he podido leer la oferta: la web ha respondido 403.")]
    [InlineData(FalloDescarga.TiempoAgotado, null, "No he podido leer la oferta: tardaba demasiado.")]
    [InlineData(FalloDescarga.DireccionNoPermitida, null, "No he podido leer la oferta: la dirección no está permitida.")]
    [InlineData(FalloDescarga.TipoNoAdmitido, null, "No he podido leer la oferta: no es una página de texto.")]
    [InlineData(FalloDescarga.ErrorDeRed, null, "No he podido leer la oferta: no se ha podido conectar.")]
    public void Decidir_SinPaginaNiListado_NoSeSabeConElMotivo(FalloDescarga fallo, int? codigo, string motivo)
    {
        var resultado = Decidir(null, DescargaOferta.Fallida(fallo, codigo), null);

        Assert.Equal(new ComprobacionOferta(EstadoOferta.NoSeSabe, motivo), resultado);
    }
}
