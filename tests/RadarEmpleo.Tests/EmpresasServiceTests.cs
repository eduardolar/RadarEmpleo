using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests;

/// <summary><see cref="EmpresasService"/>: autocompletado de empresas.</summary>
public class EmpresasServiceTests : ArnesSqliteTests
{
    private readonly EmpresasService _empresas;
    private readonly CandidaturasService _candidaturas;

    public EmpresasServiceTests()
    {
        _empresas = new EmpresasService(Factory);
        _candidaturas = new CandidaturasService(Factory, Reloj);
    }

    [Fact]
    public async Task BuscarAsync_TextoConTildesYMayusculas_EncuentraLaEmpresaYCuentaSusCandidaturas()
    {
        await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Consultoría Pérez S.L.", "Backend"));
        await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("consultoria perez", "Frontend"));
        await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme", "Tester"));

        var resultado = await _empresas.BuscarAsync("PÉREZ");

        var empresa = Assert.Single(resultado);
        Assert.Equal("Consultoría Pérez S.L.", empresa.Nombre); // se muestra como se escribió la primera vez
        Assert.Equal(2, empresa.Candidaturas);
    }

    [Fact]
    public async Task CrearAsync_MismaEmpresaEnNfcYEnNfd_NoLaDuplica()
    {
        var nfc = "Ingeniería España S.L.".Normalize(System.Text.NormalizationForm.FormC);
        var nfd = "Ingeniería España".Normalize(System.Text.NormalizationForm.FormD);
        Assert.False(string.Equals("Ingeniería España", nfd, StringComparison.Ordinal)); // de verdad vienen distintas

        var primera = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest(nfc, "Backend"));
        var segunda = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest(nfd, "Frontend"));

        Assert.Equal(primera.EmpresaId, segunda.EmpresaId);
        Assert.Single(await _empresas.BuscarAsync("españa"));
    }

    [Fact]
    public async Task BuscarAsync_SinTexto_DevuelveTodasPorOrdenAlfabetico()
    {
        await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Zeta Labs", "Backend"));
        await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme", "Tester"));

        var resultado = await _empresas.BuscarAsync(null);

        Assert.Equal(["Acme", "Zeta Labs"], resultado.Select(e => e.Nombre));
    }
}
