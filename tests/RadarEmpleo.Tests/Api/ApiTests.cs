using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RadarEmpleo.Api.Configuracion;
using RadarEmpleo.Api.Errores;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// Tests de integración por HTTP contra la API real (<see cref="FabricaApi"/>): salud, información del
/// sistema, alta, flujo hasta «Enviada» y los códigos de error (400 en español, 404, 409). Cada test
/// estrena API y BD.
/// </summary>
public sealed class ApiTests : IDisposable
{
    private readonly FabricaApi _fabrica = new();
    private readonly HttpClient _http;

    public ApiTests() => _http = _fabrica.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _fabrica.Dispose();
    }

    private async Task<CandidaturaDetalleDto> CrearAsync(string empresa = "Acme Software S.L.", string puesto = "Desarrollador .NET")
    {
        var respuesta = await _http.PostAsJsonAsync("/api/candidaturas", new GuardarCandidaturaRequest(empresa, puesto), FabricaApi.Json);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!;
    }

    private Task<HttpResponseMessage> CambiarEstadoAsync(int id, object peticion) =>
        _http.PostAsJsonAsync($"/api/candidaturas/{id}/estado", peticion, FabricaApi.Json);

    private Task<HttpResponseMessage> PostJsonCrudoAsync(string url, string json) =>
        _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>
    /// Comprueba que la respuesta es un 400 de validación en español y sin nombres internos, y
    /// devuelve el ProblemDetails.
    /// </summary>
    private static async Task<JsonElement> AssertBadRequestEnEspanolAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("RadarEmpleo.", texto);
        Assert.DoesNotContain("System.", texto);
        Assert.DoesNotContain("field is required", texto);
        Assert.DoesNotContain("$.", texto);
        var problema = JsonDocument.Parse(texto).RootElement;
        Assert.Equal(400, problema.GetProperty("status").GetInt32());
        Assert.Equal("Hay datos que no son válidos.", problema.GetProperty("title").GetString());
        return problema;
    }

    [Fact]
    public async Task Health_ApiArrancada_Devuelve200Healthy()
    {
        var respuesta = await _http.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Healthy", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Fabrica_CarpetaCurriculum_EsLaTemporalDelTestYNoLaReal()
    {
        var opciones = _fabrica.Services.GetRequiredService<IOptions<OpcionesRadar>>().Value;

        Assert.Equal(_fabrica.CarpetaCurriculum, opciones.CarpetaCurriculum);
        Assert.StartsWith(Path.GetTempPath(), opciones.CarpetaCurriculum);
    }

    [Fact]
    public async Task SistemaInfo_ConfiguracionPorDefecto_DevuelveModoLocalYAgentesApagados()
    {
        var json = await _http.GetFromJsonAsync<JsonElement>("/api/sistema/info");

        Assert.Equal("Local", json.GetProperty("modo").GetString());
        Assert.False(json.GetProperty("agentesHabilitados").GetBoolean());
        Assert.Equal("0.1.0", json.GetProperty("version").GetString());
    }

    [Fact]
    public async Task CrearYPasarAEnviada_FlujoCompleto_DetalleConHistorialYSeguimientoASieteDias()
    {
        var creada = await CrearAsync();
        Assert.Equal(EstadoCandidatura.Detectada, creada.Estado);

        var respuesta = await CambiarEstadoAsync(creada.Id, new { estado = "Enviada", comentario = "Enviada por la web" });
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var detalle = (await _http.GetFromJsonAsync<CandidaturaDetalleDto>($"/api/candidaturas/{creada.Id}", FabricaApi.Json))!;
        Assert.Equal(EstadoCandidatura.Enviada, detalle.Estado);
        Assert.NotNull(detalle.FechaEnvio);
        Assert.Equal(DateTimeKind.Utc, detalle.FechaEnvio!.Value.Kind);
        Assert.Equal([EstadoCandidatura.Detectada, EstadoCandidatura.Enviada], detalle.Historial.Select(h => h.A));
        var seguimiento = Assert.Single(detalle.Recordatorios);
        Assert.Equal(TipoRecordatorio.Seguimiento, seguimiento.Tipo);
        Assert.Equal(ReglasEstado.FechaSeguimiento(detalle.FechaEnvio.Value), seguimiento.FechaHora);
        Assert.Equal(new TimeSpan(9, 0, 0), Fechas.UtcAMadrid(seguimiento.FechaHora).TimeOfDay);
    }

    [Fact]
    public async Task Fechas_SeSerializanEnUtcConZ()
    {
        var creada = await CrearAsync();
        await CambiarEstadoAsync(creada.Id, new { estado = "Enviada" });

        var json = await _http.GetFromJsonAsync<JsonElement>($"/api/candidaturas/{creada.Id}");

        Assert.EndsWith("Z", json.GetProperty("fechaDeteccion").GetString());
        Assert.EndsWith("Z", json.GetProperty("fechaEnvio").GetString());
        Assert.EndsWith("Z", json.GetProperty("fechaEstado").GetString());
        Assert.EndsWith("Z", json.GetProperty("fechaUltimoCambio").GetString());
        Assert.EndsWith("Z", json.GetProperty("historial")[0].GetProperty("fecha").GetString());
        Assert.EndsWith("Z", json.GetProperty("recordatorios")[0].GetProperty("fechaHora").GetString());

        var lista = await _http.GetFromJsonAsync<JsonElement>("/api/candidaturas");
        Assert.EndsWith("Z", lista[0].GetProperty("fechaDeteccion").GetString());
        Assert.EndsWith("Z", lista[0].GetProperty("fechaEstado").GetString());
    }

    [Fact]
    public async Task CambiarEstado_EstadoNumerico_Devuelve400YNoCambiaNada()
    {
        var creada = await CrearAsync();

        var respuesta = await PostJsonCrudoAsync($"/api/candidaturas/{creada.Id}/estado", """{"estado":99}""");

        var problema = await AssertBadRequestEnEspanolAsync(respuesta);
        Assert.Equal("El valor de «estado» no es válido.", problema.GetProperty("detail").GetString());
        Assert.True(problema.GetProperty("errors").TryGetProperty("estado", out _));
        var detalle = (await _http.GetFromJsonAsync<CandidaturaDetalleDto>($"/api/candidaturas/{creada.Id}", FabricaApi.Json))!;
        Assert.Equal(EstadoCandidatura.Detectada, detalle.Estado);
        Assert.Single(detalle.Historial);
    }

    [Theory]
    [InlineData("""{"estado":"2"}""")]
    [InlineData("""{"estado":"Inventado"}""")]
    [InlineData("""{"estado":"Enviada","origen":3}""")]
    public async Task CambiarEstado_EnumNoValido_Devuelve400(string json)
    {
        var creada = await CrearAsync();

        await AssertBadRequestEnEspanolAsync(await PostJsonCrudoAsync($"/api/candidaturas/{creada.Id}/estado", json));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"estado":null}""")]
    [InlineData("""{"comentario":"sin estado"}""")]
    public async Task CambiarEstado_SinEstado_Devuelve400ElEstadoEsObligatorio(string json)
    {
        var creada = await CrearAsync();
        await CambiarEstadoAsync(creada.Id, new { estado = "Descartada", motivoDescarte = "Zona" });

        var respuesta = await PostJsonCrudoAsync($"/api/candidaturas/{creada.Id}/estado", json);

        var problema = await AssertBadRequestEnEspanolAsync(respuesta);
        Assert.Equal("El estado es obligatorio.", problema.GetProperty("detail").GetString());
        Assert.Equal("El estado es obligatorio.", problema.GetProperty("errors").GetProperty("estado")[0].GetString());
        // No se ha reabierto en silencio.
        var detalle = (await _http.GetFromJsonAsync<CandidaturaDetalleDto>($"/api/candidaturas/{creada.Id}", FabricaApi.Json))!;
        Assert.Equal(EstadoCandidatura.Descartada, detalle.Estado);
    }

    [Fact]
    public async Task CrearCandidatura_ModalidadNumerica_Devuelve400()
    {
        var respuesta = await PostJsonCrudoAsync("/api/candidaturas", """{"empresa":"Acme","puesto":"Backend","modalidad":42}""");

        var problema = await AssertBadRequestEnEspanolAsync(respuesta);
        Assert.Equal("El valor de «modalidad» no es válido.", problema.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("""{"empresa":"Acme", "puesto": """)]
    [InlineData("""{"empresa": "A", "puesto": """)]
    [InlineData("""{"empresa":"Acme", "puesto":"Back""")]
    [InlineData("""{"empresa":"Acme" "puesto":"Backend"}""")]
    [InlineData("""{"empresa":"Acme", "puesto":Backend}""")]
    [InlineData("""{"empresa":"Acme", "puesto":"Backend",}""")]
    public async Task CrearCandidatura_JsonCortadoOMalFormado_Devuelve400ConMensajeGenerico(string json)
    {
        var respuesta = await PostJsonCrudoAsync("/api/candidaturas", json);

        var problema = await AssertBadRequestEnEspanolAsync(respuesta);
        Assert.Equal("Falta el cuerpo de la petición o no es un JSON válido.", problema.GetProperty("detail").GetString());
        Assert.True(problema.GetProperty("errors").TryGetProperty("cuerpo", out _));
    }

    [Theory]
    [InlineData("""{"empresa":"Acme", "puesto": 123}""", "puesto")]
    [InlineData("""{"empresa":"Acme", "puesto": ["Backend"]}""", "puesto")]
    [InlineData("""{"empresa":"Acme", "puesto":"Backend", "modalidad":"Marte"}""", "modalidad")]
    [InlineData("""{"empresa":"Acme", "puesto":"Backend", "fechaDeteccion":"ayer"}""", "fechaDeteccion")]
    public async Task CrearCandidatura_ValorDeTipoIncorrecto_Devuelve400ConElCampo(string json, string campo)
    {
        var respuesta = await PostJsonCrudoAsync("/api/candidaturas", json);

        var problema = await AssertBadRequestEnEspanolAsync(respuesta);
        Assert.Equal($"El valor de «{campo}» no es válido.", problema.GetProperty("detail").GetString());
        Assert.True(problema.GetProperty("errors").TryGetProperty(campo, out _));
    }

    [Fact]
    public async Task CrearCandidatura_SinCuerpo_Devuelve400EnEspanol()
    {
        var respuesta = await PostJsonCrudoAsync("/api/candidaturas", "");

        var problema = await AssertBadRequestEnEspanolAsync(respuesta);
        Assert.Equal("Falta el cuerpo de la petición o no es un JSON válido.", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task CrearCandidatura_PuestoDemasiadoLargo_Devuelve400ConMensajeEnEspanol()
    {
        var respuesta = await _http.PostAsJsonAsync("/api/candidaturas", new { empresa = "Acme", puesto = new string('x', 301) });

        var problema = await AssertBadRequestEnEspanolAsync(respuesta);
        Assert.Equal("El puesto no puede tener más de 300 caracteres.",
            problema.GetProperty("errors").GetProperty("puesto")[0].GetString());
    }

    [Theory]
    [InlineData(", S.L.")]
    [InlineData("...")]
    [InlineData("¿?")]
    public async Task CrearCandidatura_EmpresaSinLetrasNiNumeros_Devuelve400(string empresa)
    {
        var respuesta = await _http.PostAsJsonAsync("/api/candidaturas", new { empresa, puesto = "Backend" });

        var problema = await AssertBadRequestEnEspanolAsync(respuesta);
        Assert.StartsWith("El nombre de la empresa no es válido", problema.GetProperty("detail").GetString());
        Assert.True(problema.GetProperty("errors").TryGetProperty("empresa", out _));
    }

    [Fact]
    public async Task CrearCandidatura_EnlaceQueNoEsWeb_Devuelve400ConElCampoEnlace()
    {
        var respuesta = await _http.PostAsJsonAsync("/api/candidaturas", new { empresa = "Acme", puesto = "Backend", enlace = "mailto:a@ejemplo.com" });

        var problema = await AssertBadRequestEnEspanolAsync(respuesta);
        Assert.Equal("El enlace tiene que ser una dirección web.", problema.GetProperty("errors").GetProperty("enlace")[0].GetString());
    }

    [Fact]
    public async Task CrearCandidatura_OchoAltasSimultaneasDeUnaEmpresaNueva_TodasCreadasYUnaSolaEmpresa()
    {
        for (var vuelta = 0; vuelta < 3; vuelta++)
        {
            var nombre = $"Empresa Concurrente {vuelta}";
            var respuestas = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                _http.PostAsJsonAsync("/api/candidaturas", new GuardarCandidaturaRequest(nombre, $"Puesto {i}"), FabricaApi.Json)));

            Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            var empresas = (await _http.GetFromJsonAsync<List<EmpresaDto>>($"/api/empresas?texto={Uri.EscapeDataString(nombre)}", FabricaApi.Json))!;
            Assert.Equal(8, Assert.Single(empresas).Candidaturas);
        }
    }

    [Fact]
    public async Task Listar_EstadoNumericoEnLaQuery_Devuelve400()
    {
        await AssertBadRequestEnEspanolAsync(await _http.GetAsync("/api/candidaturas?estado=99"));
    }

    [Fact]
    public async Task Listar_FechaDesdeNoValida_Devuelve400EnEspanol()
    {
        var problema = await AssertBadRequestEnEspanolAsync(await _http.GetAsync("/api/candidaturas?desde=ayer"));

        Assert.Equal("El valor «ayer» no es válido.", problema.GetProperty("detail").GetString());
        Assert.True(problema.GetProperty("errors").TryGetProperty("desde", out _));
    }

    [Fact]
    public async Task Listar_DesdeSoloFecha_FiltraDesdeLaMedianocheDeMadrid()
    {
        // 27/10/2026 en Madrid empieza el 26/10 a las 23:00 UTC.
        await _http.PostAsJsonAsync("/api/candidaturas",
            new GuardarCandidaturaRequest("Acme", "Del 26", FechaDeteccion: new DateTime(2026, 10, 26, 22, 30, 0, DateTimeKind.Utc)), FabricaApi.Json);
        await _http.PostAsJsonAsync("/api/candidaturas",
            new GuardarCandidaturaRequest("Acme", "Del 27", FechaDeteccion: new DateTime(2026, 10, 26, 23, 30, 0, DateTimeKind.Utc)), FabricaApi.Json);

        var lista = (await _http.GetFromJsonAsync<List<CandidaturaResumenDto>>("/api/candidaturas?desde=2026-10-27", FabricaApi.Json))!;

        Assert.Contains(lista, c => c.Puesto == "Del 27");
        Assert.DoesNotContain(lista, c => c.Puesto == "Del 26");
    }

    [Fact]
    public async Task ViolacionDeIndiceUnico_QueLlegaAlManejador_Devuelve409EnEspanol()
    {
        var manejador = ActivatorUtilities.CreateInstance<ManejadorExcepciones>(_fabrica.Services);
        var http = new DefaultHttpContext { RequestServices = _fabrica.Services };
        http.Response.Body = new MemoryStream();
        var error = new DbUpdateException("Error al guardar",
            new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: Candidaturas.ClaveImportacion'.", 19, 2067));

        Assert.True(await manejador.TryHandleAsync(http, error, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        var problema = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.StartsWith("Ya existe un registro con esos mismos datos", problema.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task CambiarEstado_TransicionProhibida_Devuelve409ConProblemDetailsEnEspanol()
    {
        var creada = await CrearAsync();
        await CambiarEstadoAsync(creada.Id, new { estado = "Entrevista" });

        var respuesta = await CambiarEstadoAsync(creada.Id, new { estado = "Detectada" });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var problema = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(409, problema.GetProperty("status").GetInt32());
        Assert.Equal("No se puede volver de «Entrevista» a «Detectada»: los estados solo avanzan.",
            problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task CambiarEstado_DescartarSinMotivo_Devuelve409()
    {
        var creada = await CrearAsync();

        var respuesta = await CambiarEstadoAsync(creada.Id, new { estado = "Descartada" });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
    }

    [Fact]
    public async Task ObtenerCandidatura_Inexistente_Devuelve404()
    {
        var respuesta = await _http.GetAsync("/api/candidaturas/12345");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task EditarBorrarYAnadirNota_CandidaturaInexistente_Devuelven404()
    {
        var editar = await _http.PutAsJsonAsync("/api/candidaturas/12345", new GuardarCandidaturaRequest("Acme", "Backend"), FabricaApi.Json);
        var borrar = await _http.DeleteAsync("/api/candidaturas/12345");
        var nota = await _http.PostAsJsonAsync("/api/candidaturas/12345/notas", new { texto = "Hola" });
        var estado = await CambiarEstadoAsync(12345, new { estado = "Enviada" });

        Assert.Equal(HttpStatusCode.NotFound, editar.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, borrar.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nota.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, estado.StatusCode);
        var problema = await borrar.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("No se ha encontrado.", problema.GetProperty("title").GetString());
    }

    [Fact]
    public async Task CrearCandidatura_SinPuesto_Devuelve400EnEspanol()
    {
        var respuesta = await _http.PostAsJsonAsync("/api/candidaturas", new { empresa = "Acme" });

        var problema = await AssertBadRequestEnEspanolAsync(respuesta);
        Assert.Equal("El puesto es obligatorio.", problema.GetProperty("detail").GetString());
        Assert.Equal("El puesto es obligatorio.", problema.GetProperty("errors").GetProperty("puesto")[0].GetString());
    }

    [Fact]
    public async Task NotasYEmpresas_AltaBorradoYAutocompletado_FuncionanPorHttp()
    {
        var creada = await CrearAsync(empresa: "Globex, S.A.");
        await CrearAsync(empresa: "GLOBEX", puesto: "Tester");

        var notaRespuesta = await _http.PostAsJsonAsync($"/api/candidaturas/{creada.Id}/notas", new { texto = "Hablar con RR. HH." });
        Assert.Equal(HttpStatusCode.Created, notaRespuesta.StatusCode);
        var nota = (await notaRespuesta.Content.ReadFromJsonAsync<NotaDto>(FabricaApi.Json))!;

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/notas/{nota.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync($"/api/notas/{nota.Id}")).StatusCode);

        var empresas = (await _http.GetFromJsonAsync<List<EmpresaDto>>("/api/empresas?texto=glob", FabricaApi.Json))!;
        var empresa = Assert.Single(empresas);
        Assert.Equal(2, empresa.Candidaturas);
    }
}
