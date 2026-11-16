using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RadarEmpleo.Api.Seguridad;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// El <c>Origen</c> lo pone el servidor según el canal: Manual por el PC, Movil por el móvil. Un <c>"origen"</c> en el
/// cuerpo (aunque sea «Agente») se ignora.
/// </summary>
public sealed class OrigenServidorApiTests
{
    private static Task<HttpResponseMessage> PostJson(HttpClient http, string url, string json) =>
        http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    [Theory]
    [InlineData(CanalPeticion.Local, "Manual")]
    [InlineData(CanalPeticion.Remoto, "Movil")]
    public async Task AltaEstadoYNota_LlevanElOrigenDelCanalYNoElDelCuerpo(CanalPeticion canal, string esperado)
    {
        using var fabrica = new FabricaApi(canal: canal);
        using var http = fabrica.CreateClient();

        var alta = await PostJson(http, "/api/candidaturas", """{"empresa":"Acme","puesto":"Desarrollador .NET","origen":"Agente"}""");
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var ficha = await alta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(esperado, ficha.GetProperty("origen").GetString());
        var id = ficha.GetProperty("id").GetInt32();

        var estado = await PostJson(http, $"/api/candidaturas/{id}/estado", """{"estado":"Preparada","origen":"Agente"}""");
        Assert.Equal(HttpStatusCode.OK, estado.StatusCode);
        var historial = (await estado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("historial");
        Assert.All(historial.EnumerateArray(), h => Assert.Equal(esperado, h.GetProperty("origen").GetString()));

        var nota = await PostJson(http, $"/api/candidaturas/{id}/notas", """{"texto":"Llamar el lunes","origen":"Registro"}""");
        Assert.Equal(HttpStatusCode.Created, nota.StatusCode);
        Assert.Equal(esperado, (await nota.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("origen").GetString());
    }

    [Fact]
    public async Task UnOrigenQueNoExiste_SeIgnoraIgual()
    {
        using var fabrica = new FabricaApi();
        using var http = fabrica.CreateClient();

        var alta = await PostJson(http, "/api/candidaturas", """{"empresa":"Acme","puesto":"Programador","origen":99}""");

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        Assert.Equal("Manual", (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("origen").GetString());
    }
}
