using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary>Normalización de nombres de empresa (<see cref="NombresEmpresa.Normalizar"/>).</summary>
public class NombresEmpresaTests
{
    [Theory]
    [InlineData("Inditec Sistemas, S.A.", "inditec sistemas")]
    [InlineData("INDITEC SISTEMAS SA", "inditec sistemas")]
    [InlineData("  Inditec   Sistemas  ", "inditec sistemas")]
    [InlineData("Acme Software S.L.", "acme software")]
    [InlineData("Acme Software, SL", "acme software")]
    [InlineData("Acme Software S.L.U.", "acme software")]
    [InlineData("Acme Software S. L.", "acme software")]
    [InlineData("Telefónica Tech S.A.U.", "telefonica tech")]
    [InlineData("Ingeniería Ñandú Sociedad Limitada", "ingenieria ñandu")]
    [InlineData("Consultoría Pérez", "consultoria perez")]
    public void Normalizar_VariantesDeUnNombre_DevuelveLaFormaCanonica(string nombre, string esperado)
    {
        Assert.Equal(esperado, NombresEmpresa.Normalizar(nombre));
    }

    [Theory]
    [InlineData("Acme S.L.L.")]
    [InlineData("Acme SLL")]
    [InlineData("Acme S.L.P.")]
    [InlineData("Acme S.L.N.E.")]
    [InlineData("Acme S.A.L.")]
    [InlineData("Acme S.Coop.")]
    [InlineData("Acme S. Coop.")]
    [InlineData("Acme S.Coop. And.")]
    [InlineData("Acme Ltd")]
    [InlineData("Acme Ltd.")]
    [InlineData("Acme Inc.")]
    [InlineData("Acme, Inc.")]
    [InlineData("Acme LLC")]
    [InlineData("Acme GmbH")]
    [InlineData("Acme B.V.")]
    [InlineData("Acme BV")]
    [InlineData("Acme S.p.A.")]
    [InlineData("Acme SAS")]
    [InlineData("Acme S.A.S.")]
    [InlineData("Acme (S.L.)")]
    [InlineData("Acme (SA)")]
    [InlineData("Acme ( S.L.U. )")]
    [InlineData("Acme Sociedad Limitada Laboral")]
    public void Normalizar_OtrasFormasJuridicas_LasQuita(string nombre)
    {
        Assert.Equal("acme", NombresEmpresa.Normalizar(nombre));
    }

    [Theory]
    [InlineData("Nasa")]   // terminan en una forma jurídica pero sin separador: no lo son
    [InlineData("Visa")]
    [InlineData("Sasa")]
    [InlineData("Inca")]
    [InlineData("Saltec")]
    [InlineData("Grupo Inca")]       // con separador, pero la palabra sigue
    [InlineData("Grupo Sasa")]
    [InlineData("Talleres Saltec")]
    [InlineData("Hotel Spa")]        // «Spa» sin puntos no es S.p.A.
    [InlineData("Mar y Sal")]        // «Sal» sin puntos no es S.A.L.
    [InlineData("Banco Inversis")]
    public void Normalizar_NombresRealesParecidosAUnaForma_NoLosRecorta(string nombre)
    {
        Assert.Equal(nombre.ToLowerInvariant(), NombresEmpresa.Normalizar(nombre));
    }

    [Fact]
    public void Normalizar_TextoEnNfd_DaLoMismoQueEnNfc()
    {
        var nfd = "Ingeniería España".Normalize(System.Text.NormalizationForm.FormD);

        Assert.Equal("ingenieria españa", NombresEmpresa.Normalizar(nfd));
        Assert.Equal(NombresEmpresa.Normalizar("Ingeniería España"), NombresEmpresa.Normalizar(nfd));
    }

    [Theory]
    [InlineData(", S.L.")]
    [InlineData("...")]
    [InlineData("(S.A.)")]
    public void Normalizar_SoloFormaJuridicaOPuntuacion_DevuelveCadenaVacia(string nombre)
    {
        Assert.Equal("", NombresEmpresa.Normalizar(nombre));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalizar_Vacio_DevuelveCadenaVacia(string? nombre)
    {
        Assert.Equal("", NombresEmpresa.Normalizar(nombre));
    }
}
