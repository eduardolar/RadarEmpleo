using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Agentes;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Ofertas;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="ManejadorAnalizarEncaje"/> de principio a fin por la cola y el procesador, con <see cref="EjecutorClaudeFalso"/>,
/// <see cref="DescargadorOfertasFalso"/>, una BD SQLite temporal y una Curriculum temporal con los datos de ejemplo.
/// Nunca se lanza Claude ni se sale a internet. Datos inventados.
/// </summary>
public sealed class ManejadorAnalizarEncajeTests : IDisposable
{
    private const string Enlace = "https://empleo.ejemplo.es/ofertas/analista-1";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), $"radar-encaje-{Guid.NewGuid():N}");
    /// <summary>18/11/2026 a las 12:00 de Madrid: una hora fija, lejos de medianoche y de los cambios de hora.</summary>
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 18, 11, 0, 0, DateTimeKind.Utc));
    private readonly TestDbContextFactory _factory;
    private readonly RutasCurriculum _rutas;
    private readonly EjecutorClaudeFalso _claude;
    private readonly DescargadorOfertasFalso _descargas = new();
    private IDescargadorOfertas? _descargaQueEspera;
    private readonly ExportadorFalso _exportador = new();
    private readonly ServiceProvider _servicios;
    private readonly GuardiaCola _guardia;
    private readonly ColaEjecuciones _cola;
    private readonly ProcesadorEjecuciones _procesador;

    public ManejadorAnalizarEncajeTests()
    {
        var carpeta = Path.Combine(_raiz, "Curriculum");
        FabricaApi.CopiarDatosEjemplo(carpeta);
        _rutas = new RutasCurriculum(carpeta);
        var opcionesBd = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_raiz, "radar.db") }.ToString()).Options;
        using (var db = new AppDbContext(opcionesBd)) db.Database.EnsureCreated();
        _factory = new TestDbContextFactory(opcionesBd);

        var opciones = new OpcionesEjecutor("claude.exe", [], Path.Combine(_raiz, "datos"), 15m, "2.1.185");
        var lector = new LectorCurriculum(_rutas);
        _claude = new EjecutorClaudeFalso(_reloj);
        _servicios = new ServiceCollection()
            .AddSingleton<IDbContextFactory<AppDbContext>>(_factory)
            .AddSingleton<IClock>(_reloj)
            .AddSingleton<IExportadorRadar>(_exportador)
            .AddSingleton<IDescargadorOfertas>(_ => _descargaQueEspera ?? (IDescargadorOfertas)_descargas)
            .AddSingleton<ILectorCurriculum>(lector)
            .AddSingleton(_rutas)
            .AddSingleton<ComprobadorOfertaAbierta>()
            .AddScoped<CandidaturasService>()
            .AddScoped<IManejadorTarea, ManejadorAnalizarEncaje>()
            .BuildServiceProvider();
        var ambitos = _servicios.GetRequiredService<IServiceScopeFactory>();
        _guardia = new GuardiaCola(opciones);
        _cola = new ColaEjecuciones(_factory, _reloj, _rutas, opciones, ambitos, _guardia);
        _procesador = new ProcesadorEjecuciones(_factory, _reloj, _rutas, opciones, _cola,
            new BloqueoCompartido(_rutas, NullLogger<BloqueoCompartido>.Instance), _claude, lector, ambitos, _guardia,
            NullLogger<ProcesadorEjecuciones>.Instance);
    }

    public void Dispose()
    {
        _procesador.Dispose();
        _servicios.Dispose();
        _guardia.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { /* temporal */ }
    }

    private int Sembrar(EstadoCandidatura estado = EstadoCandidatura.Detectada, string enlace = Enlace)
    {
        using var db = _factory.CreateDbContext();
        var candidatura = Candidatura.Nueva(new Empresa { Nombre = "Empresa Ficticia", NombreNormalizado = "empresa ficticia" },
            "Analista de datos", _reloj.AhoraUtc, Origen.Manual);
        candidatura.Enlace = enlace;
        candidatura.Modalidad = Modalidad.Hibrido;
        if (estado != EstadoCandidatura.Detectada) candidatura.CambiarEstado(estado, _reloj.AhoraUtc, Origen.Manual, MotivoDescarte.Experiencia);
        db.Candidaturas.Add(candidatura);
        db.SaveChanges();
        return candidatura.Id;
    }

    private void ProgramarClaude(object salida) =>
        _claude.Programar(TareaAgente.AnalizarEncaje, new RespuestaFalsa(Salida: JsonSerializer.SerializeToElement(salida), CosteUsd: 0.18m));

    private static object Salida(string encaje = "Medio", string pega = "Piden inglés B2.") =>
        new { encaje, pega, analisis = "- Encaja el puesto.\n- Falla el inglés.", resumen = "Encaje medio." };

    private async Task<EjecucionAgente> AnalizarAsync(int candidaturaId, Action? antesDeProcesar = null)
    {
        var (encolada, _) = (await _cola.EncolarAsync(TareaAgente.AnalizarEncaje, candidaturaId))!.Value;
        antesDeProcesar?.Invoke();
        Assert.True(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));
        await using var db = _factory.CreateDbContext();
        return await db.EjecucionesAgente.AsNoTracking().SingleAsync(e => e.Id == encolada.Id);
    }

    private async Task<Candidatura?> LeerAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.Candidaturas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    }

    private static JsonElement Json(EjecucionAgente e) => JsonDocument.Parse(e.ResultadoJson!).RootElement;

    [Fact]
    public async Task OfertaAbierta_ConClaudeOk_RellenaLaFichaYGuardaElEstadoDeLaOferta()
    {
        _descargas.Programar(Enlace, new RespuestaDescargaFalsa(Cuerpo: "<h1>Analista de datos</h1><p>Buscamos SQL avanzado.</p>"));
        ProgramarClaude(Salida());
        var id = Sembrar();

        var ejecucion = await AnalizarAsync(id);

        Assert.Equal(EstadoEjecucion.Ok, ejecucion.Estado);
        Assert.Equal("Encaje medio. Pega: Piden inglés B2.", ejecucion.Resumen);
        Assert.Equal(0.18m, ejecucion.CosteUsd);
        Assert.Equal("Abierta", Json(ejecucion).GetProperty("estadoOferta").GetString());
        Assert.True(Json(ejecucion).GetProperty("ofertaLeida").GetBoolean());
        Assert.Equal("Medio", Json(ejecucion).GetProperty("encaje").GetString());
        var candidatura = (await LeerAsync(id))!;
        Assert.Equal(Encaje.Medio, candidatura.Encaje);
        Assert.Equal("Piden inglés B2.", candidatura.Pega);
        Assert.Equal(1, _exportador.Llamadas);

        var peticion = Assert.Single(_claude.Peticiones);
        Assert.Contains("Buscamos SQL avanzado.", peticion.EntradaEstandar);
        Assert.Contains("- Modalidad: Híbrido", peticion.EntradaEstandar);
        Assert.Contains("Perfil de búsqueda (ejemplo inventado)", peticion.EntradaEstandar);
        Assert.Equal(new[] { "--tools", "" }, peticion.Argumentos.SkipWhile(a => a != "--tools").Take(2));
        Assert.Equal(new[] { (Enlace, TipoDescarga.Pagina) }, _descargas.Peticiones);
    }

    [Fact]
    public async Task OfertaCerrada_TerminaEnOkSinLlamarAClaudeNiGastarNiTocarLaFicha()
    {
        _descargas.Programar(Enlace, new RespuestaDescargaFalsa(Codigo: 404));
        var id = Sembrar();

        var ejecucion = await AnalizarAsync(id);

        Assert.Equal(EstadoEjecucion.Ok, ejecucion.Estado);
        Assert.Equal(ManejadorAnalizarEncaje.MensajeOfertaCerrada, ejecucion.Resumen);
        Assert.Equal(0m, ejecucion.CosteUsd);
        Assert.Equal("Cerrada", Json(ejecucion).GetProperty("estadoOferta").GetString());
        Assert.False(Json(ejecucion).TryGetProperty("encaje", out _));
        Assert.Empty(_claude.Peticiones);
        Assert.Equal(Encaje.SinValorar, (await LeerAsync(id))!.Encaje);
    }

    [Fact]
    public async Task OfertaQueNoSePuedeLeer_SeAnalizaConLaFichaYElResumenLoDice()
    {
        ProgramarClaude(Salida("TiroLargo"));
        var id = Sembrar();

        var ejecucion = await AnalizarAsync(id);

        Assert.Equal(EstadoEjecucion.Ok, ejecucion.Estado);
        Assert.StartsWith("Tiro largo. No he podido leer la oferta: analizo con los datos de la ficha. Pega: ", ejecucion.Resumen);
        Assert.Equal("NoSeSabe", Json(ejecucion).GetProperty("estadoOferta").GetString());
        Assert.False(Json(ejecucion).GetProperty("ofertaLeida").GetBoolean());
        Assert.Contains(PromptAnalizarEncaje.OfertaSinLeer, Assert.Single(_claude.Peticiones).EntradaEstandar);
        Assert.Equal(Encaje.TiroLargo, (await LeerAsync(id))!.Encaje);
    }

    [Theory]
    [InlineData("<p>Inicia sesión para ver esta oferta</p>", null)]
    [InlineData("<h1>Bienvenido a Otra Web</h1>", "https://otra.ejemplo.es/empleo/1")]
    [InlineData("<h1>Todas nuestras ofertas</h1>", "https://empleo.ejemplo.es/")]
    public async Task PaginaQueNoEsLaOferta_SeAnalizaConLaFichaSinMandarSuTexto(string cuerpo, string? urlFinal)
    {
        _descargas.Programar(Enlace, new RespuestaDescargaFalsa(Cuerpo: cuerpo, UrlFinal: urlFinal));
        ProgramarClaude(Salida());

        var ejecucion = await AnalizarAsync(Sembrar());

        Assert.StartsWith("Encaje medio. No he podido leer la oferta", ejecucion.Resumen);
        Assert.False(Json(ejecucion).GetProperty("ofertaLeida").GetBoolean());
        Assert.Equal("NoSeSabe", Json(ejecucion).GetProperty("estadoOferta").GetString());
        var prompt = Assert.Single(_claude.Peticiones).EntradaEstandar;
        Assert.Contains(PromptAnalizarEncaje.OfertaSinLeer, prompt);
        Assert.DoesNotContain(HtmlATexto.Convertir(cuerpo), prompt);
    }

    [Fact]
    public async Task OfertaDeLeverFueraDelListadoConLaPaginaLegible_SeAnalizaYElResumenDiceQueNoSeSabeConLaPegaRecortada()
    {
        const string EnlaceLever = "https://jobs.lever.co/empresa-ficticia/00000000-0000-4000-8000-000000000001";
        _descargas.Programar(EnlaceLever, new RespuestaDescargaFalsa(Cuerpo: "<h1>Analista de datos</h1><p>Buscamos SQL.</p>"));
        _descargas.Programar(SistemasSeleccion.Detectar(EnlaceLever)!.UrlListado, new RespuestaDescargaFalsa(TipoContenido: "application/json", Cuerpo: "[]"));
        ProgramarClaude(Salida(pega: new string('x', 500)));

        var ejecucion = await AnalizarAsync(Sembrar(enlace: EnlaceLever));

        Assert.StartsWith("Encaje medio. No sé si sigue abierta. Pega: xxx", ejecucion.Resumen);
        Assert.Equal(ManejadorAnalizarEncaje.LargoResumen, ejecucion.Resumen!.Length);
        Assert.EndsWith("…", ejecucion.Resumen);
        Assert.True(Json(ejecucion).GetProperty("ofertaLeida").GetBoolean());
        Assert.Contains("Buscamos SQL.", Assert.Single(_claude.Peticiones).EntradaEstandar);
    }

    public static TheoryData<string> SalidasNoValidas => new()
    {
        """{"encaje":"Regular","pega":"","analisis":"","resumen":""}""",
        """{"encaje":"SinValorar","pega":"","analisis":"","resumen":""}""",
        """{"encaje":1,"pega":"","analisis":"","resumen":""}""",
        $$"""{"encaje":"Alto","pega":"{{new string('x', 3000)}}","analisis":"","resumen":""}""",
        """{"encaje":"Alto","pega":"","resumen":""}""",
        """{"encaje":"Alto","pega":7,"analisis":"","resumen":""}""",
        "[]"
    };

    [Theory]
    [MemberData(nameof(SalidasNoValidas))]
    public async Task SalidaFueraDelEsquema_DaErrorYNoTocaLaFicha(string salida)
    {
        _claude.Programar(TareaAgente.AnalizarEncaje, new RespuestaFalsa(Salida: JsonDocument.Parse(salida).RootElement));
        var id = Sembrar();

        var ejecucion = await AnalizarAsync(id);

        Assert.Equal(EstadoEjecucion.Error, ejecucion.Estado);
        Assert.Equal(MensajesEjecucion.SalidaNoValida, ejecucion.Error);
        Assert.Equal(Encaje.SinValorar, (await LeerAsync(id))!.Encaje);
    }

    [Fact]
    public async Task CandidaturaBorradaMientrasTrabajaClaude_QuedaOkSinGuardarNada()
    {
        ProgramarClaude(Salida());
        var id = Sembrar();
        _claude.AntesDeResponder = async (_, _) =>
        {
            await using var db = _factory.CreateDbContext();
            await db.Candidaturas.Where(c => c.Id == id).ExecuteDeleteAsync();
        };

        var ejecucion = await AnalizarAsync(id);

        Assert.Equal(EstadoEjecucion.Ok, ejecucion.Estado);
        Assert.Equal(ManejadorAnalizarEncaje.MensajeCandidaturaBorrada, ejecucion.Resumen);
        Assert.Equal(0, _exportador.Llamadas);
    }

    [Fact]
    public async Task CandidaturaBorradaAntesDeEmpezar_QuedaOkSinLlamarAClaude()
    {
        var id = Sembrar();

        var ejecucion = await AnalizarAsync(id, () =>
        {
            using var db = _factory.CreateDbContext();
            db.Candidaturas.Where(c => c.Id == id).ExecuteDelete();
        });

        Assert.Equal(EstadoEjecucion.Ok, ejecucion.Estado);
        Assert.Equal(ManejadorAnalizarEncaje.MensajeCandidaturaBorrada, ejecucion.Resumen);
        Assert.Empty(_claude.Peticiones);
        Assert.Empty(_descargas.Peticiones);
    }

    /// <summary>Una descarga que no acaba hasta que la cortan; antes, pide cancelar la ejecución.</summary>
    private sealed class DescargaQueEspera(Func<Task> alEmpezar) : IDescargadorOfertas
    {
        public async Task<DescargaOferta> DescargarAsync(string enlace, TipoDescarga tipo, CancellationToken cancelacion)
        {
            await alEmpezar();
            await Task.Delay(Timeout.Infinite, cancelacion);
            throw new InvalidOperationException("No llega.");
        }
    }

    [Fact]
    public async Task CancelarMientrasSeDescargaLaOferta_QuedaCanceladaSinGastoNiClaude()
    {
        var id = Sembrar();
        var ejecucionId = 0;
        _descargaQueEspera = new DescargaQueEspera(() => _cola.CancelarAsync(ejecucionId));

        var ejecucion = await AnalizarAsync(id, () =>
        {
            using var db = _factory.CreateDbContext();
            ejecucionId = db.EjecucionesAgente.Single().Id;
        });

        Assert.Equal(EstadoEjecucion.Cancelada, ejecucion.Estado);
        Assert.Equal(0m, ejecucion.CosteUsd);
        Assert.Equal(0m, ReglasGasto.GastoComputable(ejecucion));
        Assert.Empty(_claude.Peticiones);
    }

    [Fact]
    public async Task Encolar_ValidaLaCandidatura()
    {
        await Assert.ThrowsAsync<ValidacionException>(() => _cola.EncolarAsync(TareaAgente.AnalizarEncaje));
        Assert.Null(await _cola.EncolarAsync(TareaAgente.AnalizarEncaje, 999));
        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            _cola.EncolarAsync(TareaAgente.AnalizarEncaje, Sembrar(EstadoCandidatura.Descartada)));
        Assert.Equal(ManejadorAnalizarEncaje.MensajeCandidaturaFinal, error.Message);
    }
}
