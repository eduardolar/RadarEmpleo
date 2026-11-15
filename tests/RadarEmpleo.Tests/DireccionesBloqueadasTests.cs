using System.Net;
using RadarEmpleo.Services.Ofertas;

namespace RadarEmpleo.Tests;

/// <summary><see cref="DireccionesBloqueadas"/>: la descarga de ofertas solo conecta con direcciones públicas de internet.</summary>
public class DireccionesBloqueadasTests
{
    [Theory]
    // IPv4: sin especificar, «esta red», privadas, CGNAT, loopback, link-local, IETF, documentación, 6to4 relay,
    // pruebas de rendimiento, multicast, reservadas y difusión.
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("192.0.2.10")]
    [InlineData("192.88.99.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    // IPv6: sin especificar, loopback, compatibles con IPv4, IPv4 dentro de IPv6 (privadas), SIIT, NAT64, descarte,
    // Teredo, documentación, 6to4, locales únicas, link-local, site-local y multicast.
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::7f00:1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:0:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    [InlineData("64:ff9b:1::1")]
    [InlineData("100::1")]
    [InlineData("2001:0:4136:e378::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:c0a8:101::1")]
    [InlineData("2002:808:808::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fe80::1")]
    [InlineData("fe80::1%12")]
    [InlineData("febf::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    public void EstaBloqueada_LoQueNoEsInternetPublico_SeRechaza(string direccion)
    {
        Assert.True(DireccionesBloqueadas.EstaBloqueada(IPAddress.Parse(direccion), []));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("192.0.1.1")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.1")]
    [InlineData("223.255.255.255")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4003:80e::200e")]
    [InlineData("2001:4860:4860::8888")]
    public void EstaBloqueada_UnaDireccionPublica_SePermite(string direccion)
    {
        Assert.False(DireccionesBloqueadas.EstaBloqueada(IPAddress.Parse(direccion), []));
    }

    [Theory]
    [InlineData("2a0c:5a80:1234:5678::20", 64, "2a0c:5a80:1234:5678:abcd::1", true)]
    [InlineData("2a0c:5a80:1234:5678::20", 64, "2a0c:5a80:1234:5679::1", false)]
    [InlineData("81.40.10.20", 24, "81.40.10.200", true)]
    [InlineData("81.40.10.20", 24, "81.40.11.1", false)]
    [InlineData("81.40.10.20", 0, "81.40.10.20", true)]
    [InlineData("81.40.10.20", 0, "81.40.10.21", false)]
    [InlineData("::ffff:81.40.10.20", 32, "81.40.10.20", true)]
    public void EstaBloqueada_LaRedDeEstePc_SeRechaza(string delPc, int prefijo, string direccion, bool bloqueada)
    {
        var redes = new[] { new RedLocal(IPAddress.Parse(delPc), prefijo) };
        Assert.Equal(bloqueada, DireccionesBloqueadas.EstaBloqueada(IPAddress.Parse(direccion), redes));
    }

    [Fact]
    public void DeEstePc_TraeLoopbackYTodasQuedanBloqueadas()
    {
        // Las interfaces de este PC: en el CI (Linux) dependen del contenedor, así que solo se mira en Windows.
        if (!OperatingSystem.IsWindows()) return;

        var redes = DireccionesBloqueadas.DeEstePc();

        Assert.Contains(redes, r => IPAddress.IsLoopback(r.Direccion));
        Assert.All(redes, r => Assert.True(DireccionesBloqueadas.EstaBloqueada(r.Direccion, redes), r.Direccion.ToString()));
    }
}
