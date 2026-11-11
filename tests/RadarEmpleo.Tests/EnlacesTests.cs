using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary><see cref="Enlaces"/>: los mismos casos que <c>normalizar-enlace.spec.ts</c> del front.</summary>
public class EnlacesTests
{
    [Theory]
    [InlineData("https://www.linkedin.com/jobs/1")]
    [InlineData("http://acme.com")]
    [InlineData("HTTPS://ACME.COM/x")]
    [InlineData("https://intranet/empleo")]
    public void Normalizar_YaEsUnaDireccionWeb_LaDejaComoEsta(string enlace)
    {
        Assert.Equal(enlace, Enlaces.Normalizar(enlace));
    }

    [Fact]
    public void Normalizar_EspaciosEnLosExtremos_LosQuita()
    {
        Assert.Equal("https://acme.com", Enlaces.Normalizar("  https://acme.com  "));
    }

    [Theory]
    [InlineData("www.linkedin.com/jobs/1", "https://www.linkedin.com/jobs/1")]
    [InlineData("acme.com", "https://acme.com")]
    [InlineData("acme.com:8080/empleo", "https://acme.com:8080/empleo")]
    [InlineData("//acme.com/empleo", "https://acme.com/empleo")]
    public void Normalizar_SinEsquema_LePoneHttps(string enlace, string esperado)
    {
        Assert.Equal(esperado, Enlaces.Normalizar(enlace));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalizar_Vacio_NoEsUnError(string? enlace)
    {
        Assert.Equal("", Enlaces.Normalizar(enlace));
    }

    [Theory]
    [InlineData("mailto:rrhh@acme.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://acme.com/oferta")]
    [InlineData("tel:600123123")]
    public void Normalizar_OtroEsquema_LoRechaza(string enlace)
    {
        Assert.Null(Enlaces.Normalizar(enlace));
    }

    [Theory]
    [InlineData("no es un enlace")]
    [InlineData("oferta")]
    [InlineData("https://")]
    public void Normalizar_NoEsUnaDireccion_LoRechaza(string enlace)
    {
        Assert.Null(Enlaces.Normalizar(enlace));
    }

    [Theory]
    [InlineData("https://www.linkedin.com/jobs/view/1/")]
    [InlineData("http://www.linkedin.com/jobs/view/1")]
    [InlineData("HTTPS://WWW.LinkedIn.com/jobs/view/1")]
    [InlineData("www.linkedin.com/jobs/view/1")]
    public void ParaComparar_LaMismaOfertaEscritaDistinto_DaLoMismo(string enlace)
    {
        Assert.Equal("https://www.linkedin.com/jobs/view/1", Enlaces.ParaComparar(enlace));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mailto:rrhh@acme.com")]
    public void ParaComparar_SinEnlaceWeb_Null(string? enlace)
    {
        Assert.Null(Enlaces.ParaComparar(enlace));
    }
}
