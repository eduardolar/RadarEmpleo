using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// <c>GET /api/estadisticas</c> por HTTP contra la API real, con el reloj fijo el viernes 13/11/2026 a las
/// 10:00 de Madrid. Cada test estrena API y BD.
/// </summary>
public sealed class EstadisticasApiTests : IDisposable
{
    private readonly FabricaApi _fabrica = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc));
    private readonly HttpClient _http;

    public EstadisticasApiTests()
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

    [Fact]
    public async Task Get_SinDatos_Devuelve200ConLosEstadosYLasSemanasACero()
    {
        var respuesta = await _http.GetAsync("/api/estadisticas");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("total").GetInt32());
        // Los estados van por su nombre, como en el resto de la API
        Assert.Equal("Detectada", json.GetProperty("porEstado")[0].GetProperty("estado").GetString());
        Assert.Equal("2026-11-09", json.GetProperty("enviadasPorSemana")[7].GetProperty("lunes").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("respuesta").GetProperty("porcentaje").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("respuesta").GetProperty("diasMedios").ValueKind);
    }

    [Fact]
    public async Task Get_TrasEnviarYDescartar_CuentaLaEnviadaYElMotivo()
    {
        var alta = await _http.PostAsJsonAsync("/api/candidaturas",
            new GuardarCandidaturaRequest("Consultora Ejemplo", "Desarrollador .NET"), FabricaApi.Json);
        var candidatura = (await alta.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!;
        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await _http.PostAsJsonAsync($"/api/candidaturas/{candidatura.Id}/estado",
            new { estado = "Enviada" }, FabricaApi.Json)).StatusCode);
        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await _http.PostAsJsonAsync($"/api/candidaturas/{candidatura.Id}/estado",
            new { estado = "Descartada", motivoDescarte = "Ingles" }, FabricaApi.Json)).StatusCode);

        var estadisticas = (await _http.GetFromJsonAsync<EstadisticasDto>("/api/estadisticas", FabricaApi.Json))!;

        Assert.Equal(1, estadisticas.Total);
        Assert.Equal(1, estadisticas.EnviadasPorSemana[^1].Total);
        Assert.Equal(new RespuestaDto(1, 0, 0, null, 0), estadisticas.Respuesta);
        Assert.Equal([new TotalPorMotivoDto(Domain.MotivoDescarte.Ingles, 1)], estadisticas.MotivosDescarte);
    }
}
