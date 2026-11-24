using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadarEmpleo.Api.Seguridad;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Agentes;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Importacion;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// Importación con una extracción de Claude por HTTP: <c>POST /api/importacion/extraccion/{id}/previsualizar</c> (con
/// <c>[SoloPcConAgentes]</c>) y aplicar con <see cref="OrigenLote.RegistroConClaude"/>. Las ejecuciones se siembran en la BD
/// o, de principio a fin, salen de la cola con el ejecutor falso y <c>extraer-registro.json</c>. Nunca se lanza Claude.
/// </summary>
public sealed class ImportacionExtraccionApiTests
{
    private const string UrlAplicar = "/api/importacion/lote/aplicar";
    private static readonly DateTime Ahora = new(2026, 11, 20, 11, 0, 0, DateTimeKind.Utc);

    private static FabricaApi Fabrica(CanalPeticion canal = CanalPeticion.Local, bool agentes = true) => new(canal: canal, agentes: agentes);

    private static string UrlPrevisualizar(int id) => $"/api/importacion/extraccion/{id}/previsualizar";

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string url, string json = "{}", string tipo = "application/json") =>
        http.PostAsync(url, new StringContent(json, Encoding.UTF8, tipo));

    private static string RutaRegistro(FabricaApi fabrica) => Path.Combine(fabrica.CarpetaCurriculum, RutasCurriculum.ArchivoRegistro);

    private static string HashRegistro(FabricaApi fabrica) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(RutaRegistro(fabrica))));

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage respuesta, HttpStatusCode codigo)
    {
        Assert.Equal(codigo, respuesta.StatusCode);
        return await respuesta.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<AppDbContext> BdAsync(FabricaApi fabrica) =>
        fabrica.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    /// <summary>Una ExtraerRegistro en Error, empezada con el hash del registro de ahora.</summary>
    private static async Task<int> SembrarEnErrorAsync(FabricaApi fabrica)
    {
        var ejecucion = EjecucionAgente.Nueva(TareaAgente.ExtraerRegistro, "{}", 1m, 300, null, Ahora);
        ejecucion.Empezar(Ahora, HashRegistro(fabrica), null);
        ejecucion.Fallar(Ahora, "Falló.");
        await using var db = await BdAsync(fabrica);
        db.EjecucionesAgente.Add(ejecucion);
        await db.SaveChangesAsync();
        return ejecucion.Id;
    }

    /// <summary>Extrae de verdad por la API con el ejecutor falso y la salida de <c>extraer-registro.json</c>, hasta Ok.</summary>
    private static async Task<int> ExtraerAsync(FabricaApi fabrica, HttpClient http)
    {
        using var ejemplo = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(FabricaApi.CarpetaDatosEjemplo, "agentes", "extraer-registro.json")));
        ((EjecutorClaudeFalso)fabrica.Services.GetRequiredService<IEjecutorClaude>()).Programar(TareaAgente.ExtraerRegistro,
            new RespuestaFalsa(Salida: ejemplo.RootElement.GetProperty("salida").Clone()));
        var id = (await LeerAsync(await PostAsync(http, "/api/agentes/extraer-registro"), HttpStatusCode.Accepted)).GetProperty("id").GetInt32();
        await Esperar.HastaAsync(() => http.GetFromJsonAsync<JsonElement>($"/api/agentes/ejecuciones/{id}"),
            e => e.GetProperty("estado").GetString() == "Ok",
            e => $"La ejecución {id} sigue en {e.GetProperty("estado")}.");
        return id;
    }

    private static List<string> ClavesAccionables(JsonElement vista) => vista.GetProperty("filas").EnumerateArray()
        .Where(f => f.GetProperty("resultado").GetString() is "Nueva" or "AvanzaEstado")
        .Select(f => f.GetProperty("clave").GetString()!)
        .ToList();

    private static Task<HttpResponseMessage> AplicarAsync(HttpClient http, int? ejecucionId, string hash, IEnumerable<string> claves) =>
        PostAsync(http, UrlAplicar, new JsonObject
        {
            ["origen"] = "RegistroConClaude",
            ["ejecucionId"] = ejecucionId,
            ["hashRegistro"] = hash,
            ["claves"] = new JsonArray(claves.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray())
        }.ToJsonString());

    [Fact]
    public async Task PrevisualizarExtraccion_PorRemoto_Da403YNoLeeNada()
    {
        using var fabrica = Fabrica(CanalPeticion.Remoto);
        using var http = fabrica.CreateClient();
        // Sin el registro, si llegara a leerlo daría 409.
        File.Delete(RutaRegistro(fabrica));

        var problema = await LeerAsync(await PostAsync(http, UrlPrevisualizar(1)), HttpStatusCode.Forbidden);

        Assert.Equal("Los agentes solo se pueden lanzar desde el PC.", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PrevisualizarExtraccion_PorRemotoConTextPlain_Da403AntesQue415()
    {
        using var fabrica = Fabrica(CanalPeticion.Remoto);
        using var http = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(http, UrlPrevisualizar(1), tipo: "text/plain")).StatusCode);
    }

    [Fact]
    public async Task PrevisualizarExtraccion_AgentesApagados_Da403()
    {
        using var fabrica = Fabrica(agentes: false);
        using var http = fabrica.CreateClient();

        var problema = await LeerAsync(await PostAsync(http, UrlPrevisualizar(1)), HttpStatusCode.Forbidden);

        Assert.Equal("Los agentes están apagados.", problema.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task PrevisualizarExtraccion_ConFormularioOTextPlain_Da415(string tipo)
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await PostAsync(http, UrlPrevisualizar(1), tipo: tipo)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await http.PostAsync(UrlPrevisualizar(1), null)).StatusCode);
    }

    [Fact]
    public async Task PrevisualizarExtraccion_NoExiste_Da404()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(http, UrlPrevisualizar(999))).StatusCode);
    }

    [Fact]
    public async Task Aplicar_RegistroConClaudeSinEjecucionId_Devuelve400()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();

        var problema = await LeerAsync(await AplicarAsync(http, null, HashRegistro(fabrica), []), HttpStatusCode.BadRequest);

        Assert.True(problema.GetProperty("errors").TryGetProperty("ejecucionId", out _));
    }

    [Fact]
    public async Task Aplicar_RegistroConClaudePorRemoto_Da403YNoCreaNada()
    {
        using var fabrica = Fabrica(CanalPeticion.Remoto);
        using var http = fabrica.CreateClient();
        var id = await SembrarEnErrorAsync(fabrica);
        // Sin el registro, si llegara a leerlo daría 409.
        var hash = HashRegistro(fabrica);
        File.Delete(RutaRegistro(fabrica));

        var problema = await LeerAsync(await AplicarAsync(http, id, hash, []), HttpStatusCode.Forbidden);

        Assert.Equal(DisponibilidadAgentes.MensajeSoloPc, problema.GetProperty("detail").GetString());
        await using var db = await BdAsync(fabrica);
        Assert.Empty(db.Candidaturas);
    }

    [Fact]
    public async Task Aplicar_RegistroConClaudeConLosAgentesApagadosOTraidoDeOtroSitio_Da403()
    {
        using var apagados = Fabrica(agentes: false);
        using var http = apagados.CreateClient();
        var problema = await LeerAsync(await AplicarAsync(http, 1, HashRegistro(apagados), []), HttpStatusCode.Forbidden);
        Assert.Equal(DisponibilidadAgentes.MensajeApagados, problema.GetProperty("detail").GetString());

        using var fabrica = Fabrica();
        using var otroSitio = fabrica.CreateClient();
        otroSitio.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        var deFuera = await LeerAsync(await AplicarAsync(otroSitio, 1, HashRegistro(fabrica), []), HttpStatusCode.Forbidden);
        Assert.Equal(DisponibilidadAgentes.MensajeOtroSitio, deFuera.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Aplicar_TablaPorRemoto_SigueDando200()
    {
        using var fabrica = Fabrica(CanalPeticion.Remoto, agentes: false);
        using var http = fabrica.CreateClient();
        var vista = await LeerAsync(await http.PostAsync("/api/importacion/tabla/previsualizar", null), HttpStatusCode.OK);

        var resultado = await LeerAsync(await PostAsync(http, UrlAplicar, new JsonObject
        {
            ["origen"] = "Tabla",
            ["hashRegistro"] = vista.GetProperty("hashRegistro").GetString(),
            ["claves"] = new JsonArray(ClavesAccionables(vista).Select(c => (JsonNode)JsonValue.Create(c)!).ToArray())
        }.ToJsonString()), HttpStatusCode.OK);

        Assert.Equal(8, resultado.GetProperty("creadas").GetInt32());
    }

    [Fact]
    public async Task Aplicar_RegistroConClaudeConEjecucionEnError_Devuelve409()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        var id = await SembrarEnErrorAsync(fabrica);

        var problema = await LeerAsync(await AplicarAsync(http, id, HashRegistro(fabrica), []), HttpStatusCode.Conflict);

        Assert.Equal(ImportacionService.MensajeNoEsExtraccion, problema.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(http, UrlPrevisualizar(id))).StatusCode);
    }

    [Fact]
    public async Task Aplicar_RegistroConClaudeConElRegistroCambiado_Devuelve409ConElMensajeDeLaExtraccion()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        var id = await ExtraerAsync(fabrica, http);
        var vista = await LeerAsync(await PostAsync(http, UrlPrevisualizar(id)), HttpStatusCode.OK);
        await File.AppendAllTextAsync(RutaRegistro(fabrica), "\n- Una línea más.\n");

        var conElNuevo = await LeerAsync(await AplicarAsync(http, id, HashRegistro(fabrica), ClavesAccionables(vista)), HttpStatusCode.Conflict);
        var conElViejo = await LeerAsync(
            await AplicarAsync(http, id, vista.GetProperty("hashRegistro").GetString()!, ClavesAccionables(vista)), HttpStatusCode.Conflict);
        var otraVista = await LeerAsync(await PostAsync(http, UrlPrevisualizar(id)), HttpStatusCode.Conflict);

        Assert.Equal(ImportacionService.MensajeExtraccionCambiada, conElNuevo.GetProperty("detail").GetString());
        Assert.Equal(ImportacionService.MensajeRegistroCambiado, conElViejo.GetProperty("detail").GetString());
        Assert.Equal(ImportacionService.MensajeExtraccionCambiada, otraVista.GetProperty("detail").GetString());
        await using var db = await BdAsync(fabrica);
        Assert.Empty(db.Candidaturas);
    }

    [Fact]
    public async Task ExtraerPrevisualizarYAplicar_DePrincipioAFinConElFalso_CreaTablaMasTextoLibreYRepetirNoDuplica()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        var id = await ExtraerAsync(fabrica, http);

        var vista = await LeerAsync(await PostAsync(http, UrlPrevisualizar(id)), HttpStatusCode.OK);

        Assert.Equal("RegistroConClaude", vista.GetProperty("origen").GetString());
        Assert.Equal(id, vista.GetProperty("ejecucionId").GetInt32());
        Assert.Equal(HashRegistro(fabrica), vista.GetProperty("hashRegistro").GetString());
        Assert.Equal(0, vista.GetProperty("resumen").GetProperty("enviosEnTexto").GetInt32());
        Assert.Equal(["La línea de Empresa Texto Dos no dice la fecha (ejemplo inventado)."],
            vista.GetProperty("dudas").EnumerateArray().Select(d => d.GetString()));
        var filas = vista.GetProperty("filas").EnumerateArray().ToList();
        Assert.Equal(9, filas.Count);
        Assert.Empty(Assert.Single(filas, f => f.GetProperty("empresa").GetString() == "Empresa Texto Uno S.L.").GetProperty("avisos").EnumerateArray());

        var primera = await LeerAsync(await AplicarAsync(http, id, HashRegistro(fabrica), ClavesAccionables(vista)), HttpStatusCode.OK);
        var otraVista = await LeerAsync(await PostAsync(http, UrlPrevisualizar(id)), HttpStatusCode.OK);
        var segunda = await LeerAsync(await AplicarAsync(http, id, HashRegistro(fabrica), ClavesAccionables(otraVista)), HttpStatusCode.OK);

        Assert.Equal(9, primera.GetProperty("creadas").GetInt32());
        Assert.Equal((0, 9), (segunda.GetProperty("creadas").GetInt32(), segunda.GetProperty("sinCambios").GetInt32()));
        await using var db = await BdAsync(fabrica);
        Assert.Equal(9, await db.Candidaturas.CountAsync());
        var extraida = await db.Candidaturas.SingleAsync(c => c.Empresa.Nombre == "Empresa Texto Uno S.L.");
        Assert.Equal("Texto libre del registro: Notas sueltas de septiembre", extraida.SeccionOrigen);
        Assert.Equal(Origen.Registro, extraida.Origen);
    }
}
