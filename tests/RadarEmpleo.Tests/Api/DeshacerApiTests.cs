using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// <c>POST /api/candidaturas/{id}/estado/deshacer</c> por HTTP contra la API real, con un reloj fijo para
/// poder probar los 10 minutos. Cada test estrena API y BD.
/// </summary>
public sealed class DeshacerApiTests : IDisposable
{
    private readonly FabricaApi _fabrica = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc));
    private readonly HttpClient _http;

    public DeshacerApiTests()
    {
        _http = _fabrica.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IClock>();
            s.AddSingleton<IClock>(_reloj);
        })).CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _fabrica.Dispose();
    }

    private static string Url(int id) => $"/api/candidaturas/{id}/estado/deshacer";

    private async Task<CandidaturaDetalleDto> CrearAsync()
    {
        var respuesta = await _http.PostAsJsonAsync("/api/candidaturas",
            new GuardarCandidaturaRequest("Acme Software S.L.", "Desarrollador .NET"), FabricaApi.Json);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!;
    }

    /// <summary>Cambia el estado y devuelve el id del apunte creado, como lo saca el front de la respuesta.</summary>
    private async Task<int> CambiarAsync(int id, object peticion)
    {
        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        var respuesta = await _http.PostAsJsonAsync($"/api/candidaturas/{id}/estado", peticion, FabricaApi.Json);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!.Historial[^1].Id;
    }

    private Task<HttpResponseMessage> DeshacerAsync(int id, int cambioId) =>
        _http.PostAsync(Url(id), new StringContent($$"""{"cambioId":{{cambioId}}}""", Encoding.UTF8, "application/json"));

    private Task<CandidaturaDetalleDto?> FichaAsync(int id) =>
        _http.GetFromJsonAsync<CandidaturaDetalleDto>($"/api/candidaturas/{id}", FabricaApi.Json);

    private static async Task<string?> DetalleConflictoAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();
    }

    [Fact]
    public async Task EnviadaAPreparada_DevuelveLaFichaComoEstabaYSinElApunte()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, new { estado = "Preparada" });
        var envio = await CambiarAsync(creada.Id, new { estado = "Enviada" });

        var respuesta = await DeshacerAsync(creada.Id, envio);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var detalle = (await respuesta.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!;
        Assert.Equal(EstadoCandidatura.Preparada, detalle.Estado);
        Assert.Null(detalle.FechaEnvio);
        Assert.Empty(detalle.Recordatorios);
        Assert.Equal(2, detalle.Historial.Count);
        Assert.Contains(EstadoCandidatura.Enviada, detalle.EstadosPosibles);
        Assert.DoesNotContain((await FichaAsync(creada.Id))!.Historial, h => h.A == EstadoCandidatura.Enviada);
    }

    [Fact]
    public async Task Descartar_SeDeshaceSinMotivo()
    {
        var creada = await CrearAsync();
        var descarte = await CambiarAsync(creada.Id,
            new { estado = "Descartada", motivoDescarte = "Experiencia", motivoDescarteTexto = "Piden 5 años" });

        var respuesta = await DeshacerAsync(creada.Id, descarte);

        var detalle = (await respuesta.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!;
        Assert.Equal(EstadoCandidatura.Detectada, detalle.Estado);
        Assert.Null(detalle.MotivoDescarte);
        Assert.Null(detalle.MotivoDescarteTexto);
    }

    [Fact]
    public async Task DosPestanas_LaOtraHaMovidoDespues_Devuelve409YNoTocaNada()
    {
        var creada = await CrearAsync();
        var deLaPestanaA = await CambiarAsync(creada.Id, new { estado = "Enviada" });
        await CambiarAsync(creada.Id, new { estado = "Entrevista" });

        Assert.Equal("Ese cambio ya no es el último.", await DetalleConflictoAsync(await DeshacerAsync(creada.Id, deLaPestanaA)));

        var ficha = (await FichaAsync(creada.Id))!;
        Assert.Equal(EstadoCandidatura.Entrevista, ficha.Estado);
        Assert.Equal(3, ficha.Historial.Count);
    }

    [Fact]
    public async Task DosPeticionesALaVez_SoloDeshaceUnPaso()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, new { estado = "Preparada" });
        var envio = await CambiarAsync(creada.Id, new { estado = "Enviada" });

        var respuestas = await Task.WhenAll(DeshacerAsync(creada.Id, envio), DeshacerAsync(creada.Id, envio));

        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(EstadoCandidatura.Preparada, (await FichaAsync(creada.Id))!.Estado);
    }

    [Fact]
    public async Task SinCambioId_Devuelve400EnEspanol()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, new { estado = "Preparada" });

        var respuesta = await _http.PostAsync(Url(creada.Id), new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Falta el cambio que se quiere deshacer.", problema.GetProperty("errors").GetProperty("cambioId")[0].GetString());
        Assert.Equal(EstadoCandidatura.Preparada, (await FichaAsync(creada.Id))!.Estado);
    }

    [Fact]
    public async Task SinCambios_Devuelve409EnEspanol()
    {
        var creada = await CrearAsync();

        Assert.Equal("No hay ningún cambio de estado que deshacer.",
            await DetalleConflictoAsync(await DeshacerAsync(creada.Id, creada.Historial[0].Id)));
    }

    [Fact]
    public async Task PasadosDiezMinutos_Devuelve409()
    {
        var creada = await CrearAsync();
        var preparada = await CambiarAsync(creada.Id, new { estado = "Preparada" });
        _reloj.Avanzar(TimeSpan.FromMinutes(10));

        Assert.Equal("Ya no se puede deshacer este cambio.", await DetalleConflictoAsync(await DeshacerAsync(creada.Id, preparada)));
    }

    [Fact]
    public async Task Inexistente_Devuelve404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await DeshacerAsync(999, 1)).StatusCode);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded", "cambioId=2")]
    [InlineData("text/plain", """{"cambioId":2}""")]
    public async Task SinJson_Devuelve415YNoDeshaceNada(string tipo, string cuerpo)
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, new { estado = "Preparada" });
        using var contenido = new StringContent(cuerpo, Encoding.UTF8);
        contenido.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(tipo);

        var respuesta = await _http.PostAsync(Url(creada.Id), contenido);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        Assert.Equal(EstadoCandidatura.Preparada, (await FichaAsync(creada.Id))!.Estado);
    }
}
