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
        Assert.Equal(ImportacionService.MensajeExtraccionCambiada, conElViejo.GetProperty("detail").GetString());
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
        Assert.Equal(1, vista.GetProperty("resumen").GetProperty("delTextoLibre").GetInt32());
        Assert.Equal(["Línea 9: Empresa Texto Dos no dice la fecha (ejemplo inventado)."],
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

    [Fact]
    public async Task ExtraerPorPartesYPrevisualizar_DePrincipioAFinConElFalso_CombinaLoDeTodasConSuLineaYLaDudaDeLaPerdida()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        // Unos 40 KB de texto libre delante del registro de ejemplo: el primer mes no cabe en una parte y se reparte en dos.
        var registro = ManejadorExtraerRegistroTests.RegistroPorMeses(200, 50, 50, 50, 50) + await File.ReadAllTextAsync(RutaRegistro(fabrica));
        await File.WriteAllTextAsync(RutaRegistro(fabrica), registro);
        var trozos = PartidorTextoLibre.Partir(AnalizadorRegistro.TextoLibre(registro), DefinicionesTareas.ExtraerRegistro.Partes!.LargoParte);
        var n = trozos.Count;
        Assert.InRange(n, 3, 6);
        Assert.Equal("## Mes 1 de 2026", trozos[1].Lineas[0].Texto);

        // Cada parte menos la última lee la última entrada de las suyas; la segunda dice además la de la primera, que no es
        // suya, y la última falla.
        var elegidas = trozos.Take(n - 1)
            .Select(t => t.Lineas.Last(l => l.Texto.StartsWith("- Empresa Parte", StringComparison.Ordinal)))
            .Select(l => (Linea: l, Mes: int.Parse(l.Texto[15..l.Texto.IndexOf('-', 15)]),
                Numero: int.Parse(l.Texto[(l.Texto.IndexOf('-', 15) + 1)..l.Texto.IndexOf(' ', 15)])))
            .ToList();
        var respuestas = elegidas.Select((e, k) =>
        {
            var filas = new JsonArray(ManejadorExtraerRegistroTests.FilaDe(e.Mes, e.Numero, e.Linea.Numero));
            if (k == 1) filas.Add(ManejadorExtraerRegistroTests.FilaDe(elegidas[0].Mes, elegidas[0].Numero, elegidas[0].Linea.Numero));
            return new RespuestaFalsa(Salida: JsonSerializer.SerializeToElement(new JsonObject { ["candidaturas"] = filas, ["dudas"] = new JsonArray() }),
                CosteUsd: 0.10m);
        }).Append(new RespuestaFalsa(ModoFalso.Error, CosteUsd: 0.02m)).ToArray();
        var falso = (EjecutorClaudeFalso)fabrica.Services.GetRequiredService<IEjecutorClaude>();
        falso.ProgramarSecuencia(TareaAgente.ExtraerRegistro, respuestas);

        var id = (await LeerAsync(await PostAsync(http, "/api/agentes/extraer-registro"), HttpStatusCode.Accepted)).GetProperty("id").GetInt32();
        var ok = await Esperar.HastaAsync(() => http.GetFromJsonAsync<JsonElement>($"/api/agentes/ejecuciones/{id}"),
            e => e.GetProperty("estado").GetString() is "Ok" or "Error" or "Cancelada",
            e => $"La ejecución {id} sigue en {e.GetProperty("estado")}.");

        Assert.Equal("Ok", ok.GetProperty("estado").GetString());
        Assert.Equal(0.10m * (n - 1) + 0.02m, ok.GetProperty("costeUsd").GetDecimal());
        Assert.Contains("1 parte no se ha podido leer.", ok.GetProperty("resumen").GetString());
        Assert.Equal(n, falso.Peticiones.Count);
        await using (var db = await BdAsync(fabrica))
            Assert.Equal(1, await db.EjecucionesAgente.CountAsync());

        var vista = await LeerAsync(await PostAsync(http, UrlPrevisualizar(id)), HttpStatusCode.OK);
        var dudas = vista.GetProperty("dudas").EnumerateArray().Select(d => d.GetString()!).ToList();
        Assert.StartsWith($"No se ha podido leer la parte {n} de {n} (desde «", Assert.Single(dudas));
        Assert.EndsWith("): Claude ha devuelto un error.", dudas[0]);
        // Las de la tabla y una por parte leída: la que la segunda parte dice de la primera va a «Sin leer».
        Assert.Equal(n - 1, vista.GetProperty("resumen").GetProperty("delTextoLibre").GetInt32());
        var filas = vista.GetProperty("filas").EnumerateArray().ToList();
        Assert.Equal(8 + n - 1, filas.Count);
        Assert.All(elegidas, e => Assert.Single(filas, f => f.GetProperty("empresa").GetString() == $"Empresa Parte{e.Mes}-{e.Numero} S.L."));
        var sinLeer = Assert.Single(vista.GetProperty("sinLeer").EnumerateArray());
        Assert.Equal(0, sinLeer.GetProperty("linea").GetInt32());
        Assert.Equal($"La línea {elegidas[0].Linea.Numero} no está en la parte 2 de {n} del texto libre.", sinLeer.GetProperty("motivo").GetString());
        // La sección, de su título; la nota, la línea; ninguna con el aviso de empresa.
        var primera = filas.Single(f => f.GetProperty("empresa").GetString() == $"Empresa Parte{elegidas[0].Mes}-{elegidas[0].Numero} S.L.");
        Assert.Empty(primera.GetProperty("avisos").EnumerateArray());

        // Cada cita encaja con su línea del registro.
        await using var bd = await BdAsync(fabrica);
        var extraccion = ManejadorExtraerRegistro.LeerGuardado((await bd.EjecucionesAgente.SingleAsync()).ResultadoJson)!;
        var combinado = CombinadorLotes.Combinar(AnalizadorRegistro.Analizar(registro), registro, extraccion);
        Assert.All(elegidas, e => Assert.Equal(e.Linea.Numero,
            combinado.Filas.Single(f => f.Empresa == $"Empresa Parte{e.Mes}-{e.Numero} S.L.").Linea));
        Assert.Empty(combinado.Fuera);
        var fila = combinado.Filas.Single(f => f.Linea == elegidas[0].Linea.Numero);
        Assert.Equal($"Texto libre del registro: Mes {elegidas[0].Mes} de 2026", fila.Seccion);
        Assert.Equal(elegidas[0].Linea.Texto, fila.Nota);
    }
}
