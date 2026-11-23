using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// <c>/api/recordatorios</c>, <c>/api/candidaturas/{id}/recordatorios</c> y <c>/api/panel/hoy</c> por HTTP
/// contra la API real, con el reloj fijo el viernes 13/11/2026 a las 10:00 de Madrid. Cada test estrena API y BD.
/// </summary>
public sealed class RecordatoriosApiTests : IDisposable
{
    private const string Url = "/api/recordatorios";
    private const string PostLinkedIn = """{"tipo":"PublicacionLinkedIn","titulo":"Publicar en LinkedIn: post de Supabase","dia":"2026-11-03","hora":"09:00"}""";

    private readonly FabricaApi _fabrica = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc));
    private readonly HttpClient _http;

    public RecordatoriosApiTests()
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

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static StringContent Cuerpo(string tipo, string cuerpo)
    {
        var contenido = new StringContent(cuerpo, Encoding.UTF8);
        contenido.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(tipo);
        return contenido;
    }

    private async Task<RecordatorioDto> CrearAsync(string json = PostLinkedIn)
    {
        var respuesta = await _http.PostAsync(Url, Json(json));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<RecordatorioDto>(FabricaApi.Json))!;
    }

    private async Task<CandidaturaDetalleDto> CrearCandidaturaAsync()
    {
        var respuesta = await _http.PostAsJsonAsync("/api/candidaturas",
            new GuardarCandidaturaRequest("Consultora Ejemplo", "Desarrollador .NET"), FabricaApi.Json);
        return (await respuesta.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!;
    }

    private async Task<CandidaturaDetalleDto> CambiarEstadoAsync(int id, object peticion)
    {
        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        var respuesta = await _http.PostAsJsonAsync($"/api/candidaturas/{id}/estado", peticion, FabricaApi.Json);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!;
    }

    private Task<RecordatorioDto?> ObtenerAsync(int id) => _http.GetFromJsonAsync<RecordatorioDto>($"{Url}/{id}", FabricaApi.Json);

    private Task<PanelHoyDto?> PanelAsync() => _http.GetFromJsonAsync<PanelHoyDto>("/api/panel/hoy", FabricaApi.Json);

    private Task<CandidaturaDetalleDto?> FichaAsync(int id) =>
        _http.GetFromJsonAsync<CandidaturaDetalleDto>($"/api/candidaturas/{id}", FabricaApi.Json);

    private static async Task<JsonElement> ErroresAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("RadarEmpleo.", texto);
        Assert.DoesNotContain("System.", texto);
        Assert.DoesNotContain("field is required", texto);
        return JsonDocument.Parse(texto).RootElement.GetProperty("errors");
    }

    private static async Task<string?> DetalleConflictoAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();
    }

    // ---------------------------------------------------------------- crear y leer

    [Fact]
    public async Task Post_Suelto_Devuelve201ConLocationYLaFechaEnUtc()
    {
        var respuesta = await _http.PostAsync(Url, Json(PostLinkedIn));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetInt32();
        Assert.EndsWith($"/api/recordatorios/{id}", respuesta.Headers.Location!.ToString());
        Assert.Equal("2026-11-03T08:00:00Z", json.GetProperty("fechaHora").GetString());
        Assert.Equal("PublicacionLinkedIn", json.GetProperty("tipo").GetString());
        Assert.False(json.GetProperty("hecho").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("fechaHecho").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("candidaturaId").ValueKind);

        var leido = await _http.GetAsync(respuesta.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, leido.StatusCode);
        Assert.Equal("Publicar en LinkedIn: post de Supabase", (await leido.Content.ReadFromJsonAsync<RecordatorioDto>(FabricaApi.Json))!.Titulo);
    }

    [Fact]
    public async Task Post_DeUnaCandidatura_Devuelve201YSaleEnSuFicha()
    {
        var candidatura = await CrearCandidaturaAsync();

        var respuesta = await _http.PostAsync($"/api/candidaturas/{candidatura.Id}/recordatorios",
            Json("""{"tipo":"Entrevista","titulo":"Entrevista en Consultora Ejemplo","dia":"2026-11-11","hora":"15:00","notas":"Por Teams"}"""));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creado = (await respuesta.Content.ReadFromJsonAsync<RecordatorioDto>(FabricaApi.Json))!;
        Assert.EndsWith($"/api/recordatorios/{creado.Id}", respuesta.Headers.Location!.ToString());
        Assert.Equal(candidatura.Id, creado.CandidaturaId);
        Assert.Equal(new DateTime(2026, 11, 11, 14, 0, 0, DateTimeKind.Utc), creado.FechaHora);
        Assert.Equal("Por Teams", creado.Notas);
        Assert.Equal(creado, Assert.Single((await FichaAsync(candidatura.Id))!.Recordatorios));
    }

    /// <summary>Cerrada (y el resto de la regla) se prueba en <see cref="RecordatoriosServiceTests"/>.</summary>
    [Fact]
    public async Task Post_DeUnaCandidaturaDescartada_Devuelve409YNoLoGuarda()
    {
        var candidatura = await CrearCandidaturaAsync();
        await CambiarEstadoAsync(candidatura.Id, new { estado = "Descartada", motivoDescarte = "Zona" });

        var respuesta = await _http.PostAsync($"/api/candidaturas/{candidatura.Id}/recordatorios",
            Json("""{"tipo":"Plazo","titulo":"Plazo de Consultora Ejemplo","dia":"2026-11-17"}"""));

        Assert.Equal(RecordatoriosService.MensajeCandidaturaFinal, await DetalleConflictoAsync(respuesta));
        Assert.Empty((await FichaAsync(candidatura.Id))!.Recordatorios);
    }

    /// <summary>
    /// La <see cref="ValidacionException"/> del servicio llega como 400 en su campo y con su mensaje. Al editar y al
    /// posponer (que la pasa a las tres) se prueba en <see cref="RecordatoriosServiceTests"/>.
    /// </summary>
    [Fact]
    public async Task Post_HoraQueNoExistePorElCambioDeHora_Devuelve400EnLaHora()
    {
        var errores = await ErroresAsync(await _http.PostAsync(Url,
            Json("""{"tipo":"Otro","titulo":"Renovar la demanda","dia":"2027-03-28","hora":"02:30"}""")));

        Assert.Equal("Esa hora no existe ese día: el reloj se adelanta de 02:00 a 03:00.", errores.GetProperty("hora")[0].GetString());
        Assert.Empty((await PanelAsync())!.ParaHoy);
    }

    // ---------------------------------------------------------------- 400

    [Fact]
    public async Task Post_CuerpoVacio_Devuelve400ConLosTresCamposEnEspanol()
    {
        var errores = await ErroresAsync(await _http.PostAsync(Url, Json("{}")));

        Assert.Equal("Elige qué tipo de recordatorio es.", errores.GetProperty("tipo")[0].GetString());
        Assert.Equal("El título es obligatorio.", errores.GetProperty("titulo")[0].GetString());
        Assert.Equal("El día es obligatorio.", errores.GetProperty("dia")[0].GetString());
        Assert.Empty((await PanelAsync())!.ParaHoy);
    }

    /// <summary>
    /// Lo que rechaza la API antes de llegar al servicio: el JSON (enum como número o desconocido, día con otro
    /// formato) y las anotaciones (título en blanco). La hora mala, la entrevista sin hora y el día fuera de rango
    /// son reglas del servicio: <see cref="RecordatoriosServiceTests"/> y <see cref="ReglasRecordatorioTests"/>.
    /// </summary>
    [Theory]
    [InlineData("""{"tipo":3,"titulo":"Algo","dia":"2026-11-17"}""", "tipo")]
    [InlineData("""{"tipo":"Cumple","titulo":"Algo","dia":"2026-11-17"}""", "tipo")]
    [InlineData("""{"tipo":"Otro","titulo":"Algo","dia":"17/11/2026"}""", "dia")]
    [InlineData("""{"tipo":"Otro","titulo":"   ","dia":"2026-11-17"}""", "titulo")]
    public async Task Post_ConUnDatoMalo_Devuelve400EnSuCampo(string json, string campo)
    {
        var errores = await ErroresAsync(await _http.PostAsync(Url, Json(json)));

        Assert.True(errores.TryGetProperty(campo, out var mensajes), $"Falta el error de «{campo}».");
        Assert.NotEmpty(mensajes[0].GetString()!);
    }

    [Fact]
    public async Task Post_TituloONotasDemasiadoLargos_Devuelve400()
    {
        var titulo = new string('a', 201);
        var notas = new string('a', 2001);

        var porTitulo = await ErroresAsync(await _http.PostAsync(Url, Json($$"""{"tipo":"Otro","titulo":"{{titulo}}","dia":"2026-11-17"}""")));
        var porNotas = await ErroresAsync(await _http.PostAsync(Url, Json($$"""{"tipo":"Otro","titulo":"Algo","dia":"2026-11-17","notas":"{{notas}}"}""")));

        Assert.Equal("El título no puede tener más de 200 caracteres.", porTitulo.GetProperty("titulo")[0].GetString());
        Assert.Equal("Las notas no pueden tener más de 2000 caracteres.", porNotas.GetProperty("notas")[0].GetString());
    }

    [Fact]
    public async Task HechoYPosponer_CuerpoVacio_Devuelve400ConSuCampo()
    {
        var creado = await CrearAsync();

        var hecho = await ErroresAsync(await _http.PostAsync($"{Url}/{creado.Id}/hecho", Json("{}")));
        var posponer = await ErroresAsync(await _http.PostAsync($"{Url}/{creado.Id}/posponer", Json("{}")));

        Assert.Equal("Falta decir si está hecho.", hecho.GetProperty("hecho")[0].GetString());
        Assert.Equal("Falta el día.", posponer.GetProperty("dia")[0].GetString());
        Assert.Equal(creado, await ObtenerAsync(creado.Id));
    }

    // ---------------------------------------------------------------- 415

    [Theory]
    [InlineData("application/x-www-form-urlencoded", "tipo=Otro&titulo=Colado&dia=2026-11-17")]
    [InlineData("text/plain", """{"tipo":"Otro","titulo":"Colado","dia":"2026-11-17"}""")]
    public async Task SinJson_Devuelve415EnTodoLoQueEscribeYNoCambiaNada(string tipo, string cuerpo)
    {
        var candidatura = await CrearCandidaturaAsync();
        var creado = await CrearAsync();

        var respuestas = new[]
        {
            await _http.PostAsync(Url, Cuerpo(tipo, cuerpo)),
            await _http.PostAsync($"/api/candidaturas/{candidatura.Id}/recordatorios", Cuerpo(tipo, cuerpo)),
            await _http.PutAsync($"{Url}/{creado.Id}", Cuerpo(tipo, cuerpo)),
            await _http.PostAsync($"{Url}/{creado.Id}/hecho", Cuerpo(tipo, tipo == "text/plain" ? """{"hecho":true}""" : "hecho=true")),
            await _http.PostAsync($"{Url}/{creado.Id}/posponer", Cuerpo(tipo, tipo == "text/plain" ? """{"dia":"2026-11-20"}""" : "dia=2026-11-20")),
        };

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode));
        Assert.Equal(creado, await ObtenerAsync(creado.Id));
        Assert.Empty((await FichaAsync(candidatura.Id))!.Recordatorios);
        var panel = (await PanelAsync())!;
        Assert.Equal(creado.Id, Assert.Single(panel.ParaHoy).Id);
        Assert.Empty(panel.Proximos);
        Assert.Empty(panel.HechosHoy);
    }

    // ---------------------------------------------------------------- 404

    [Fact]
    public async Task IdQueNoExiste_Devuelve404EnTodo()
    {
        const string valido = """{"tipo":"Otro","titulo":"Algo","dia":"2026-11-17"}""";

        var respuestas = new[]
        {
            await _http.GetAsync($"{Url}/999"),
            await _http.PutAsync($"{Url}/999", Json(valido)),
            await _http.PostAsync($"{Url}/999/hecho", Json("""{"hecho":true}""")),
            await _http.PostAsync($"{Url}/999/posponer", Json("""{"dia":"2026-11-20"}""")),
            await _http.DeleteAsync($"{Url}/999"),
            await _http.PostAsync("/api/candidaturas/999/recordatorios", Json(valido)),
        };

        foreach (var respuesta in respuestas)
        {
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            // ProblemDetails, como el resto de 404 de la API.
            var problema = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(404, problema.GetProperty("status").GetInt32());
            Assert.Equal("No se ha encontrado.", problema.GetProperty("title").GetString());
        }
    }

    // ---------------------------------------------------------------- editar, hecho, posponer y borrar

    [Fact]
    public async Task Put_CambiaLosDatosYNoSiEstaHecho()
    {
        var creado = await CrearAsync();
        await _http.PostAsync($"{Url}/{creado.Id}/hecho", Json("""{"hecho":true}"""));

        var respuesta = await _http.PutAsync($"{Url}/{creado.Id}",
            Json("""{"tipo":"Otro","titulo":"Renovar la demanda","dia":"2026-11-18","hora":"12:30","notas":"Por la web"}"""));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var editado = (await respuesta.Content.ReadFromJsonAsync<RecordatorioDto>(FabricaApi.Json))!;
        Assert.Equal(TipoRecordatorio.Otro, editado.Tipo);
        Assert.Equal("Renovar la demanda", editado.Titulo);
        Assert.Equal(new DateTime(2026, 11, 18, 11, 30, 0, DateTimeKind.Utc), editado.FechaHora);
        Assert.Equal("Por la web", editado.Notas);
        Assert.True(editado.Hecho);
    }

    [Fact]
    public async Task Hecho_PasaAHechosHoy_YRepetirloNoCambiaLaFecha()
    {
        var creado = await CrearAsync();

        var primera = await _http.PostAsync($"{Url}/{creado.Id}/hecho", Json("""{"hecho":true}"""));
        _reloj.Avanzar(TimeSpan.FromMinutes(3));
        var segunda = await _http.PostAsync($"{Url}/{creado.Id}/hecho", Json("""{"hecho":true}"""));

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var json = await segunda.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-11-13T09:00:00Z", json.GetProperty("fechaHecho").GetString());
        var panel = (await PanelAsync())!;
        Assert.Empty(panel.ParaHoy);
        Assert.Equal(creado.Id, Assert.Single(panel.HechosHoy).Id);
        Assert.Null(panel.HechosHoy[0].CerradoAlPasarA);

        var pendiente = await _http.PostAsync($"{Url}/{creado.Id}/hecho", Json("""{"hecho":false}"""));
        Assert.Equal(HttpStatusCode.OK, pendiente.StatusCode);
        panel = (await PanelAsync())!;
        Assert.Equal(creado.Id, Assert.Single(panel.ParaHoy).Id);
        Assert.Empty(panel.HechosHoy);
    }

    [Fact]
    public async Task Posponer_LoMueveConLaMismaHora_YAlgoHechoDa409()
    {
        var creado = await CrearAsync();

        var respuesta = await _http.PostAsync($"{Url}/{creado.Id}/posponer", Json("""{"dia":"2026-11-14"}"""));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("2026-11-14T08:00:00Z", (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fechaHora").GetString());
        Assert.Equal(creado.Id, Assert.Single((await PanelAsync())!.Proximos).Id);

        await _http.PostAsync($"{Url}/{creado.Id}/hecho", Json("""{"hecho":true}"""));
        Assert.Equal("Ya está hecho. Márcalo como pendiente para moverlo.",
            await DetalleConflictoAsync(await _http.PostAsync($"{Url}/{creado.Id}/posponer", Json("""{"dia":"2026-11-20"}"""))));
        Assert.Equal(new DateTime(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc), (await ObtenerAsync(creado.Id))!.FechaHora);
    }

    [Fact]
    public async Task Delete_Devuelve204YLuego404()
    {
        var creado = await CrearAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"{Url}/{creado.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync($"{Url}/{creado.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"{Url}/{creado.Id}")).StatusCode);
    }

    /// <summary>
    /// Guardado que, armado, manda antes otra petición: la carrera entre dos pestañas, sin depender de
    /// la suerte. Se arma después de crear el recordatorio, para que no salte con ese guardado.
    /// </summary>
    private sealed class OtraPeticionEntreMedias : SaveChangesInterceptor
    {
        private Func<Task>? _pendiente;

        public void Armar(Func<Task> accion) => _pendiente = accion;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _pendiente, null) is { } pendiente) await pendiente();
            return result;
        }
    }

    [Theory]
    [InlineData("""{"hecho":true}""", "hecho", """{"hecho":true}""", "hecho", HttpStatusCode.OK)]
    [InlineData("""{"hecho":true}""", "hecho", """{"dia":"2026-11-20"}""", "posponer", HttpStatusCode.Conflict)]
    [InlineData("""{"dia":"2026-11-20"}""", "posponer", """{"hecho":true}""", "hecho", HttpStatusCode.Conflict)]
    public async Task DosPeticionesALaVez_NuncaDan500_YDosHechoSeQuedanEnHecho(
        string cuerpo, string accion, string cuerpoOtra, string accionOtra, HttpStatusCode esperado)
    {
        var entreMedias = new OtraPeticionEntreMedias();
        using var http = _fabrica.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IClock>();
            s.AddSingleton<IClock>(_reloj);
            s.RemoveAll<IDbContextFactory<AppDbContext>>();
            s.AddSingleton<IDbContextFactory<AppDbContext>>(sp => new TestDbContextFactory(
                new DbContextOptionsBuilder<AppDbContext>(sp.GetRequiredService<DbContextOptions<AppDbContext>>())
                    .AddInterceptors(entreMedias).Options));
        })).CreateClient();
        var creado = await http.PostAsync(Url, Json(PostLinkedIn));
        var id = (await creado.Content.ReadFromJsonAsync<RecordatorioDto>(FabricaApi.Json))!.Id;
        HttpResponseMessage? otra = null;
        entreMedias.Armar(async () => otra = await http.PostAsync($"{Url}/{id}/{accionOtra}", Json(cuerpoOtra)));

        var respuesta = await http.PostAsync($"{Url}/{id}/{accion}", Json(cuerpo));

        Assert.Equal(HttpStatusCode.OK, otra!.StatusCode);
        Assert.Equal(esperado, respuesta.StatusCode);
        if (esperado == HttpStatusCode.OK)
        {
            var devuelto = (await respuesta.Content.ReadFromJsonAsync<RecordatorioDto>(FabricaApi.Json))!;
            Assert.True(devuelto.Hecho);
            Assert.Equal((await otra.Content.ReadFromJsonAsync<RecordatorioDto>(FabricaApi.Json))!.FechaHecho, devuelto.FechaHecho);
        }
        else
        {
            Assert.Equal(RecordatoriosService.MensajeCambiadoEnOtraPestana, await DetalleConflictoAsync(respuesta));
        }
    }

    // ---------------------------------------------------------------- panel

    [Fact]
    public async Task Panel_FormaDelJson()
    {
        await CrearAsync();
        await CrearAsync("""{"tipo":"PublicacionLinkedIn","titulo":"Publicar en LinkedIn: post de Angular","dia":"2026-11-17"}""");

        var json = await _http.GetFromJsonAsync<JsonElement>("/api/panel/hoy");

        Assert.Equal("2026-11-13", json.GetProperty("hoy").GetString());
        var paraHoy = Assert.Single(json.GetProperty("paraHoy").EnumerateArray());
        Assert.Equal("2026-11-03T08:00:00Z", paraHoy.GetProperty("fechaHora").GetString());
        Assert.Equal(JsonValueKind.Null, paraHoy.GetProperty("candidatura").ValueKind);
        Assert.Equal("2026-11-17T08:00:00Z", Assert.Single(json.GetProperty("proximos").EnumerateArray()).GetProperty("fechaHora").GetString());
        Assert.Equal(0, json.GetProperty("masAdelante").GetArrayLength());
        Assert.Equal(0, json.GetProperty("hechosHoy").GetArrayLength());
        Assert.Equal(0, json.GetProperty("sinRespuesta").GetArrayLength());
        Assert.Equal(0, json.GetProperty("sinRespuestaAntiguas").GetInt32());

        var resumen = json.GetProperty("resumen");
        Assert.Equal(0, resumen.GetProperty("candidaturasActivas").GetInt32());
        Assert.Equal(0, resumen.GetProperty("entrevistasSemana").GetInt32());
        var respuesta = resumen.GetProperty("respuesta");
        Assert.Equal(0, respuesta.GetProperty("enviadas").GetInt32());
        Assert.Equal(0, respuesta.GetProperty("conRespuesta").GetInt32());
        Assert.Equal(JsonValueKind.Null, respuesta.GetProperty("porcentaje").ValueKind);
        var semana = resumen.GetProperty("semana").EnumerateArray().ToList();
        Assert.Equal(7, semana.Count);
        Assert.Equal("2026-11-09", semana[0].GetProperty("dia").GetString());
        Assert.Equal("2026-11-15", semana[6].GetProperty("dia").GetString());
        // El del 3/11 es de la semana pasada y el del 17/11, de la siguiente
        Assert.All(semana, dia => Assert.Equal(0, dia.GetProperty("recordatorios").GetInt32()));
        Assert.Equal("2026-11-17", resumen.GetProperty("siguienteDia").GetString());
    }

    [Fact]
    public async Task PanelHoy_LaCandidaturaDeUnRecordatorioLlevaElEnlaceDeLaOferta()
    {
        var respuesta = await _http.PostAsJsonAsync("/api/candidaturas",
            new { empresa = "Consultora Ejemplo", puesto = "Desarrollador .NET", enlace = "https://ejemplo.test/ofertas/1" });
        var id = (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await _http.PostAsync($"/api/candidaturas/{id}/recordatorios",
            Json("""{"tipo":"Plazo","titulo":"Plazo de la prueba técnica","dia":"2026-11-13"}"""));

        var json = await _http.GetFromJsonAsync<JsonElement>("/api/panel/hoy");

        var candidatura = Assert.Single(json.GetProperty("paraHoy").EnumerateArray()).GetProperty("candidatura");
        Assert.Equal("https://ejemplo.test/ofertas/1", candidatura.GetProperty("enlace").GetString());
        Assert.Equal("Desarrollador .NET", candidatura.GetProperty("puesto").GetString());
    }

    [Fact]
    public async Task DePrincipioAFin_ElSeguimientoSaleEnHoy_EntrevistaLoCierraYDeshacerLoDevuelve()
    {
        var candidatura = await CrearCandidaturaAsync();
        var enviada = await CambiarEstadoAsync(candidatura.Id, new { estado = "Enviada" });
        var seguimiento = Assert.Single(enviada.Recordatorios);
        Assert.Equal(new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc), seguimiento.FechaHora);
        var enProximos = Assert.Single((await PanelAsync())!.Proximos);
        Assert.Equal("Consultora Ejemplo", enProximos.Candidatura!.Empresa);
        Assert.Equal(EstadoCandidatura.Enviada, enProximos.Candidatura.Estado);

        _reloj.Avanzar(TimeSpan.FromDays(7));
        var panel = (await PanelAsync())!;
        Assert.Equal(new DateOnly(2026, 11, 20), panel.Hoy);
        Assert.Equal(seguimiento.Id, Assert.Single(panel.ParaHoy).Id);
        Assert.Empty(panel.SinRespuesta); // tiene el seguimiento pendiente

        var entrevista = await CambiarEstadoAsync(candidatura.Id, new { estado = "Entrevista" });
        var cerrado = Assert.Single(entrevista.Recordatorios);
        Assert.True(cerrado.Hecho);
        Assert.Equal(_reloj.AhoraUtc, cerrado.FechaHecho);
        panel = (await PanelAsync())!;
        Assert.Empty(panel.ParaHoy);
        Assert.Equal(seguimiento.Id, Assert.Single(panel.HechosHoy).Id);
        Assert.Equal(EstadoCandidatura.Entrevista, panel.HechosHoy[0].CerradoAlPasarA);

        var deshacer = await _http.PostAsync($"/api/candidaturas/{candidatura.Id}/estado/deshacer",
            Json($$"""{"cambioId":{{entrevista.Historial[^1].Id}}}"""));
        Assert.Equal(HttpStatusCode.OK, deshacer.StatusCode);
        panel = (await PanelAsync())!;
        var devuelto = Assert.Single(panel.ParaHoy);
        Assert.Equal(seguimiento.Id, devuelto.Id);
        Assert.False(devuelto.Hecho);
        Assert.Null(devuelto.FechaHecho);
        Assert.Empty(panel.HechosHoy);
    }

    [Fact]
    public async Task Descartar_ElPlazoSaleDelPanelYSigueEnLaFicha_YReabrirLoDevuelve()
    {
        var candidatura = await CrearCandidaturaAsync();
        var plazo = await _http.PostAsync($"/api/candidaturas/{candidatura.Id}/recordatorios",
            Json("""{"tipo":"Plazo","titulo":"Plazo de Consultora Ejemplo","dia":"2026-11-13"}"""));
        Assert.Equal(HttpStatusCode.Created, plazo.StatusCode);
        Assert.Single((await PanelAsync())!.ParaHoy);

        await CambiarEstadoAsync(candidatura.Id, new { estado = "Descartada", motivoDescarte = "Zona" });
        Assert.Empty((await PanelAsync())!.ParaHoy);
        Assert.False(Assert.Single((await FichaAsync(candidatura.Id))!.Recordatorios).Hecho);

        await CambiarEstadoAsync(candidatura.Id, new { estado = "Detectada" });
        Assert.Single((await PanelAsync())!.ParaHoy);
    }

    [Theory]
    [InlineData("/api/panel/hoy")]
    [InlineData("/api/recordatorios/1")]
    public async Task HostQueNoEsLocal_Devuelve400ContraElDnsRebinding(string url)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
        peticion.Headers.Host = "evil.example";

        Assert.Equal(HttpStatusCode.BadRequest, (await _http.SendAsync(peticion)).StatusCode);
    }
}
