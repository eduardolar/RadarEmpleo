using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RadarEmpleo.Api.Errores;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// <c>PUT /api/candidaturas/{id}</c> con la versión que se leyó (<c>fechaUltimoCambioLeida</c>) por HTTP
/// contra la API real: la fecha viaja tal cual la devolvió la API, como hace el front. Cada test estrena API y BD.
/// </summary>
public sealed class EditarConVersionApiTests : IDisposable
{
    private readonly FabricaApi _fabrica = new();
    private readonly HttpClient _http;

    public EditarConVersionApiTests() => _http = _fabrica.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _fabrica.Dispose();
    }

    /// <summary>Crea una candidatura y devuelve su id y su <c>fechaUltimoCambio</c> como texto del JSON.</summary>
    private async Task<(int Id, string Version)> CrearAsync()
    {
        var respuesta = await _http.PostAsJsonAsync("/api/candidaturas",
            new GuardarCandidaturaRequest("Acme Software S.L.", "Desarrollador .NET"), FabricaApi.Json);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return LeerVersion(await respuesta.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static (int Id, string Version) LeerVersion(JsonElement ficha) =>
        (ficha.GetProperty("id").GetInt32(), ficha.GetProperty("fechaUltimoCambio").GetString()!);

    private Task<HttpResponseMessage> EditarAsync(int id, string puesto, string? version) =>
        _http.PutAsJsonAsync($"/api/candidaturas/{id}",
            new { empresa = "Acme Software S.L.", puesto, encaje = "Alto", pega = "La del formulario", fechaUltimoCambioLeida = version });

    private async Task AnalizarAsync(int id)
    {
        using var ambito = _fabrica.Services.CreateScope();
        Assert.True(await ambito.ServiceProvider.GetRequiredService<CandidaturasService>().AplicarAnalisisAgenteAsync(id,
            new ResultadoAnalisisEncaje(Encaje.Medio, "Piden inglés B2.", "- Falla el inglés.", "Encaje medio.")));
    }

    private Task<CandidaturaDetalleDto?> FichaAsync(int id) =>
        _http.GetFromJsonAsync<CandidaturaDetalleDto>($"/api/candidaturas/{id}", FabricaApi.Json);

    private static async Task AssertOtraPestanaAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var detalle = (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();
        Assert.Equal(ManejadorExcepciones.MensajeCambiadaEnOtraPestana, detalle);
    }

    [Fact]
    public async Task ConLaVersionDeAntesDelAnalisis_Da409YNoPisaElAnalisis()
    {
        var (id, version) = await CrearAsync();
        await AnalizarAsync(id);
        var analizada = (await FichaAsync(id))!;

        await AssertOtraPestanaAsync(await EditarAsync(id, "Lo del formulario viejo", version));

        var ficha = (await FichaAsync(id))!;
        Assert.Equal("Desarrollador .NET", ficha.Puesto);
        Assert.Equal(Encaje.Medio, ficha.Encaje);
        Assert.Equal("Piden inglés B2.", ficha.Pega);
        Assert.Equal("- Falla el inglés.", ficha.Analisis);
        Assert.Equal(analizada.FechaUltimoCambio, ficha.FechaUltimoCambio);
    }

    [Fact]
    public async Task ConLaVersionDeDespuesDelAnalisis_Da200()
    {
        var (id, _) = await CrearAsync();
        await AnalizarAsync(id);
        var (_, version) = LeerVersion(await _http.GetFromJsonAsync<JsonElement>($"/api/candidaturas/{id}"));

        var respuesta = await EditarAsync(id, "Desarrollador .NET senior", version);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Desarrollador .NET senior", (await FichaAsync(id))!.Puesto);
    }

    [Fact]
    public async Task DosGuardadosConLaMismaVersion_ElSegundoDa409()
    {
        var (id, version) = await CrearAsync();

        Assert.Equal(HttpStatusCode.OK, (await EditarAsync(id, "Primera pestaña", version)).StatusCode);
        await AssertOtraPestanaAsync(await EditarAsync(id, "Segunda pestaña", version));

        Assert.Equal("Primera pestaña", (await FichaAsync(id))!.Puesto);
    }

    [Fact]
    public async Task SinVersion_GuardaComoAntes()
    {
        // Un cliente anterior a la versión (una pestaña o el móvil con la app en caché) sigue pudiendo guardar
        var (id, _) = await CrearAsync();
        await AnalizarAsync(id);

        var respuesta = await EditarAsync(id, "Sin versión", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Sin versión", (await FichaAsync(id))!.Puesto);
    }
}
