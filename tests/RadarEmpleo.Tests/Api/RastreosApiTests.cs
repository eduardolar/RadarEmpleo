using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// Rastreos por HTTP contra la API real, con la carpeta Curriculum temporal de <see cref="FabricaApi"/>: la
/// sección «## Rastreo 03/11» del registro inventado y <c>rastreos\rastreo_2026-11-09.md</c>.
/// </summary>
public sealed class RastreosApiTests : IDisposable
{
    private const string Escanear = "/api/rastreos/escanear";
    private const string EnlaceLunesUno = "https://ejemplo.com/ofertas/3001";

    // La del informe sale de su nombre; la de la sección «03/11» del registro depende del año en que se lea.
    private static readonly DateOnly FechaDelLunes = new(2026, 11, 9);

    private readonly FabricaApi _fabrica = new();
    private readonly HttpClient _http;

    public RastreosApiTests() => _http = _fabrica.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _fabrica.Dispose();
    }

    private string Ruta(string relativa) => Path.Combine(_fabrica.CarpetaCurriculum, relativa);

    private Task<HttpResponseMessage> PostVacioAsync(string url) => _http.PostAsJsonAsync(url, new { });

    private async Task<ResultadoEscaneoDto> EscanearAsync()
    {
        var respuesta = await PostVacioAsync(Escanear);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ResultadoEscaneoDto>(FabricaApi.Json))!;
    }

    private async Task<RastreoDetalleDto> InformeDelLunesAsync()
    {
        var lista = await _http.GetFromJsonAsync<List<RastreoResumenDto>>("/api/rastreos", FabricaApi.Json);
        var lunes = lista!.Single(r => r.Fecha == FechaDelLunes);
        return (await _http.GetFromJsonAsync<RastreoDetalleDto>($"/api/rastreos/{lunes.Id}", FabricaApi.Json))!;
    }

    private static string Hash(string ruta) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ruta)));

    [Fact]
    public async Task Escanear_DevuelveElContrato()
    {
        var resultado = await (await PostVacioAsync(Escanear)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, resultado.GetProperty("rastreosNuevos").GetInt32());
        Assert.Equal(0, resultado.GetProperty("rastreosActualizados").GetInt32());
        Assert.Equal(8, resultado.GetProperty("ofertasNuevas").GetInt32());
        Assert.Equal(0, resultado.GetProperty("yaEnElTablero").GetInt32());
        Assert.Equal(3, resultado.GetProperty("lineasSinLeer").GetInt32());
        Assert.Empty(resultado.GetProperty("avisos").EnumerateArray());
    }

    [Fact]
    public async Task ListarYVer_ContratoDeLaListaYDelDetalle()
    {
        await EscanearAsync();

        var lista = await _http.GetFromJsonAsync<JsonElement>("/api/rastreos");
        Assert.Equal(2, lista.GetArrayLength());
        var primero = lista.EnumerateArray().Single(r => r.GetProperty("fecha").GetString() == "2026-11-09");
        Assert.Equal("TareaLunes", primero.GetProperty("origen").GetString());
        Assert.Equal(2, primero.GetProperty("total").GetInt32());
        Assert.Equal(2, primero.GetProperty("pendientes").GetInt32());
        Assert.EndsWith("Z", primero.GetProperty("fechaEscaneo").GetString());

        var detalle = await _http.GetFromJsonAsync<JsonElement>($"/api/rastreos/{primero.GetProperty("id").GetInt32()}");
        Assert.Equal("rastreos/rastreo_2026-11-09.md", detalle.GetProperty("rutaInforme").GetString());
        Assert.StartsWith("Semana tranquila.", detalle.GetProperty("informe").GetString());
        var oferta = detalle.GetProperty("ofertas").EnumerateArray().First();
        Assert.Equal("Empresa Lunes Uno", oferta.GetProperty("empresa").GetString());
        Assert.Equal("Programador/a Angular Junior", oferta.GetProperty("puesto").GetString());
        Assert.Equal(EnlaceLunesUno, oferta.GetProperty("enlace").GetString());
        Assert.Equal("SinValorar", oferta.GetProperty("encaje").GetString());
        Assert.Equal("Pendiente", oferta.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, oferta.GetProperty("candidaturaId").ValueKind);
        Assert.Equal("sueldo sin publicar.", oferta.GetProperty("pega").GetString());
        Assert.True(oferta.TryGetProperty("textoOriginal", out _));
    }

    [Fact]
    public async Task Ver_QueNoExiste_Devuelve404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/rastreos/999")).StatusCode);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded", "a=1")]
    [InlineData("text/plain", "{}")]
    [InlineData("multipart/form-data; boundary=x", "--x--")]
    public async Task Post_SinJson_Devuelve415YNoHaceNada(string tipo, string cuerpo)
    {
        foreach (var url in new[] { Escanear, "/api/rastreos/ofertas/1/detectar", "/api/rastreos/ofertas/1/ignorar" })
        {
            using var contenido = new StringContent(cuerpo, Encoding.UTF8);
            contenido.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(tipo);

            var respuesta = await _http.PostAsync(url, contenido);

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        }
        Assert.Empty((await _http.GetFromJsonAsync<JsonElement>("/api/rastreos")).EnumerateArray());
    }

    [Fact]
    public async Task Post_SinCuerpo_Devuelve415YNoHaceNada()
    {
        var respuesta = await _http.PostAsync(Escanear, null);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, respuesta.StatusCode);
        Assert.Empty((await _http.GetFromJsonAsync<JsonElement>("/api/rastreos")).EnumerateArray());
    }

    [Fact]
    public async Task Detectar_OfertaDelInformeDelLunes_PasaADetectadaConOrigenRastreo()
    {
        await EscanearAsync();
        var oferta = (await InformeDelLunesAsync()).Ofertas.Single(o => o.Enlace == EnlaceLunesUno);
        var registroAntes = Hash(Ruta(RutasCurriculum.ArchivoRegistro));

        var respuesta = await PostVacioAsync($"/api/rastreos/ofertas/{oferta.Id}/detectar");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var resultado = (await respuesta.Content.ReadFromJsonAsync<ResultadoDeteccionDto>(FabricaApi.Json))!;
        Assert.False(resultado.YaExistia);
        Assert.Equal("Convertida", resultado.Oferta.Estado.ToString());

        var ficha = await _http.GetFromJsonAsync<JsonElement>($"/api/candidaturas/{resultado.CandidaturaId}");
        Assert.Equal("Detectada", ficha.GetProperty("estado").GetString());
        Assert.Equal("Rastreo", ficha.GetProperty("origen").GetString());
        Assert.Equal(EnlaceLunesUno, ficha.GetProperty("enlace").GetString());
        Assert.Equal("Empresa Lunes Uno", ficha.GetProperty("empresa").GetString());
        // Medianoche del 09/11 en Madrid.
        Assert.Equal("2026-11-08T23:00:00Z", ficha.GetProperty("fechaDeteccion").GetString());

        // Pasar al tablero regenera el radar (lo único que se escribe) y nunca toca el registro.
        Assert.True(File.Exists(Ruta(Path.Combine("radar", "candidaturas.md"))));
        Assert.Equal(registroAntes, Hash(Ruta(RutasCurriculum.ArchivoRegistro)));

        var otraVez = await PostVacioAsync($"/api/rastreos/ofertas/{oferta.Id}/detectar");
        var segundo = (await otraVez.Content.ReadFromJsonAsync<ResultadoDeteccionDto>(FabricaApi.Json))!;
        Assert.True(segundo.YaExistia);
        Assert.Equal(resultado.CandidaturaId, segundo.CandidaturaId);
        Assert.Single((await _http.GetFromJsonAsync<JsonElement>("/api/candidaturas")).EnumerateArray());
    }

    [Fact]
    public async Task VolverAEscanear_DespuesDeDetectar_NoCreaNada()
    {
        await EscanearAsync();
        var oferta = (await InformeDelLunesAsync()).Ofertas[0];
        await PostVacioAsync($"/api/rastreos/ofertas/{oferta.Id}/detectar");

        var segundo = await EscanearAsync();

        Assert.Equal((0, 0, 0), (segundo.RastreosNuevos, segundo.RastreosActualizados, segundo.OfertasNuevas));
        Assert.Equal("Convertida", (await InformeDelLunesAsync()).Ofertas[0].Estado.ToString());
    }

    [Fact]
    public async Task Ignorar_PendienteYLuegoConvertida_200Y409()
    {
        await EscanearAsync();
        var ofertas = (await InformeDelLunesAsync()).Ofertas;

        var ignorada = await PostVacioAsync($"/api/rastreos/ofertas/{ofertas[0].Id}/ignorar");
        Assert.Equal(HttpStatusCode.OK, ignorada.StatusCode);
        Assert.Equal("Ignorada", (await ignorada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estado").GetString());

        await PostVacioAsync($"/api/rastreos/ofertas/{ofertas[1].Id}/detectar");
        var conflicto = await PostVacioAsync($"/api/rastreos/ofertas/{ofertas[1].Id}/ignorar");
        Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
        Assert.Contains("ya está en el tablero", (await conflicto.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("detectar")]
    [InlineData("ignorar")]
    public async Task OfertaQueNoExiste_Devuelve404(string accion)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await PostVacioAsync($"/api/rastreos/ofertas/999/{accion}")).StatusCode);
    }

    [Fact]
    public async Task Escanear_ConLaTablaYaImportada_LaOfertaRepetidaSaleEnElTablero()
    {
        var vista = await (await _http.PostAsync("/api/importacion/tabla/previsualizar", null)).Content.ReadFromJsonAsync<JsonElement>();
        var claves = vista.GetProperty("filas").EnumerateArray().Select(f => f.GetProperty("clave").GetString()).ToList();
        var aplicar = await _http.PostAsJsonAsync("/api/importacion/lote/aplicar", new { lote = vista.GetProperty("lote"), claves });
        Assert.Equal(HttpStatusCode.OK, aplicar.StatusCode);

        var resultado = await EscanearAsync();

        Assert.Equal(1, resultado.YaEnElTablero);
    }
}
