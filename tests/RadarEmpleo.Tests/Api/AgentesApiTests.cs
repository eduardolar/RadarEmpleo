using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RadarEmpleo.Api.Configuracion;
using RadarEmpleo.Api.Seguridad;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Agentes;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// <c>/api/agentes</c> con el ejecutor y el descargador falsos, el <see cref="ManejadorDePrueba"/> en ExtraerRegistro y el
/// manejador real de AnalizarEncaje: el filtro <c>[SoloPcConAgentes]</c>, analizar, la lista, el detalle, cancelar,
/// reintentar y el estado con el gasto de hoy.
/// </summary>
[Collection(nameof(ColeccionManejadorDePrueba))]
public sealed class AgentesApiTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 11, 0, 0, DateTimeKind.Utc);

    /// <param name="manejadorDePrueba">ExtraerRegistro con el <see cref="ManejadorDePrueba"/> en vez del real.</param>
    private static FabricaApi Fabrica(CanalPeticion canal = CanalPeticion.Local, bool agentes = true, string modo = "Local",
        RelojFijo? reloj = null, bool manejadorDePrueba = true) =>
        new(modo, canal: canal, agentes: agentes, servicios: s =>
        {
            if (manejadorDePrueba)
            {
                foreach (var real in s.Where(d => d.ImplementationType == typeof(ManejadorExtraerRegistro)).ToList()) s.Remove(real);
                s.AddScoped<IManejadorTarea>(_ => new ManejadorDePrueba(ManejadorDePrueba.SinCandidatura));
            }
            if (reloj is not null)
            {
                s.RemoveAll<IClock>();
                s.AddSingleton<IClock>(reloj);
            }
        });

    private static EjecutorClaudeFalso Falso(FabricaApi fabrica) => (EjecutorClaudeFalso)fabrica.Services.GetRequiredService<IEjecutorClaude>();

    private static async Task<int> EncolarAsync(FabricaApi fabrica) =>
        (await fabrica.Services.GetRequiredService<ColaEjecuciones>().EncolarAsync(TareaAgente.ExtraerRegistro))!.Value.Ejecucion.Id;

    private static Task<HttpResponseMessage> PostJson(HttpClient http, string url, string json = "{}") =>
        http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage respuesta, HttpStatusCode codigo)
    {
        Assert.Equal(codigo, respuesta.StatusCode);
        return await respuesta.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> EsperarAsync(HttpClient http, int id, string estado)
    {
        var limite = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var ejecucion = await http.GetFromJsonAsync<JsonElement>($"/api/agentes/ejecuciones/{id}");
            if (ejecucion.GetProperty("estado").GetString() == estado) return ejecucion;
            if (DateTime.UtcNow > limite) throw new TimeoutException($"La ejecución {id} sigue en {ejecucion.GetProperty("estado")}.");
            await Task.Delay(50);
        }
    }

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/agentes/estado" },
        { "GET", "/api/agentes/ejecuciones" },
        { "GET", "/api/agentes/ejecuciones/1" },
        { "POST", "/api/agentes/ejecuciones/1/cancelar" },
        { "POST", "/api/agentes/ejecuciones/1/reintentar" },
        { "POST", "/api/agentes/analizar/1" },
    };

    private static Task<HttpResponseMessage> Enviar(HttpClient http, string metodo, string url, string tipo = "application/json") =>
        metodo == "GET" ? http.GetAsync(url) : http.PostAsync(url, new StringContent("{}", Encoding.UTF8, tipo));

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task PorElRemoto_Da403YNoCreaNada(string metodo, string url)
    {
        using var fabrica = Fabrica(CanalPeticion.Remoto);
        using var http = fabrica.CreateClient();

        var problema = await LeerAsync(await Enviar(http, metodo, url), HttpStatusCode.Forbidden);

        Assert.Equal("Los agentes solo se pueden lanzar desde el PC.", problema.GetProperty("detail").GetString());
        await using var db = await fabrica.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Empty(db.EjecucionesAgente);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ConElCanalDesconocido_Da403(string metodo, string url)
    {
        using var fabrica = Fabrica(CanalPeticion.Desconocido);
        using var http = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(http, metodo, url)).StatusCode);
    }

    [Theory]
    [InlineData("/api/agentes/ejecuciones/1/cancelar")]
    [InlineData("/api/agentes/analizar/1")]
    public async Task PorElRemotoConTextPlain_Da403Antes415(string url)
    {
        using var fabrica = Fabrica(CanalPeticion.Remoto);
        using var http = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(http, "POST", url, "text/plain")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Apagados_Da403(string metodo, string url)
    {
        using var fabrica = Fabrica(agentes: false);
        using var http = fabrica.CreateClient();

        var problema = await LeerAsync(await Enviar(http, metodo, url), HttpStatusCode.Forbidden);
        Assert.Equal("Los agentes están apagados.", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task EnModoDemo_Da403()
    {
        using var fabrica = Fabrica(modo: "Demo");
        using var http = fabrica.CreateClient();

        var problema = await LeerAsync(await http.GetAsync("/api/agentes/estado"), HttpStatusCode.Forbidden);
        Assert.Equal("Los agentes están apagados.", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task DesdeOtroSitio_Da403()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/agentes/ejecuciones/1/cancelar")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        peticion.Headers.Add("Sec-Fetch-Site", "cross-site");

        var problema = await LeerAsync(await http.SendAsync(peticion), HttpStatusCode.Forbidden);
        Assert.Equal("Esta petición no viene de RadarEmpleo.", problema.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task PostSinJson_Da415(string tipo)
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Enviar(http, "POST", "/api/agentes/ejecuciones/1/cancelar", tipo)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Enviar(http, "POST", "/api/agentes/ejecuciones/1/reintentar", tipo)).StatusCode);
        var id = await SembrarCandidaturaAsync(fabrica);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Enviar(http, "POST", $"/api/agentes/analizar/{id}", tipo)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await http.PostAsync($"/api/agentes/analizar/{id}", null)).StatusCode);
        await using var db = await fabrica.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Empty(db.EjecucionesAgente);
    }

    [Fact]
    public async Task HostQueNoEsLocal_Devuelve400ContraElDnsRebinding()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        using var peticion = new HttpRequestMessage(HttpMethod.Get, "/api/agentes/estado");
        peticion.Headers.Host = "evil.example";

        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(peticion)).StatusCode);
    }

    [Fact]
    public async Task LoQueNoExiste_Da404()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/agentes/ejecuciones/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostJson(http, "/api/agentes/ejecuciones/999/cancelar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostJson(http, "/api/agentes/ejecuciones/999/reintentar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostJson(http, "/api/agentes/analizar/999")).StatusCode);
    }

    /// <summary>Oferta inventada de <c>descargas.json</c> que sigue publicada.</summary>
    private const string OfertaAbierta = "https://empleo.ejemplo.es/ofertas/analista-datos-1234";

    /// <summary>Oferta inventada de <c>descargas.json</c> que ya no está publicada (Lever responde 404).</summary>
    private const string OfertaCerrada = "https://jobs.lever.co/empresa-ficticia/00000000-0000-4000-8000-000000000001";

    private static async Task<int> SembrarCandidaturaAsync(FabricaApi fabrica, string enlace = OfertaAbierta,
        EstadoCandidatura estado = EstadoCandidatura.Detectada)
    {
        await using var db = await fabrica.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var nombre = $"Empresa Ficticia {Guid.NewGuid():N}";
        var candidatura = Candidatura.Nueva(new Empresa { Nombre = nombre, NombreNormalizado = nombre.ToLowerInvariant() },
            "Analista de datos", DateTime.UtcNow, Origen.Manual);
        candidatura.Enlace = enlace;
        if (estado != EstadoCandidatura.Detectada)
            candidatura.CambiarEstado(estado, DateTime.UtcNow, Origen.Manual,
                estado == EstadoCandidatura.Descartada ? MotivoDescarte.Experiencia : null);
        db.Candidaturas.Add(candidatura);
        await db.SaveChangesAsync();
        return candidatura.Id;
    }

    [Fact]
    public async Task Analizar_Da202ConLocation_YRepetirMientrasSigueActivaDa200ConLaMisma()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        Falso(fabrica).Programar(TareaAgente.AnalizarEncaje, new RespuestaFalsa(ModoFalso.NoResponde));
        var id = await SembrarCandidaturaAsync(fabrica);
        var otra = await SembrarCandidaturaAsync(fabrica);

        // Lo que llega en el cuerpo no cuenta: la candidatura es la de la ruta y el origen lo pone el servidor.
        var respuesta = await PostJson(http, $"/api/agentes/analizar/{id}", $$"""{"candidaturaId":{{otra}},"origen":"Manual","prompt":"x"}""");
        var nueva = await LeerAsync(respuesta, HttpStatusCode.Accepted);
        var ejecucionId = nueva.GetProperty("id").GetInt32();
        Assert.EndsWith($"/api/agentes/ejecuciones/{ejecucionId}", respuesta.Headers.Location!.ToString());
        Assert.Equal("AnalizarEncaje", nueva.GetProperty("tarea").GetString());
        Assert.Equal(id, nueva.GetProperty("candidaturaId").GetInt32());
        Assert.Equal(id, nueva.GetProperty("candidatura").GetProperty("id").GetInt32());
        Assert.Equal(0.50m, nueva.GetProperty("presupuestoUsd").GetDecimal());

        var repetida = await LeerAsync(await PostJson(http, $"/api/agentes/analizar/{id}"), HttpStatusCode.OK);
        Assert.Equal(ejecucionId, repetida.GetProperty("id").GetInt32());

        await EsperarAsync(http, ejecucionId, "EnCurso");
        var enCurso = await LeerAsync(await PostJson(http, $"/api/agentes/analizar/{id}"), HttpStatusCode.OK);
        Assert.Equal(ejecucionId, enCurso.GetProperty("id").GetInt32());

        await PostJson(http, $"/api/agentes/ejecuciones/{ejecucionId}/cancelar");
        await EsperarAsync(http, ejecucionId, "Cancelada");
        await using var db = await fabrica.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await db.EjecucionesAgente.CountAsync());
    }

    [Theory]
    [InlineData(EstadoCandidatura.Descartada)]
    [InlineData(EstadoCandidatura.Cerrada)]
    public async Task Analizar_UnaDescartadaOCerrada_Da409YNoCreaNada(EstadoCandidatura estado)
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        var id = await SembrarCandidaturaAsync(fabrica, estado: estado);

        var problema = await LeerAsync(await PostJson(http, $"/api/agentes/analizar/{id}"), HttpStatusCode.Conflict);

        Assert.Equal(ManejadorAnalizarEncaje.MensajeCandidaturaFinal, problema.GetProperty("detail").GetString());
        await using var db = await fabrica.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Empty(db.EjecucionesAgente);
    }

    [Fact]
    public async Task Analizar_UnaOfertaCerrada_TerminaEnOkSinClaudeYConCoste0()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        var id = await SembrarCandidaturaAsync(fabrica, OfertaCerrada);

        var nueva = await LeerAsync(await PostJson(http, $"/api/agentes/analizar/{id}"), HttpStatusCode.Accepted);
        var ok = await EsperarAsync(http, nueva.GetProperty("id").GetInt32(), "Ok");

        Assert.Equal(0m, ok.GetProperty("costeUsd").GetDecimal());
        Assert.False(ok.GetProperty("costeEstimado").GetBoolean());
        Assert.Equal("Cerrada", ok.GetProperty("estadoOferta").GetString());
        Assert.Equal(ManejadorAnalizarEncaje.MensajeOfertaCerrada, ok.GetProperty("resumen").GetString());
        Assert.Empty(Falso(fabrica).Peticiones);
        var estado = await http.GetFromJsonAsync<JsonElement>("/api/agentes/estado");
        Assert.Equal(0m, estado.GetProperty("gasto").GetProperty("gastadoUsd").GetDecimal());
        // Nunca se descarta sola: la ficha lo propone.
        var ficha = await http.GetFromJsonAsync<JsonElement>($"/api/candidaturas/{id}");
        Assert.Equal("Detectada", ficha.GetProperty("estado").GetString());
    }

    [Theory]
    [InlineData("?limite=0")]
    [InlineData("?limite=201")]
    [InlineData("?tarea=99")]
    public async Task Listar_ConParametrosFueraDeRango_Da400(string consulta)
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync($"/api/agentes/ejecuciones{consulta}")).StatusCode);
    }

    [Fact]
    public async Task UnaEjecucion_PasaAOkYSeVeEnLaListaYEnElDetalle()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        Falso(fabrica).Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(CosteUsd: 0.18m));

        var id = await EncolarAsync(fabrica);
        var ok = await EsperarAsync(http, id, "Ok");

        Assert.Equal("ExtraerRegistro", ok.GetProperty("tarea").GetString());
        Assert.Equal(0.18m, ok.GetProperty("costeUsd").GetDecimal());
        Assert.False(ok.GetProperty("costeEstimado").GetBoolean());
        Assert.False(ok.GetProperty("registroCambiado").GetBoolean());
        Assert.False(ok.GetProperty("puedeCancelar").GetBoolean());
        // Salió bien: no se reintenta (se lanza otra vez desde su pantalla).
        Assert.False(ok.GetProperty("puedeReintentar").GetBoolean());
        var problema = await LeerAsync(await PostJson(http, $"/api/agentes/ejecuciones/{id}/reintentar"), HttpStatusCode.Conflict);
        Assert.Equal(ColaEjecuciones.MensajeYaTerminoBien, problema.GetProperty("detail").GetString());
        Assert.Equal("Hecho por la tarea de prueba.", ok.GetProperty("resumen").GetString());
        // Lo interno (pid, log, hashes, resultado) no sale por la API.
        Assert.False(ok.TryGetProperty("pid", out _));
        Assert.False(ok.TryGetProperty("rutaLog", out _));
        Assert.False(ok.TryGetProperty("hashRegistroAntes", out _));

        var lista = await http.GetFromJsonAsync<JsonElement>("/api/agentes/ejecuciones?tarea=ExtraerRegistro&limite=1");
        Assert.Equal(id, Assert.Single(lista.EnumerateArray()).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Cancelar_EnCursoDa200YQuedaCancelada_YOtraVezDa409()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        Falso(fabrica).Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(ModoFalso.NoResponde));
        var id = await EncolarAsync(fabrica);
        await EsperarAsync(http, id, "EnCurso");

        var cancelando = await LeerAsync(await PostJson(http, $"/api/agentes/ejecuciones/{id}/cancelar"), HttpStatusCode.OK);
        Assert.False(cancelando.GetProperty("puedeCancelar").GetBoolean());
        await EsperarAsync(http, id, "Cancelada");
        Assert.Equal(1, Falso(fabrica).Canceladas);

        var problema = await LeerAsync(await PostJson(http, $"/api/agentes/ejecuciones/{id}/cancelar"), HttpStatusCode.Conflict);
        Assert.Equal("Ya ha terminado.", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Reintentar_CreaOtraCon202YLocation_YSiNoHaTerminadoDa409()
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        Falso(fabrica).Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(ModoFalso.Error));
        var id = await EncolarAsync(fabrica);
        var fallida = await EsperarAsync(http, id, "Error");
        Assert.True(fallida.GetProperty("puedeReintentar").GetBoolean());
        Falso(fabrica).Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(ModoFalso.NoResponde));

        var respuesta = await PostJson(http, $"/api/agentes/ejecuciones/{id}/reintentar");
        var nueva = await LeerAsync(respuesta, HttpStatusCode.Accepted);
        var nuevoId = nueva.GetProperty("id").GetInt32();
        Assert.NotEqual(id, nuevoId);
        Assert.EndsWith($"/api/agentes/ejecuciones/{nuevoId}", respuesta.Headers.Location!.ToString());

        // La nueva sigue activa: reintentar la vieja devuelve la activa (200) y la activa no se puede reintentar (409).
        var otraVez = await LeerAsync(await PostJson(http, $"/api/agentes/ejecuciones/{id}/reintentar"), HttpStatusCode.OK);
        Assert.Equal(nuevoId, otraVez.GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await PostJson(http, $"/api/agentes/ejecuciones/{nuevoId}/reintentar")).StatusCode);
        await PostJson(http, $"/api/agentes/ejecuciones/{nuevoId}/cancelar");
    }

    [Fact]
    public async Task Estado_TraeElGastoDeHoyLaVersionYLoQueEstaEnCurso()
    {
        var reloj = new RelojFijo(Ahora);
        using var fabrica = Fabrica(reloj: reloj);
        using var http = fabrica.CreateClient();
        await using (var db = await fabrica.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var hoy = EjecucionAgente.Nueva(TareaAgente.AnalizarEncaje, "{}", 0.50m, 300, null, Ahora.AddHours(-2));
            hoy.Empezar(Ahora.AddHours(-2), null, null);
            hoy.Terminar(Ahora.AddHours(-2), 0.42m, 3, null, "x", null, null, null);
            var ayer = EjecucionAgente.Nueva(TareaAgente.AnalizarEncaje, "{}", 0.50m, 300, null, Ahora.AddDays(-1));
            ayer.Empezar(Ahora.AddDays(-1), null, null);
            ayer.Terminar(Ahora.AddDays(-1), 5m, 3, null, "x", null, null, null);
            db.EjecucionesAgente.AddRange(hoy, ayer);
            await db.SaveChangesAsync();
        }
        Falso(fabrica).Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(ModoFalso.NoResponde));
        var id = await EncolarAsync(fabrica);
        await EsperarAsync(http, id, "EnCurso");

        var estado = await http.GetFromJsonAsync<JsonElement>("/api/agentes/estado");

        Assert.True(estado.GetProperty("disponibles").GetBoolean());
        Assert.Equal(JsonValueKind.Null, estado.GetProperty("motivoNoDisponibles").ValueKind);
        Assert.Equal(EjecutorClaudeFalso.VersionFalsa, estado.GetProperty("versionClaude").GetString());
        Assert.Equal("2.1.185", estado.GetProperty("versionProbada").GetString());
        Assert.True(estado.GetProperty("versionDistinta").GetBoolean());
        var gasto = estado.GetProperty("gasto");
        Assert.Equal("2026-11-16", gasto.GetProperty("hoy").GetString());
        Assert.Equal(0.42m, gasto.GetProperty("gastadoUsd").GetDecimal());
        Assert.Equal(0.50m, gasto.GetProperty("reservadoUsd").GetDecimal());
        Assert.Equal(15m, gasto.GetProperty("topeUsd").GetDecimal());
        Assert.Equal(14.08m, gasto.GetProperty("disponibleUsd").GetDecimal());
        Assert.Equal(2, gasto.GetProperty("ejecuciones").GetInt32());
        Assert.Equal(id, estado.GetProperty("enCurso").GetProperty("id").GetInt32());
        Assert.Equal(0, estado.GetProperty("enCola").GetInt32());

        await PostJson(http, $"/api/agentes/ejecuciones/{id}/cancelar");
        await EsperarAsync(http, id, "Cancelada");
    }

    [Fact]
    public async Task EstadoOferta_SoloEnAnalizarEncajeYSiLoDejoEnSuResultado()
    {
        var reloj = new RelojFijo(Ahora);
        using var fabrica = Fabrica(reloj: reloj);
        using var http = fabrica.CreateClient();
        var resultados = new (TareaAgente Tarea, string? Json)[]
        {
            (TareaAgente.AnalizarEncaje, """{"estadoOferta":"Cerrada","motivoOferta":"x","ofertaLeida":false}"""),
            (TareaAgente.AnalizarEncaje, """{"estadoOferta":"NoSeSabe","motivoOferta":"x","ofertaLeida":true,"encaje":"Medio"}"""),
            (TareaAgente.AnalizarEncaje, null),
            (TareaAgente.AnalizarEncaje, "no es json"),
            (TareaAgente.AnalizarEncaje, """{"estadoOferta":1}"""),
            (TareaAgente.AnalizarEncaje, """{"estadoOferta":"Cerrada, Abierta"}"""),
            (TareaAgente.ExtraerRegistro, """{"estadoOferta":"Cerrada"}"""),
        };
        await using (var db = await fabrica.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            foreach (var (tarea, json) in resultados)
            {
                var ejecucion = EjecucionAgente.Nueva(tarea, "{}", 0.50m, 300, null, Ahora);
                ejecucion.Empezar(Ahora, null, null);
                ejecucion.Terminar(Ahora, 0m, null, null, "x", json, null, null);
                db.EjecucionesAgente.Add(ejecucion);
            }
            await db.SaveChangesAsync();
        }

        var lista = await http.GetFromJsonAsync<JsonElement>("/api/agentes/ejecuciones");
        var estados = lista.EnumerateArray().Reverse()
            .Select(e => e.GetProperty("estadoOferta") is { ValueKind: JsonValueKind.String } s ? s.GetString() : null).ToList();

        Assert.Equal(["Cerrada", "NoSeSabe", null, null, null, null, null], estados);
    }

    [Theory]
    [InlineData(CanalPeticion.Local, true, true)]
    [InlineData(CanalPeticion.Remoto, true, false)]
    [InlineData(CanalPeticion.Local, false, false)]
    public async Task SistemaInfo_AgentesDisponiblesSoloDesdeElPcYEncendidos(CanalPeticion canal, bool agentes, bool disponibles)
    {
        using var fabrica = Fabrica(canal, agentes);
        using var http = fabrica.CreateClient();

        var info = await http.GetFromJsonAsync<JsonElement>("/api/sistema/info");

        Assert.Equal(disponibles, info.GetProperty("agentesDisponibles").GetBoolean());
        // Por el móvil no se dice si están habilitados.
        Assert.Equal(agentes && canal == CanalPeticion.Local, info.GetProperty("agentesHabilitados").GetBoolean());
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("same-origin", null, true)]
    [InlineData("none", null, true)]
    [InlineData("same-site", "http://localhost:4200", true)]
    [InlineData("same-site", "http://localhost", true)]
    [InlineData("same-site", "http://localhost:8099", false)]
    [InlineData("same-site", null, false)]
    [InlineData("cross-site", "https://evil.example", false)]
    [InlineData("otra-cosa", null, false)]
    public async Task SecFetchSite_SoloDesdeRadarEmpleo(string? sitio, string? origen, bool pasa)
    {
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        HttpRequestMessage Peticion(string url)
        {
            var m = new HttpRequestMessage(HttpMethod.Get, url);
            if (sitio is not null) m.Headers.Add("Sec-Fetch-Site", sitio);
            if (origen is not null) m.Headers.Add("Origin", origen);
            return m;
        }

        using var estado = Peticion("/api/agentes/estado");
        Assert.Equal(pasa ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await http.SendAsync(estado)).StatusCode);
        // Sistema dice lo mismo que el filtro.
        using var info = Peticion("/api/sistema/info");
        var cuerpo = await (await http.SendAsync(info)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(pasa, cuerpo.GetProperty("agentesDisponibles").GetBoolean());
    }

    [Fact]
    public async Task ElProxyDeNgServe_Pasa()
    {
        // Con el proxy de «ng serve», el navegador está en localhost:4200 y la petición es de su mismo origen.
        using var fabrica = Fabrica();
        using var http = fabrica.CreateClient();
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/agentes/ejecuciones/999/cancelar")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        peticion.Headers.Add("Sec-Fetch-Site", "same-origin");
        peticion.Headers.Add("Origin", "http://localhost:4200");

        Assert.Equal(HttpStatusCode.NotFound, (await http.SendAsync(peticion)).StatusCode);
    }

    [Fact]
    public void EjecutorFalso_ConLaBaseDeDatosPorDefecto_NoArranca()
    {
        var falso = new OpcionesAgentes { Ejecutor = TipoEjecutor.Falso };

        var error = Assert.Throws<InvalidOperationException>(() => ConfiguracionAgentes.ExigirFalsoPermitido(falso, new OpcionesRadar()));
        Assert.Contains("base de datos temporal", error.Message);
        Assert.Throws<InvalidOperationException>(() => ConfiguracionAgentes.ExigirFalsoPermitido(falso,
            new OpcionesRadar { Modo = ModoEjecucion.Demo, RutaBaseDatos = Path.Combine(Path.GetTempPath(), "x.db") }));
        ConfiguracionAgentes.ExigirFalsoPermitido(falso, new OpcionesRadar { RutaBaseDatos = Path.Combine(Path.GetTempPath(), "x.db") });
        // Con el Cli no se exige nada.
        ConfiguracionAgentes.ExigirFalsoPermitido(new OpcionesAgentes(), new OpcionesRadar());
    }

    [Fact]
    public void ArgumentosPrevios_ConElCli_NoArranca()
    {
        var cli = new OpcionesAgentes { Ejecutor = TipoEjecutor.Cli, ArgumentosPrevios = ["--dangerously-skip-permissions"] };
        Assert.Throws<InvalidOperationException>(() => ConfiguracionAgentes.ExigirFalsoPermitido(cli, new OpcionesRadar()));
    }

    public static TheoryData<string> RutasTrampa
    {
        get
        {
            var real = new OpcionesRadar().RutaBaseDatosEfectiva();
            var temporal = Path.GetTempPath();
            return
            [
                real,
                @"\\?\" + real,
                @"\\.\" + real,
                @"\\localhost\C$\" + real[3..],
                Path.Combine(temporal, "x.db::$DATA"),
                Path.Combine(temporal, "..", "RadarEmpleo", "radar.db"),
                Path.Combine(Path.TrimEndingDirectorySeparator(temporal) + "otra", "x.db"),
                "//localhost/c$/x.db"
            ];
        }
    }

    [Theory]
    [MemberData(nameof(RutasTrampa))]
    public void EstaBajoTemporal_LasRutasTrampaNo(string ruta)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.False(ConfiguracionAgentes.EstaBajoTemporal(ruta, Path.GetTempPath()));
    }

    [Fact]
    public void EstaBajoTemporal_UnaBdTemporalNormalSi()
    {
        Assert.True(ConfiguracionAgentes.EstaBajoTemporal(Path.Combine(Path.GetTempPath(), "radar-x", "radar.db"), Path.GetTempPath()));
    }

    [Fact]
    public void EstaBajoTemporal_ConUnaJunctionEnElCamino_No()
    {
        if (!OperatingSystem.IsWindows()) return;
        var base_ = Path.Combine(Path.GetTempPath(), $"radar-junction-{Guid.NewGuid():N}");
        var destino = Directory.CreateDirectory(Path.Combine(base_, "destino")).FullName;
        var enlace = Path.Combine(base_, "enlace");
        try
        {
            Directory.CreateSymbolicLink(enlace, destino);
        }
        catch (IOException)
        {
            Directory.Delete(base_, recursive: true);
            return; // Sin permiso para crear enlaces simbólicos en este PC: no se puede probar aquí.
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(base_, recursive: true);
            return;
        }
        try
        {
            Assert.False(ConfiguracionAgentes.EstaBajoTemporal(Path.Combine(enlace, "radar.db"), Path.GetTempPath()));
        }
        finally
        {
            Directory.Delete(enlace);
            Directory.Delete(base_, recursive: true);
        }
    }
}
