using System.IO.Compression;
using System.Net;
using System.Text;
using RadarEmpleo.Services.Ofertas;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="DescargadorOfertasHttp"/> con un <see cref="HttpMessageHandler"/> y un DNS falsos: nunca sale a internet.
/// </summary>
public class DescargadorOfertasHttpTests
{
    private const string Oferta = "https://empleo.ejemplo.es/ofertas/42";
    private const string Listado = "https://boards-api.greenhouse.io/v1/boards/empresa-ficticia/jobs";
    private readonly ResolvedorDnsFalso _dns = new()
    {
        ["empleo.ejemplo.es"] = ["81.40.10.20"], ["otro.ejemplo.es"] = ["81.40.10.30"], ["interno.ejemplo.es"] = ["192.168.1.10"],
        ["boards-api.greenhouse.io"] = ["81.40.10.40"], ["api.lever.co"] = ["81.40.10.41"]
    };
    private readonly ManejadorFalso _web = new();

    private DescargadorOfertasHttp Descargador(OpcionesDescarga? opciones = null) =>
        new(new ConectorSeguro(_dns, () => [], (_, _) => throw new InvalidOperationException("Los tests no abren sockets.")), opciones, _web);

    private static HttpResponseMessage Respuesta(string cuerpo, string tipo = "text/html", HttpStatusCode codigo = HttpStatusCode.OK) =>
        new(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, tipo) };

    private static HttpResponseMessage Redireccion(string destino) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(destino, UriKind.RelativeOrAbsolute) } };

    [Fact]
    public async Task Descargar_UnaPaginaHtml_DevuelveElTextoConPeticionGenericaYHttp2ComoMucho()
    {
        _web.Responder(_ => Respuesta("<html><head><title>Analista</title></head><body><p>Madrid</p><script>x()</script></body></html>"));

        var descarga = await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);

        Assert.True(descarga.Ok);
        Assert.Equal(("Analista\n\nMadrid", (int?)200, Oferta), (descarga.Texto, descarga.Codigo, descarga.UrlFinal!.ToString()));
        var peticion = Assert.Single(_web.Peticiones);
        Assert.Equal((HttpVersion.Version20, HttpVersionPolicy.RequestVersionOrLower), (peticion.Version, peticion.VersionPolicy));
        Assert.Equal(DescargadorOfertasHttp.AgenteUsuario, peticion.Headers.UserAgent.ToString());
        Assert.False(peticion.Headers.Contains("Cookie") || peticion.Headers.Contains("Authorization"));
    }

    [Theory]
    [InlineData("http://empleo.ejemplo.es/ofertas/42", FalloDescarga.EnlaceNoValido)]
    [InlineData("https://yo:clave@empleo.ejemplo.es/ofertas/42", FalloDescarga.EnlaceNoValido)]
    [InlineData("file:///C:/Windows/win.ini", FalloDescarga.EnlaceNoValido)]
    [InlineData("no es un enlace", FalloDescarga.EnlaceNoValido)]
    [InlineData("https://empleo.ejemplo.es:8443/ofertas/42", FalloDescarga.DireccionNoPermitida)]
    [InlineData("https://interno.ejemplo.es/ofertas/42", FalloDescarga.DireccionNoPermitida)]
    [InlineData("https://127.0.0.1/", FalloDescarga.DireccionNoPermitida)]
    [InlineData("https://[::ffff:10.0.0.1]/", FalloDescarga.DireccionNoPermitida)]
    [InlineData("https://2130706433/", FalloDescarga.DireccionNoPermitida)]
    public async Task Descargar_UnEnlaceNoAdmitido_FallaSinPedirNada(string enlace, FalloDescarga fallo)
    {
        var descarga = await Descargador().DescargarAsync(enlace, TipoDescarga.Pagina, CancellationToken.None);

        Assert.Equal(fallo, descarga.Fallo);
        Assert.Empty(_web.Peticiones);
    }

    [Fact]
    public async Task Descargar_HastaTresRedirecciones_LasSigueYDaLaUrlFinal()
    {
        _web.Responder(p => p.RequestUri!.AbsolutePath switch
        {
            "/ofertas/42" => Redireccion("/ofertas/42/"),
            "/ofertas/42/" => Redireccion("https://otro.ejemplo.es/a"),
            "/a" => Redireccion("https://otro.ejemplo.es/b?error=true"),
            _ => Respuesta("Ya no está disponible", "text/plain")
        });

        var descarga = await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);

        Assert.Equal(("Ya no está disponible", "https://otro.ejemplo.es/b?error=true"), (descarga.Texto, descarga.UrlFinal!.ToString()));
        Assert.Equal(4, _web.Peticiones.Count);
    }

    [Theory]
    [InlineData("http://otro.ejemplo.es/b", FalloDescarga.EnlaceNoValido)]
    [InlineData("https://interno.ejemplo.es/b", FalloDescarga.DireccionNoPermitida)]
    [InlineData("https://169.254.169.254/latest/meta-data/", FalloDescarga.DireccionNoPermitida)]
    [InlineData("https://otro.ejemplo.es:444/b", FalloDescarga.DireccionNoPermitida)]
    public async Task Descargar_UnaRedireccionNoAdmitida_SeRechazaSinSeguirla(string destino, FalloDescarga fallo)
    {
        _web.Responder(_ => Redireccion(destino));

        var descarga = await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);

        Assert.Equal(fallo, descarga.Fallo);
        Assert.Single(_web.Peticiones);
    }

    [Fact]
    public async Task Descargar_LaCuartaRedireccion_NoSeSigue()
    {
        _web.Responder(_ => Redireccion($"/salto{_web.Peticiones.Count}"));

        var descarga = await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);

        Assert.Equal(((FalloDescarga?)FalloDescarga.DemasiadasRedirecciones, (int?)302), (descarga.Fallo, descarga.Codigo));
        Assert.Equal(4, _web.Peticiones.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Descargar_UnCodigoQueNoEs2xx_FallaConElCodigo(HttpStatusCode codigo)
    {
        _web.Responder(_ => Respuesta("<p>No</p>", codigo: codigo));

        var descarga = await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);

        Assert.Equal(((FalloDescarga?)FalloDescarga.CodigoNoValido, (int?)(int)codigo, (string?)null), (descarga.Fallo, descarga.Codigo, descarga.Texto));
    }

    [Theory]
    [InlineData(TipoDescarga.Pagina, "text/html", true)]
    [InlineData(TipoDescarga.Pagina, "text/plain", true)]
    [InlineData(TipoDescarga.Pagina, "application/json", false)]
    [InlineData(TipoDescarga.Pagina, "application/pdf", false)]
    [InlineData(TipoDescarga.Pagina, "image/svg+xml", false)]
    [InlineData(TipoDescarga.ListadoJson, "application/json", true)]
    [InlineData(TipoDescarga.ListadoJson, "text/html", false)]
    public async Task Descargar_ElTipoDeContenido_DependeDeLoQueSeDescarga(TipoDescarga tipo, string tipoContenido, bool admitido)
    {
        _web.Responder(_ => Respuesta("{\"jobs\":[]}", tipoContenido));

        var descarga = await Descargador().DescargarAsync(tipo == TipoDescarga.ListadoJson ? Listado : Oferta, tipo, CancellationToken.None);

        Assert.Equal(admitido ? null : (FalloDescarga?)FalloDescarga.TipoNoAdmitido, descarga.Fallo);
        if (tipo == TipoDescarga.ListadoJson && admitido) Assert.Equal("{\"jobs\":[]}", descarga.Texto);
    }

    [Fact]
    public async Task Descargar_SinTipoDeContenido_NoSeAdmite()
    {
        _web.Responder(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("hola"u8.ToArray()) });

        Assert.Equal(FalloDescarga.TipoNoAdmitido, (await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None)).Fallo);
    }

    [Fact]
    public async Task Descargar_PasadoElTope_LaPaginaSeRecortaYElListadoFalla()
    {
        var opciones = OpcionesDescarga.PorDefecto with { TopeBytes = 100 };
        _web.Responder(p => Respuesta(new string('a', 500), p.RequestUri!.Host == "boards-api.greenhouse.io" ? "application/json" : "text/plain"));

        var pagina = await Descargador(opciones).DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);
        var listado = await Descargador(opciones).DescargarAsync(Listado, TipoDescarga.ListadoJson, CancellationToken.None);

        Assert.Equal((true, 100), (pagina.Recortada, pagina.Texto!.Length));
        Assert.Equal(FalloDescarga.DemasiadoGrande, listado.Fallo);
    }

    [Theory]
    [InlineData(99, false, 99)]
    [InlineData(100, false, 100)]
    [InlineData(101, true, 100)]
    public async Task Descargar_EnElBordeDelTope_SoloSeRecortaSiLoPasa(int largo, bool recortada, int esperado)
    {
        var opciones = OpcionesDescarga.PorDefecto with { TopeBytes = 100 };
        _web.Responder(p => Respuesta(new string('a', largo), p.RequestUri!.Host == "boards-api.greenhouse.io" ? "application/json" : "text/plain"));

        var pagina = await Descargador(opciones).DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);
        var listado = await Descargador(opciones).DescargarAsync(Listado, TipoDescarga.ListadoJson, CancellationToken.None);

        Assert.Equal((recortada, esperado), (pagina.Recortada, pagina.Texto!.Length));
        // El listado justo en el tope entra entero; con un byte más, falla.
        Assert.Equal(recortada ? FalloDescarga.DemasiadoGrande : null, listado.Fallo);
        if (!recortada) Assert.Equal(largo, listado.Texto!.Length);
    }

    /// <summary>
    /// El tope cuenta lo que llega de verdad, no lo que el servidor dice en <c>Content-Length</c>: diciendo menos no se
    /// cuela un cuerpo enorme, y diciendo más no se da por grande ni se reserva memoria para lo que no viene.
    /// </summary>
    [Theory]
    [InlineData(500, 10L, true, 100)]
    [InlineData(500, 100L, true, 100)]
    [InlineData(50, 1_000_000_000L, false, 50)]
    [InlineData(50, 10L, false, 50)]
    public async Task Descargar_ConUnContentLengthFalso_ElTopeCuentaElCuerpoReal(int largo, long declarado, bool recortada, int esperado)
    {
        var opciones = OpcionesDescarga.PorDefecto with { TopeBytes = 100 };
        _web.Responder(p =>
        {
            var respuesta = Respuesta(new string('a', largo), p.RequestUri!.Host == "boards-api.greenhouse.io" ? "application/json" : "text/plain");
            respuesta.Content.Headers.ContentLength = declarado;
            return respuesta;
        });

        var pagina = await Descargador(opciones).DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);
        var listado = await Descargador(opciones).DescargarAsync(Listado, TipoDescarga.ListadoJson, CancellationToken.None);

        Assert.Equal((recortada, esperado), (pagina.Recortada, pagina.Texto!.Length));
        Assert.Equal(recortada ? FalloDescarga.DemasiadoGrande : null, listado.Fallo);
        if (!recortada) Assert.Equal(largo, listado.Texto!.Length);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task Descargar_Comprimida_ElTopeCuentaLosBytesDescomprimidos(string compresion)
    {
        // 1 KB comprimido que se descomprime en 1 MB: con un tope de 10 KB se corta ahí.
        var comprimido = Comprimir(new byte[1024 * 1024].Select(_ => (byte)'a').ToArray(), compresion);
        _web.Responder(_ =>
        {
            var contenido = new ByteArrayContent(comprimido);
            contenido.Headers.ContentType = new("text/plain");
            contenido.Headers.ContentEncoding.Add(compresion);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = contenido };
        });

        var descarga = await Descargador(OpcionesDescarga.PorDefecto with { TopeBytes = 10_240 }).DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);

        Assert.True(comprimido.Length < 10_240);
        Assert.Equal((true, 10_240), (descarga.Recortada, descarga.Texto!.Length));
    }

    [Fact]
    public async Task Descargar_ConUnaCompresionDesconocida_NoSeAdmite()
    {
        _web.Responder(_ =>
        {
            var respuesta = Respuesta("hola", "text/plain");
            respuesta.Content.Headers.ContentEncoding.Add("zstd");
            return respuesta;
        });

        Assert.Equal(FalloDescarga.TipoNoAdmitido, (await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None)).Fallo);
    }

    [Fact]
    public async Task Descargar_EnLatin1_SeLeeConSuJuegoDeCaracteres()
    {
        _web.Responder(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.Latin1.GetBytes("Peña Ingeniería")) { Headers = { { "Content-Type", "text/plain; charset=iso-8859-1" } } } });

        Assert.Equal("Peña Ingeniería", (await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None)).Texto);
    }

    [Fact]
    public async Task Descargar_SiNoResponde_SeCortaPorTiempo()
    {
        _web.Responder(async (_, cancelacion) =>
        {
            await Task.Delay(Timeout.Infinite, cancelacion);
            return Respuesta("");
        });

        var descarga = await Descargador(OpcionesDescarga.PorDefecto with { TiempoMaximo = TimeSpan.FromMilliseconds(100) })
            .DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);

        Assert.Equal(FalloDescarga.TiempoAgotado, descarga.Fallo);
    }

    [Fact]
    public async Task Descargar_SiLaCancelaQuienLlama_LanzaLaCancelacion()
    {
        _web.Responder(async (_, cancelacion) =>
        {
            await Task.Delay(Timeout.Infinite, cancelacion);
            return Respuesta("");
        });
        using var corte = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, corte.Token));
    }

    [Fact]
    public async Task Descargar_SiElConectorRechazaAlConectarOFallaLaRed_LoDice()
    {
        _web.Responder(p => throw (p.RequestUri!.AbsolutePath == "/rechazo"
            ? new HttpRequestException("conexión", new DireccionNoPermitidaException("bloqueada"))
            : new HttpRequestException("sin red")));

        Assert.Equal(FalloDescarga.DireccionNoPermitida, (await Descargador().DescargarAsync("https://empleo.ejemplo.es/rechazo", TipoDescarga.Pagina, CancellationToken.None)).Fallo);
        Assert.Equal(FalloDescarga.ErrorDeRed, (await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None)).Fallo);
    }

    [Theory]
    [InlineData("https://empleo.ejemplo.es/jobs.json")]
    [InlineData("https://boards-api.greenhouse.io.ejemplo.es/v1/boards/x/jobs")]
    [InlineData("https://otro.boards-api.greenhouse.io/v1/boards/x/jobs")]
    public async Task Descargar_UnListadoDeOtroHost_FallaSinPedirNada(string enlace)
    {
        var descarga = await Descargador().DescargarAsync(enlace, TipoDescarga.ListadoJson, CancellationToken.None);

        Assert.Equal(FalloDescarga.DireccionNoPermitida, descarga.Fallo);
        Assert.Empty(_web.Peticiones);
    }

    [Theory]
    [InlineData("https://empleo.ejemplo.es/jobs.json", FalloDescarga.DireccionNoPermitida)]
    [InlineData("https://api.lever.co/v0/postings/empresa-ficticia?mode=json", null)]
    public async Task Descargar_UnListadoQueRedirige_SoloSigueAOtroHostDeListados(string destino, FalloDescarga? fallo)
    {
        _web.Responder(p => p.RequestUri!.AbsoluteUri == Listado ? Redireccion(destino) : Respuesta("[]", "application/json"));

        var descarga = await Descargador().DescargarAsync(Listado, TipoDescarga.ListadoJson, CancellationToken.None);

        Assert.Equal(fallo, descarga.Fallo);
        Assert.Equal(fallo is null ? 2 : 1, _web.Peticiones.Count);
    }

    [Fact]
    public async Task Descargar_UnNombreQueNoExisteEnElDns_EsErrorDeRedSinLanzar()
    {
        var dns = new ConectorSeguro(new ResolvedorQueFalla(), () => []);

        var descarga = await new DescargadorOfertasHttp(dns, null, _web).DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None);

        Assert.Equal(FalloDescarga.ErrorDeRed, descarga.Fallo);
        Assert.Empty(_web.Peticiones);
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    [InlineData("deflate")]
    public async Task Descargar_UnCuerpoQueNoEstaComprimidoComoDice_EsErrorDeRedSinLanzar(string compresion)
    {
        _web.Responder(_ =>
        {
            var respuesta = Respuesta("esto no está comprimido de ninguna manera, aunque lo diga la cabecera", "text/plain");
            respuesta.Content.Headers.ContentEncoding.Add(compresion);
            return respuesta;
        });

        Assert.Equal(FalloDescarga.ErrorDeRed, (await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None)).Fallo);
    }

    [Theory]
    [InlineData("utf-7")]
    [InlineData("no-existe")]
    public async Task Descargar_ConUnJuegoDeCaracteresNoAdmitido_LeeEnUtf8(string juego)
    {
        _web.Responder(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("Peña +AGEA-")) { Headers = { { "Content-Type", $"text/plain; charset={juego}" } } } });

        Assert.Equal("Peña +AGEA-", (await Descargador().DescargarAsync(Oferta, TipoDescarga.Pagina, CancellationToken.None)).Texto);
    }

    private sealed class ResolvedorQueFalla : IResolvedorDns
    {
        public Task<System.Net.IPAddress[]> ResolverAsync(string host, CancellationToken cancelacion) =>
            throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
    }

    [Fact]
    public void CrearManejador_SinProxyCookiesNiRedireccionesYConElConectorSeguro()
    {
        using var manejador = DescargadorOfertasHttp.CrearManejador(new ConectorSeguro(_dns));

        Assert.Equal((false, false, false, DecompressionMethods.None),
            (manejador.UseProxy, manejador.AllowAutoRedirect, manejador.UseCookies, manejador.AutomaticDecompression));
        Assert.Null(manejador.Credentials);
        Assert.NotNull(manejador.ConnectCallback);
    }

    private static byte[] Comprimir(byte[] datos, string compresion)
    {
        using var salida = new MemoryStream();
        using (Stream compresor = compresion switch
               {
                   "gzip" => new GZipStream(salida, CompressionLevel.SmallestSize),
                   "deflate" => new ZLibStream(salida, CompressionLevel.SmallestSize),
                   _ => new BrotliStream(salida, CompressionLevel.SmallestSize)
               })
            compresor.Write(datos);
        return salida.ToArray();
    }
}

/// <summary>Hace de internet en los tests: responde lo que diga el test y apunta lo que le piden.</summary>
internal sealed class ManejadorFalso : HttpMessageHandler
{
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder =
        (_, _) => throw new InvalidOperationException("Sin respuesta programada.");

    public List<HttpRequestMessage> Peticiones { get; } = [];

    public void Responder(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = (p, _) => Task.FromResult(responder(p));

    public void Responder(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Peticiones.Add(request);
        return _responder(request, cancellationToken);
    }
}
