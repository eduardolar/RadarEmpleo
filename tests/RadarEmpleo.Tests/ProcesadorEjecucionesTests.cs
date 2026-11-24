using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Agentes;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary>
/// Manejador que solo existe en los tests: lanza una «tarea» con la definición que se le dé y devuelve lo que diga el
/// ejecutor falso. Sirve para probar la cola, el procesador y la API sin las tareas reales de la F7b.
/// </summary>
public sealed class ManejadorDePrueba : IManejadorTarea
{
    public static readonly DefinicionTarea SinCandidatura = new(TareaAgente.ExtraerRegistro, 0.50m, TimeSpan.FromMinutes(5),
        CarpetaTrabajo.Ejecucion, "", null, null, null, "prueba");

    public static readonly DefinicionTarea ConCandidatura = new(TareaAgente.AnalizarEncaje, 0.50m, TimeSpan.FromMinutes(5),
        CarpetaTrabajo.Curriculum, "Read", "Read", "Bash", null, "prueba-encaje");

    private readonly DefinicionTarea _definicion;

    public ManejadorDePrueba(DefinicionTarea definicion) => _definicion = definicion;

    /// <summary>La que se le da y, con <see cref="Partes"/>, con el reparto de <see cref="PorPartes"/>.</summary>
    public DefinicionTarea Definicion => Partes is null ? _definicion : _definicion with { Partes = PorPartes };

    /// <summary>Lo que hace al aplicar la respuesta (por defecto, nada).</summary>
    public static Func<RespuestaClaude, Task>? AlAplicar { get; set; }

    /// <summary>Lo que hace al montar la petición, con el corte de la ejecución (por defecto, nada).</summary>
    public static Func<CancellationToken, Task>? AlConstruir { get; set; }

    /// <summary>Si no es null, la tarea termina al prepararla con este resultado, sin lanzar Claude.</summary>
    public static ResultadoTarea? SinClaude { get; set; }

    /// <summary>Si no es null, la tarea se lanza en tantas partes, con el reparto de <see cref="PorPartes"/>.</summary>
    public static int? Partes { get; set; }

    /// <summary>Si no es null, las peticiones que prepara, en vez de las suyas (para probar las que no caben).</summary>
    public static Func<ContextoTarea, DefinicionTarea, IReadOnlyList<PeticionClaude>>? OtrasPeticiones { get; set; }

    /// <summary>Lo que ha recibido <see cref="AplicarPartesAsync"/> la última vez.</summary>
    public static IReadOnlyList<(PeticionClaude Peticion, RespuestaClaude Respuesta)>? PartesRecibidas { get; set; }

    /// <summary>Como mucho 0,20 y 2 min por parte: con 3 partes de 0,50 y 5 min, 0,16 y 1 min 40 s cada una.</summary>
    public static readonly PartesTarea PorPartes = new(16_000, 6, 0.20m, TimeSpan.FromMinutes(2));

    /// <summary>Deja los estáticos como al principio.</summary>
    public static void Limpiar()
    {
        AlAplicar = null;
        AlConstruir = null;
        SinClaude = null;
        Partes = null;
        OtrasPeticiones = null;
        PartesRecibidas = null;
    }

    public Task<bool> ValidarAsync(int? candidaturaId, CancellationToken cancelacion)
    {
        if (Definicion.Tarea == TareaAgente.AnalizarEncaje && candidaturaId is null)
            throw new ValidacionException("Falta la candidatura.", "candidaturaId");
        return Task.FromResult(candidaturaId is null or > 0);
    }

    public async Task<PreparacionTarea> ConstruirAsync(EjecucionAgente ejecucion, ContextoTarea contexto, CancellationToken cancelacion)
    {
        if (AlConstruir is { } alConstruir) await alConstruir(cancelacion);
        if (SinClaude is { } sinClaude) return PreparacionTarea.Terminada(sinClaude);
        var valores = new ValoresArgumentos(contexto.CarpetaDatos);
        if (OtrasPeticiones is { } otras) return PreparacionTarea.ConClaude(otras(contexto, Definicion));
        if (Partes is { } n)
        {
            var definicion = Definicion;
            return PreparacionTarea.ConClaude(Enumerable.Range(1, n).Select(k => contexto.Peticion(definicion,
                ConstructorArgumentos.Construir(definicion, Esquema, valores, definicion.PresupuestoParte(n)), $"Prompt de prueba {k}",
                definicion.TiempoParte(n), $"{k} de {n}")).ToList());
        }
        return PreparacionTarea.ConClaude(contexto.Peticion(Definicion, ConstructorArgumentos.Construir(Definicion, Esquema, valores), "Prompt de prueba"));
    }

    private const string Esquema = """{"type":"object"}""";

    /// <summary>Con una parte, como <see cref="AplicarAsync"/>; con varias, junta las salidas de las que han salido bien.</summary>
    public async Task<ResultadoTarea> AplicarPartesAsync(EjecucionAgente ejecucion,
        IReadOnlyList<(PeticionClaude Peticion, RespuestaClaude Respuesta)> partes, CancellationToken cancelacion)
    {
        PartesRecibidas = partes;
        if (partes.Count == 1) return await AplicarAsync(ejecucion, partes[0].Respuesta, cancelacion);
        var buenas = partes.Where(p => p.Respuesta.Correcta).ToList();
        return new ResultadoTarea($"Hecho en {buenas.Count} de {partes.Count} partes.",
            $"[{string.Join(",", buenas.Select(p => p.Respuesta.SalidaEstructurada?.GetRawText()))}]");
    }

    public async Task<ResultadoTarea> AplicarAsync(EjecucionAgente ejecucion, RespuestaClaude respuesta, CancellationToken cancelacion)
    {
        if (AlAplicar is { } alAplicar) await alAplicar(respuesta);
        return new ResultadoTarea("Hecho por la tarea de prueba.", respuesta.SalidaEstructurada?.GetRawText());
    }
}

/// <summary>
/// <see cref="ColaEjecuciones"/>, <see cref="ProcesadorEjecuciones"/> y <see cref="BloqueoCompartido"/> con el ejecutor
/// falso, una BD SQLite temporal y una carpeta Curriculum temporal. Nunca se lanza Claude.
/// </summary>
[Collection(nameof(ColeccionManejadorDePrueba))]
public sealed class ProcesadorEjecucionesTests : IAsyncLifetime
{
    /// <summary>
    /// «Ahora» de estos tests: 18/11/2026 a las 12:00 de Madrid, lejos de medianoche y de los cambios de hora. Como no es
    /// la hora real, los bloqueos que escribe el test llevan esta fecha de modificación y el inicio de los procesos sale
    /// de <see cref="InicioDelProceso"/>, no del sistema. Ojo: un bloqueo que coge el propio código con
    /// <c>_bloqueo.Intentar</c> lleva la fecha real, posterior a esta, y otro lo daría por caducado: si un test necesita
    /// que se vea ocupado, ponle <c>File.SetLastWriteTimeUtc(RutaBloqueo, _reloj.AhoraUtc)</c>.
    /// </summary>
    private static readonly DateTime Ahora = new(2026, 11, 18, 11, 0, 0, DateTimeKind.Utc);

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), $"radar-cola-{Guid.NewGuid():N}");
    private readonly RelojFijo _reloj = new(Ahora);
    private readonly TestDbContextFactory _factory;
    private readonly RutasCurriculum _rutas;
    private readonly EjecutorClaudeFalso _falso;
    private readonly ColaEjecuciones _cola;
    private readonly BloqueoCompartido _bloqueo;
    private readonly ProcesadorEjecuciones _procesador;
    private readonly ServiceProvider _servicios;
    private OpcionesEjecutor _opciones;
    private readonly int _candidatura;
    private readonly int _otraCandidatura;

    public ProcesadorEjecucionesTests()
    {
        var carpeta = Path.Combine(_raiz, "Curriculum");
        FabricaApi.CopiarDatosEjemplo(carpeta);
        _rutas = new RutasCurriculum(carpeta);

        var opcionesBd = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_raiz, "radar.db") }.ToString()).Options;
        using (var db = new AppDbContext(opcionesBd))
        {
            db.Database.EnsureCreated();
            var empresa = new Empresa { Nombre = "Acme", NombreNormalizado = "acme" };
            var una = Candidatura.Nueva(empresa, "Desarrollador .NET", _reloj.AhoraUtc, Origen.Manual);
            var otra = Candidatura.Nueva(empresa, "Programador Angular", _reloj.AhoraUtc, Origen.Manual);
            db.Candidaturas.AddRange(una, otra);
            db.SaveChanges();
            _candidatura = una.Id;
            _otraCandidatura = otra.Id;
        }
        _factory = new TestDbContextFactory(opcionesBd);

        _opciones = new OpcionesEjecutor("claude.exe", [], Path.Combine(_raiz, "datos"), 15m, "2.1.185")
        {
            IntervaloSondeo = TimeSpan.FromMilliseconds(20)
        };
        _falso = new EjecutorClaudeFalso(_reloj);
        _servicios = new ServiceCollection()
            .AddScoped<IManejadorTarea>(_ => new ManejadorDePrueba(ManejadorDePrueba.SinCandidatura))
            .AddScoped<IManejadorTarea>(_ => new ManejadorDePrueba(ManejadorDePrueba.ConCandidatura))
            .BuildServiceProvider();
        ManejadorDePrueba.Limpiar();

        // Sin ruta de exclusión: esta «API» lleva siempre la cola (los tests de dos APIs crean sus propias guardias).
        _guardia = new GuardiaCola(_opciones);
        _cola = Cola(_opciones, _guardia);
        _bloqueo = new BloqueoCompartido(_rutas, NullLogger<BloqueoCompartido>.Instance, InicioDelProceso);
        _procesador = Procesador(_cola);
    }

    private readonly GuardiaCola _guardia;

    private ColaEjecuciones Cola(OpcionesEjecutor opciones, GuardiaCola guardia, RutasCurriculum? rutas = null) =>
        new(_factory, _reloj, rutas ?? _rutas, opciones, _servicios.GetRequiredService<IServiceScopeFactory>(), guardia);

    private ProcesadorEjecuciones Procesador(ColaEjecuciones cola, GuardiaCola? guardia = null, IEjecutorClaude? ejecutor = null) =>
        new(_factory, _reloj, _rutas, _opciones, cola, _bloqueo, ejecutor ?? _falso, new LectorCurriculum(_rutas),
            _servicios.GetRequiredService<IServiceScopeFactory>(), guardia ?? _guardia, NullLogger<ProcesadorEjecuciones>.Instance);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _procesador.StopAsync(CancellationToken.None);
        _procesador.Dispose();
        await _servicios.DisposeAsync();
        ManejadorDePrueba.Limpiar();
        _guardia.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { /* temporal */ }
    }

    private string RutaBloqueo => _bloqueo.Ruta!;
    private string Registro => _rutas.Registro!;

    private async Task<EjecucionAgente> EncolarAsync(TareaAgente tarea = TareaAgente.ExtraerRegistro, int? candidaturaId = null)
    {
        var encolada = await _cola.EncolarAsync(tarea, candidaturaId);
        Assert.NotNull(encolada);
        return encolada.Value.Ejecucion;
    }

    private async Task<EjecucionAgente> LeerAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.EjecucionesAgente.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    /// <summary>
    /// Espera a que el procesador, que trabaja en otra tarea, deje la ejecución como diga la condición: solo lo cuenta en
    /// la BD, así que se lee hasta que se cumple.
    /// </summary>
    private Task<EjecucionAgente> EsperarAsync(int id, Func<EjecucionAgente, bool> condicion) =>
        Esperar.HastaAsync(() => LeerAsync(id), condicion, e => $"La ejecución {id} sigue en {e.Estado} (espera: {e.Espera}).");

    /// <summary>
    /// El inicio de los procesos para el bloqueo, sin preguntar al sistema (su hora no es la del reloj fijo): esta API
    /// empezó el día anterior y <see cref="EjecutorClaudeFalso.PidFalso"/> no existe.
    /// </summary>
    private static DateTime? InicioDelProceso(int pid) => pid == Environment.ProcessId ? Ahora.AddDays(-1) : null;

    /// <summary>¿Lo tiene abierto alguien que no comparte la escritura (como el bloqueo mientras está cogido)?</summary>
    private static bool SigueAbiertoParaEscribir(string ruta)
    {
        try
        {
            using var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private async Task GuardarAsync(EjecucionAgente ejecucion)
    {
        await using var db = _factory.CreateDbContext();
        db.EjecucionesAgente.Add(ejecucion);
        await db.SaveChangesAsync();
    }

    private void EscribirBloqueo(string quien, int? pid, DateTime caduca, DateTime? inicio = null) =>
        EscribirBloqueo(new BloqueoLeido(1, quien, "Rastreo", null, pid, inicio ?? _reloj.AhoraUtc, caduca, true));

    /// <summary>Lo deja como otro programa, con la hora del reloj como fecha de modificación (si no, sería la real).</summary>
    private void EscribirBloqueo(BloqueoLeido bloqueo)
    {
        Directory.CreateDirectory(_rutas.CarpetaRadar!);
        File.WriteAllText(RutaBloqueo, BloqueoEjecucion.Serializar(bloqueo));
        File.SetLastWriteTimeUtc(RutaBloqueo, _reloj.AhoraUtc);
    }

    [Fact]
    public async Task UnaEjecucion_PasaPorEnColaEnCursoYOk()
    {
        var ejecucion = await EncolarAsync();
        Assert.Equal(EstadoEjecucion.EnCola, ejecucion.Estado);
        _falso.Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(Salida: JsonSerializer.SerializeToElement(new { ok = true }), CosteUsd: 0.18m, Turnos: 4));

        EjecucionAgente? durante = null;
        _falso.AntesDeResponder = async (_, _) => durante = await LeerAsync(ejecucion.Id);
        Assert.True(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));

        Assert.Equal(EstadoEjecucion.EnCurso, durante!.Estado);
        Assert.Equal(EjecutorClaudeFalso.PidFalso, durante.Pid);
        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Ok, final.Estado);
        Assert.Equal(0.18m, final.CosteUsd);
        Assert.Equal(4, final.Turnos);
        Assert.Equal("Hecho por la tarea de prueba.", final.Resumen);
        Assert.Equal("""{"ok":true}""", final.ResultadoJson);
        Assert.Equal($"ejecucion-{ejecucion.Id}-{ejecucion.FechaAlta:yyyyMMdd-HHmmss}.log", final.RutaLog);
        Assert.NotNull(final.HashRegistroAntes);
        Assert.Equal(final.HashRegistroAntes, final.HashRegistroDespues);
        Assert.Equal(final.HashPerfilAntes, final.HashPerfilDespues);
        Assert.False(final.RegistroCambiado);
        Assert.False(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Encolar_SoloEsperaTurnoSiHayOtraDelante()
    {
        var primera = await EncolarAsync();
        Assert.Null((await LeerAsync(primera.Id)).Espera);

        var segunda = await EncolarAsync(TareaAgente.AnalizarEncaje, _candidatura);
        var leida = await LeerAsync(segunda.Id);
        Assert.Equal(MotivoEspera.Turno, leida.Espera);
        Assert.Null(leida.QuienBloquea);

        // Con la de delante ya terminada, la siguiente vuelve a no tener motivo de espera.
        Assert.True(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));
        Assert.True(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));
        var tercera = await EncolarAsync();
        Assert.Null((await LeerAsync(tercera.Id)).Espera);
    }

    [Fact]
    public async Task LaTareaSinCarpetaDeCurriculum_TrabajaEnSuCarpetaDeEjecucion()
    {
        var ejecucion = await EncolarAsync();
        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var peticion = Assert.Single(_falso.Peticiones);
        Assert.Equal(Path.Combine(_opciones.CarpetaEjecuciones, ejecucion.Id.ToString()), peticion.CarpetaTrabajo);
        Assert.Equal(Path.Combine(_opciones.CarpetaLogs, $"ejecucion-{ejecucion.Id}-{ejecucion.FechaAlta:yyyyMMdd-HHmmss}.log"), peticion.RutaLog);
        Assert.Equal("Prompt de prueba", peticion.EntradaEstandar);
    }

    [Fact]
    public async Task MientrasCorre_ElBloqueoExisteConSuJsonYSeBorraAlAcabar()
    {
        var ejecucion = await EncolarAsync();
        BloqueoLeido? durante = null;
        _falso.AntesDeResponder = (_, _) =>
        {
            using var flujo = new FileStream(RutaBloqueo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            durante = BloqueoEjecucion.Leer(new StreamReader(flujo).ReadToEnd());
            return Task.CompletedTask;
        };

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.NotNull(durante);
        Assert.Equal(BloqueoEjecucion.QuienRadar, durante.Quien);
        Assert.Equal("ExtraerRegistro", durante.Tarea);
        Assert.Equal(ejecucion.Id, durante.EjecucionId);
        Assert.Equal(Environment.ProcessId, durante.Pid);
        Assert.False(durante.UsaChrome);
        Assert.Equal(TimeSpan.FromMinutes(10), durante.CaducaUtc - durante.InicioUtc);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task SiFalla_ElBloqueoTambienSeBorra()
    {
        var ejecucion = await EncolarAsync();
        _falso.Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(ModoFalso.PresupuestoAgotado, CosteUsd: 0.52m));

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal(MensajesEjecucion.TopeTarea, final.Error);
        Assert.Equal(0.52m, final.CosteUsd);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task TerminadaSinClaude_QuedaOkConCosteCeroSinLlamarAlEjecutorYConElBloqueoLibre()
    {
        var ejecucion = await EncolarAsync();
        var bloqueoAlPreparar = false;
        ManejadorDePrueba.AlConstruir = _ =>
        {
            bloqueoAlPreparar = File.Exists(RutaBloqueo);
            return Task.CompletedTask;
        };
        ManejadorDePrueba.SinClaude = new ResultadoTarea("No hacía falta Claude.", """{"sinClaude":true}""");

        Assert.True(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Ok, final.Estado);
        Assert.Equal(0m, final.CosteUsd);
        Assert.Equal(0m, ReglasGasto.GastoComputable(final));
        Assert.Null(final.Turnos);
        Assert.Null(final.SesionClaude);
        Assert.Equal("No hacía falta Claude.", final.Resumen);
        Assert.Equal("""{"sinClaude":true}""", final.ResultadoJson);
        Assert.NotNull(final.HashRegistroDespues);
        Assert.False(final.RegistroCambiado);
        Assert.Empty(_falso.Peticiones);
        Assert.True(bloqueoAlPreparar);
        Assert.False(File.Exists(RutaBloqueo));

        // El bloqueo está libre de verdad: la siguiente lo coge y llega a Claude.
        ManejadorDePrueba.SinClaude = null;
        var siguiente = await EncolarAsync();
        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);
        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(siguiente.Id)).Estado);
        Assert.Single(_falso.Peticiones);
    }

    [Theory]
    [InlineData(ModoFalso.Error, MensajesEjecucion.ErrorDeClaude)]
    [InlineData(ModoFalso.SalidaNoValida, MensajesEjecucion.SalidaNoValida)]
    public async Task UnFalloDeClaude_QuedaErrorConSuMensaje(ModoFalso modo, string mensaje)
    {
        var ejecucion = await EncolarAsync();
        _falso.Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(modo));

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(mensaje, (await LeerAsync(ejecucion.Id)).Error);
    }

    [Fact]
    public async Task TiempoAgotado_QuedaErrorConElTiempoDeLaTarea()
    {
        var ejecucion = await EncolarAsync();
        _falso.Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(ModoFalso.NoResponde));
        // El falso respeta el tiempo máximo de la petición: se acorta para el test.
        var corto = new PeticionAcortada(_falso);
        var procesador = Procesador(_cola, ejecutor: corto);

        await procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal("Ha tardado más de 5 min y la he parado.", final.Error);
        Assert.Null(final.CosteUsd);
    }

    [Fact]
    public async Task ElFalsoQueTocaElRegistro_DaRegistroCambiado()
    {
        var ejecucion = await EncolarAsync();
        // Lo toca el test (como lo haría Claude o Eduardo): la app nunca escribe el registro.
        _falso.AntesDeResponder = (_, _) => File.AppendAllTextAsync(Registro, "\n- Línea añadida mientras trabajaba.\n");

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Ok, final.Estado);
        Assert.True(final.RegistroCambiado);
        Assert.False(final.PerfilCambiado);
    }

    [Fact]
    public async Task Reintentar_UnaQueSalioBien_DiceQueYaTermino()
    {
        var ejecucion = await EncolarAsync();
        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => _cola.ReintentarAsync(ejecucion.Id));
        Assert.Equal(ColaEjecuciones.MensajeYaTerminoBien, error.Message);
    }

    [Fact]
    public async Task ElFalsoQueBorraElRegistro_DaRegistroCambiado()
    {
        var ejecucion = await EncolarAsync();
        _falso.AntesDeResponder = (_, _) =>
        {
            File.Delete(Registro);
            return Task.CompletedTask;
        };

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(ILectorCurriculum.HashAusente, final.HashRegistroDespues);
        Assert.True(final.RegistroCambiado);
    }

    [Fact]
    public async Task ElFalsoQueRenombraElPerfil_DaPerfilCambiado()
    {
        var ejecucion = await EncolarAsync();
        _falso.AntesDeResponder = (_, _) =>
        {
            File.Move(_rutas.Perfil!, _rutas.Perfil + ".bak");
            return Task.CompletedTask;
        };

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.True((await LeerAsync(ejecucion.Id)).PerfilCambiado);
    }

    /// <summary>Dos copias de la API sobre la misma BD: cada una con su guardia, su cola y su procesador.</summary>
    private (GuardiaCola Guardia, ColaEjecuciones Cola, ProcesadorEjecuciones Procesador) OtraApi()
    {
        var opciones = _opciones with { RutaExclusion = Path.Combine(_raiz, "radar.db") };
        var guardia = new GuardiaCola(opciones);
        var cola = Cola(opciones, guardia);
        return (guardia, cola, Procesador(cola, guardia));
    }

    [Fact]
    public async Task OtraApiSobreLaMismaBd_NoRecuperaNiLanzaNiEncola()
    {
        var (guardiaA, colaA, primera) = OtraApi();
        var (guardiaB, colaB, segunda) = OtraApi();
        try
        {
            Assert.True(await primera.RecuperarAsync());
            // La primera está trabajando: una en curso y otra en cola.
            var enCurso = EjecucionAgente.Nueva(TareaAgente.ExtraerRegistro, "{}", 1m, 600, null, _reloj.AhoraUtc);
            enCurso.Empezar(_reloj.AhoraUtc, "aaa", "ppp");
            enCurso.AnotarProceso(EjecutorClaudeFalso.PidFalso, _reloj.AhoraUtc);
            await GuardarAsync(enCurso);
            var enCola = (await colaA.EncolarAsync(TareaAgente.AnalizarEncaje, _candidatura))!.Value.Ejecucion;

            // La segunda no toca nada, no encola (ni reserva presupuesto) y lo dice en el estado.
            Assert.False(await segunda.RecuperarAsync());
            Assert.False(await segunda.ProcesarSiguienteAsync(CancellationToken.None));
            Assert.Equal(EstadoEjecucion.EnCurso, (await LeerAsync(enCurso.Id)).Estado);
            Assert.Equal(EstadoEjecucion.EnCola, (await LeerAsync(enCola.Id)).Estado);
            Assert.Empty(_falso.Peticiones);
            var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => colaB.EncolarAsync(TareaAgente.ExtraerRegistro));
            Assert.Equal(MensajesEjecucion.OtraCopiaLlevaLaCola, error.Message);
            await using (var db = _factory.CreateDbContext())
                Assert.Equal(2, await db.EjecucionesAgente.CountAsync());
            var estado = await new AgentesService(_factory, _reloj, colaB, _falso, _opciones, guardiaB).EstadoAsync(true, null);
            Assert.False(estado.LlevaLaCola);
            Assert.Equal(MensajesEjecucion.OtraCopiaLlevaLaCola, estado.MotivoCola);
            Assert.True((await new AgentesService(_factory, _reloj, colaA, _falso, _opciones, guardiaA).EstadoAsync(true, null)).LlevaLaCola);

            // Cuando la primera se cierra, la segunda la coge y recupera.
            guardiaA.Dispose();
            Assert.True(await segunda.RecuperarAsync());
            Assert.Equal(EstadoEjecucion.Error, (await LeerAsync(enCurso.Id)).Estado);
        }
        finally
        {
            primera.Dispose();
            segunda.Dispose();
            guardiaA.Dispose();
            guardiaB.Dispose();
        }
    }

    [Fact]
    public async Task CancelarDesdeLaOtraApi_SoloLaMarcaYLaDuenaLaCorta()
    {
        var (guardiaA, colaA, primera) = OtraApi();
        var (guardiaB, colaB, segunda) = OtraApi();
        try
        {
            Assert.True(await primera.RecuperarAsync());
            Assert.False(await segunda.RecuperarAsync());
            _falso.Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(ModoFalso.NoResponde, CosteUsd: 0.3m));
            var ejecucion = (await colaA.EncolarAsync(TareaAgente.ExtraerRegistro))!.Value.Ejecucion;
            var procesando = primera.ProcesarSiguienteAsync(CancellationToken.None);
            await EsperarAsync(ejecucion.Id, e => e.Estado == EstadoEjecucion.EnCurso && e.Pid is not null);

            // La segunda no lleva la cola: no la puede dar por cancelada (la primera sigue gastando), solo marcarla.
            var marcada = await colaB.CancelarAsync(ejecucion.Id);
            Assert.Equal(EstadoEjecucion.EnCurso, marcada!.Estado);
            Assert.True(marcada.CancelacionPedida);

            // La primera lo ve en su vigilancia, corta el proceso y la deja Cancelada.
            await procesando;
            var final = await LeerAsync(ejecucion.Id);
            Assert.Equal(EstadoEjecucion.Cancelada, final.Estado);
            Assert.Equal(1, _falso.Canceladas);
            Assert.False(File.Exists(RutaBloqueo));
        }
        finally
        {
            primera.Dispose();
            segunda.Dispose();
            guardiaA.Dispose();
            guardiaB.Dispose();
        }
    }

    [Fact]
    public void Guardia_ConUnaCarpetaEnLugarDelArchivoDeControl_DiceElMotivo()
    {
        var bd = Path.Combine(_raiz, "otra.db");
        Directory.CreateDirectory(bd + ExclusionBaseDatos.Sufijo);
        using var guardia = new GuardiaCola(_opciones with { RutaExclusion = bd });

        Assert.False(guardia.Intentar());
        Assert.False(guardia.LlevaLaCola);
        Assert.Equal(MensajesEjecucion.SinExclusion("en su lugar hay una carpeta"), guardia.Motivo);
    }

    [Fact]
    public void Guardia_ConElArchivoDeControlDeSoloLectura_DiceElMotivo()
    {
        var bd = Path.Combine(_raiz, "solo-lectura.db");
        var control = bd + ExclusionBaseDatos.Sufijo;
        File.WriteAllText(control, "");
        File.SetAttributes(control, FileAttributes.ReadOnly);
        try
        {
            using var guardia = new GuardiaCola(_opciones with { RutaExclusion = bd });
            Assert.False(guardia.Intentar());
            Assert.StartsWith("No puedo usar los agentes:", guardia.Motivo);
        }
        finally
        {
            File.SetAttributes(control, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task CancelarJustoAlEmpezar_QuedaCanceladaYNoError()
    {
        var ejecucion = await EncolarAsync();
        // Se cancela mientras se monta la petición: el corte salta dentro del manejador.
        ManejadorDePrueba.AlConstruir = async corte =>
        {
            await _cola.CancelarAsync(ejecucion.Id);
            corte.ThrowIfCancellationRequested();
        };

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Cancelada, final.Estado);
        Assert.Null(final.Error);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task CancelarMientrasSePreparaUnaSinClaude_QuedaCanceladaConCosteCero()
    {
        var ejecucion = await EncolarAsync();
        ManejadorDePrueba.AlConstruir = async _ => await _cola.CancelarAsync(ejecucion.Id);
        ManejadorDePrueba.SinClaude = new ResultadoTarea("No hacía falta Claude.", null);

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Cancelada, final.Estado);
        Assert.Equal(0m, ReglasGasto.GastoComputable(final));
        Assert.Empty(_falso.Peticiones);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task CancelarMientrasSePreparaAntesDeLanzarClaude_QuedaCanceladaSinGastoEnElDia()
    {
        var ejecucion = await EncolarAsync();
        // Como una descarga que espera hasta que la cortan.
        ManejadorDePrueba.AlConstruir = async corte =>
        {
            await _cola.CancelarAsync(ejecucion.Id);
            await Task.Delay(Timeout.Infinite, corte);
        };

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Cancelada, final.Estado);
        Assert.Equal(0m, final.CosteUsd);
        Assert.Empty(_falso.Peticiones);
        await using var db = _factory.CreateDbContext();
        Assert.Equal(0m, (await ColaEjecuciones.LeerGastoAsync(db, _reloj.AhoraUtc)).Dia.Gastado);
    }

    [Fact]
    public async Task UnaExcepcionAlPrepararla_QuedaErrorConCosteCero()
    {
        var ejecucion = await EncolarAsync();
        ManejadorDePrueba.AlConstruir = _ => throw new InvalidOperationException("Fallo interno");

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal(MensajesEjecucion.Inesperado, final.Error);
        Assert.Equal(0m, final.CosteUsd);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task PararLaApiMientrasSePrepara_QuedaInterrumpidaConCosteCero()
    {
        var ejecucion = await EncolarAsync();
        using var parada = new CancellationTokenSource();
        ManejadorDePrueba.AlConstruir = async corte =>
        {
            await parada.CancelAsync();
            await Task.Delay(Timeout.Infinite, corte);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _procesador.ProcesarSiguienteAsync(parada.Token));

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal(0m, final.CosteUsd);
    }

    [Fact]
    public async Task CanceladaEnLaBdMientrasSePreparaUnaSinClaude_QuedaCanceladaAunqueElCorteNoHayaSaltado()
    {
        var ejecucion = await EncolarAsync();
        // Como «Cancelar» desde otra copia de la API: solo marca la BD y la vigilancia aún no ha cortado.
        ManejadorDePrueba.AlConstruir = async _ =>
        {
            await using var db = _factory.CreateDbContext();
            await db.EjecucionesAgente.Where(e => e.Id == ejecucion.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.CancelacionPedida, true));
        };
        ManejadorDePrueba.SinClaude = new ResultadoTarea("No hacía falta Claude.", null);

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Cancelada, final.Estado);
        Assert.Equal(0m, final.CosteUsd);
        Assert.Empty(_falso.Peticiones);
    }

    [Fact]
    public async Task UnaReglaDeNegocioAlPreparar_FallaSinContarGasto()
    {
        var ejecucion = await EncolarAsync();
        ManejadorDePrueba.AlConstruir = _ => throw new ReglaNegocioException("La candidatura ya no existe.");

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal((EstadoEjecucion.Error, "La candidatura ya no existe."), (final.Estado, final.Error));
        Assert.Equal(0m, ReglasGasto.GastoComputable(final));
        Assert.Empty(_falso.Peticiones);
    }

    [Fact]
    public async Task CancelarUnaEnCursoHuerfanaEnLaApiQueLlevaLaCola_QuedaCanceladaAlMomento()
    {
        // En curso, pero nadie la procesa aquí (no hay corte) y esta API lleva la cola: nadie la va a terminar.
        var huerfana = EjecucionAgente.Nueva(TareaAgente.ExtraerRegistro, "{}", 1m, 600, null, _reloj.AhoraUtc);
        huerfana.Empezar(_reloj.AhoraUtc, null, null);
        await GuardarAsync(huerfana);

        var cancelada = await _cola.CancelarAsync(huerfana.Id);

        Assert.Equal(EstadoEjecucion.Cancelada, cancelada!.Estado);
        Assert.Equal(EstadoEjecucion.Cancelada, (await LeerAsync(huerfana.Id)).Estado);
    }

    [Fact]
    public async Task UnBloqueoPropioDeOtraEjecucion_SeLimpiaYSeCoge()
    {
        // Lo dejó esta misma API (mismo pid, vivo) para una ejecución anterior y no se pudo borrar.
        EscribirBloqueo(BloqueoEjecucion.DeLaApp(TareaAgente.AnalizarEncaje, 9999, Environment.ProcessId, _reloj.AhoraUtc,
            TimeSpan.FromMinutes(5)));
        var ejecucion = await EncolarAsync();

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(ejecucion.Id)).Estado);
    }

    [Fact]
    public async Task UnaCarpetaEnLugarDelBloqueo_CuentaComoOcupadoYNoComoError()
    {
        Directory.CreateDirectory(RutaBloqueo);
        var ejecucion = await EncolarAsync();

        var procesando = _procesador.ProcesarSiguienteAsync(CancellationToken.None);
        var esperando = await EsperarAsync(ejecucion.Id, e => e.Espera == MotivoEspera.BloqueoOcupado);
        Assert.Equal(BloqueoEjecucion.NombreOtroPrograma, esperando.QuienBloquea);

        Directory.Delete(RutaBloqueo);
        await procesando;
        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(ejecucion.Id)).Estado);
    }

    [Fact]
    public async Task UnBloqueoQueNoSeDejaLeer_CuentaComoOcupado()
    {
        EscribirBloqueo(BloqueoEjecucion.QuienTareaLunes, Environment.ProcessId, _reloj.AhoraUtc.AddHours(1));
        var ejecucion = await EncolarAsync();
        Task procesando;
        // Abierto en exclusiva por otro: ni se lee ni se borra.
        await using (new FileStream(RutaBloqueo, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            procesando = _procesador.ProcesarSiguienteAsync(CancellationToken.None);
            await EsperarAsync(ejecucion.Id, e => e.Espera == MotivoEspera.BloqueoOcupado);
        }
        File.Delete(RutaBloqueo);
        await procesando;
        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(ejecucion.Id)).Estado);
    }

    [Fact]
    public async Task AlSoltar_ReintentaElBorradoSiOtroLoEstaLeyendo()
    {
        var tomado = _bloqueo.Intentar(BloqueoEjecucion.DeLaApp(TareaAgente.ExtraerRegistro, 1, Environment.ProcessId, _reloj.AhoraUtc,
            TimeSpan.FromMinutes(5)), _reloj.AhoraUtc);
        Assert.True(tomado.Cogido);
        // Un lector que no comparte el borrado (como Get-Content en la tarea de los lunes), durante un momento.
        var lector = new FileStream(RutaBloqueo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var soltando = Task.Run(tomado.Dispose);
        // Soltar cierra su archivo y enseguida intenta borrarlo, que en Windows falla mientras el lector siga abierto. El
        // lector se cierra en cuanto se ve cerrado el del bloqueo (ya se puede abrir para escribir), sin esperar un tiempo
        // fijo: el borrado llega en uno de los reintentos, que son pausas reales del propio Dispose (hasta 5
        // intentos, con 100 ms entre uno y otro). En la práctica el primer borrado falla (el propio sondeo abre el archivo
        // sin compartir el borrado), pero no está garantizado al 100 % que se use un reintento.
        await Esperar.HastaAsync(() => !SigueAbiertoParaEscribir(RutaBloqueo), "El bloqueo no se ha cerrado al soltarlo.");
        await lector.DisposeAsync();
        await soltando;

        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task UnBloqueoCaducadoDeSoloLectura_SeBorraYSeCoge()
    {
        EscribirBloqueo(BloqueoEjecucion.QuienTareaLunes, EjecutorClaudeFalso.PidFalso, _reloj.AhoraUtc.AddHours(1));
        File.SetAttributes(RutaBloqueo, FileAttributes.ReadOnly);
        var ejecucion = await EncolarAsync();

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(ejecucion.Id)).Estado);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task UnBloqueoConLaFechaDeModificacionEnElFuturo_SeDaPorCaducado()
    {
        EscribirBloqueo(BloqueoEjecucion.QuienTareaLunes, Environment.ProcessId, _reloj.AhoraUtc.AddHours(1));
        File.SetLastWriteTimeUtc(RutaBloqueo, _reloj.AhoraUtc.AddDays(3));
        var ejecucion = await EncolarAsync();

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(ejecucion.Id)).Estado);
    }

    [Fact]
    public async Task UnQuienRaro_SeEnsenaComoOtroPrograma()
    {
        EscribirBloqueo(new BloqueoLeido(1, "<b>Hola</b>\u202E", null, null, Environment.ProcessId, _reloj.AhoraUtc,
            _reloj.AhoraUtc.AddHours(1), false));
        var ejecucion = await EncolarAsync();

        var procesando = _procesador.ProcesarSiguienteAsync(CancellationToken.None);
        var esperando = await EsperarAsync(ejecucion.Id, e => e.Espera == MotivoEspera.BloqueoOcupado);
        Assert.Equal(BloqueoEjecucion.NombreOtroPrograma, esperando.QuienBloquea);
        File.Delete(RutaBloqueo);
        await procesando;
    }

    [Fact]
    public void UnBloqueoDeOtroPrograma_SeApuntaEnElLogUnaVezPorBloqueo()
    {
        var log = new LogEnMemoria();
        var bloqueo = new BloqueoCompartido(_rutas, log, InicioDelProceso);
        var contenido = BloqueoEjecucion.DeLaApp(TareaAgente.ExtraerRegistro, 1, Environment.ProcessId, _reloj.AhoraUtc, TimeSpan.FromMinutes(5));
        EscribirBloqueo("<b>Raro</b>", Environment.ProcessId, _reloj.AhoraUtc.AddHours(1));

        for (var sondeo = 0; sondeo < 3; sondeo++)
            Assert.Equal(BloqueoEjecucion.NombreOtroPrograma, bloqueo.Intentar(contenido, _reloj.AhoraUtc).OcupadoPor);
        var aviso = Assert.Single(log.Mensajes);
        Assert.Contains("bRaro/b", aviso);

        // Otro bloqueo distinto (otra caducidad) se vuelve a apuntar, también una vez.
        EscribirBloqueo("<b>Raro</b>", Environment.ProcessId, _reloj.AhoraUtc.AddHours(2));
        bloqueo.Intentar(contenido, _reloj.AhoraUtc);
        bloqueo.Intentar(contenido, _reloj.AhoraUtc);
        Assert.Equal(2, log.Mensajes.Count);

        // Los conocidos no se apuntan: ya se enseñan con su nombre.
        EscribirBloqueo(BloqueoEjecucion.QuienTareaLunes, Environment.ProcessId, _reloj.AhoraUtc.AddHours(1));
        bloqueo.Intentar(contenido, _reloj.AhoraUtc);
        Assert.Equal(2, log.Mensajes.Count);
    }

    /// <summary>Log que guarda los mensajes de nivel Information o más.</summary>
    private sealed class LogEnMemoria : Microsoft.Extensions.Logging.ILogger<BloqueoCompartido>
    {
        public List<string> Mensajes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Information) Mensajes.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task DosEncoladas_VanDeUnaEnUnaYPorOrden()
    {
        // Arranca antes de encolar: al arrancar, lo que ya estaba en cola se cancela.
        await _procesador.StartAsync(CancellationToken.None);
        EjecucionAgente? segundaDurante = null;
        var segundaId = 0;
        // La primera no responde hasta que la segunda está en la cola (una señal, no una pausa).
        var segundaEncolada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _falso.AntesDeResponder = async (p, corte) =>
        {
            if (p.Tarea != TareaAgente.ExtraerRegistro) return;
            await segundaEncolada.Task.WaitAsync(corte);
            segundaDurante = await LeerAsync(segundaId);
        };
        var primera = await EncolarAsync(TareaAgente.ExtraerRegistro);
        var segunda = await EncolarAsync(TareaAgente.AnalizarEncaje, _candidatura);
        segundaId = segunda.Id;
        segundaEncolada.SetResult();

        await EsperarAsync(segunda.Id, e => e.EsTerminal);

        Assert.Equal(EstadoEjecucion.EnCola, segundaDurante!.Estado);
        Assert.Equal(MotivoEspera.Turno, segundaDurante.Espera);
        Assert.Equal([TareaAgente.ExtraerRegistro, TareaAgente.AnalizarEncaje], _falso.Peticiones.Select(p => p.Tarea));
        var a = await LeerAsync(primera.Id);
        var b = await LeerAsync(segunda.Id);
        Assert.Equal(EstadoEjecucion.Ok, a.Estado);
        Assert.Equal(EstadoEjecucion.Ok, b.Estado);
    }

    [Fact]
    public async Task EncolarDosVecesLoMismo_DevuelveLaMisma()
    {
        var primera = await _cola.EncolarAsync(TareaAgente.AnalizarEncaje, _candidatura);
        var segunda = await _cola.EncolarAsync(TareaAgente.AnalizarEncaje, _candidatura);
        var otraCandidatura = await _cola.EncolarAsync(TareaAgente.AnalizarEncaje, _otraCandidatura);

        Assert.True(primera!.Value.Nueva);
        Assert.False(segunda!.Value.Nueva);
        Assert.Equal(primera.Value.Ejecucion.Id, segunda.Value.Ejecucion.Id);
        Assert.NotEqual(primera.Value.Ejecucion.Id, otraCandidatura!.Value.Ejecucion.Id);
    }

    [Fact]
    public async Task Encolar_LoQueNoExiste_DaNull()
    {
        Assert.Null(await _cola.EncolarAsync(TareaAgente.AnalizarEncaje, -1));
    }

    [Fact]
    public async Task Encolar_UnaTareaSinManejador_Lanza()
    {
        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => _cola.EncolarAsync(TareaAgente.Redactar));
        Assert.Equal(MensajesEjecucion.SinManejador, error.Message);
    }

    [Fact]
    public async Task Encolar_SinCarpetaCurriculum_Lanza()
    {
        var cola = Cola(_opciones, _guardia, new RutasCurriculum(Path.Combine(_raiz, "no-existe")));
        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => cola.EncolarAsync(TareaAgente.ExtraerRegistro));
        Assert.Equal(RutasCurriculum.MensajeSinCarpeta, error.Message);
    }

    [Fact]
    public async Task TopeDiario_RechazaAlEncolarContandoLoQueEstaEnCola()
    {
        _opciones = _opciones with { TopeGastoDiarioUsd = 0.6m };
        var cola = Cola(_opciones, _guardia);
        await cola.EncolarAsync(TareaAgente.ExtraerRegistro);

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => cola.EncolarAsync(TareaAgente.AnalizarEncaje, _candidatura));
        Assert.Equal(MensajesEjecucion.TopeDiario, error.Message);
    }

    [Fact]
    public async Task TopeDiario_RechazaAlEmpezarSiYaNoCabe()
    {
        var ejecucion = await EncolarAsync();
        // Mientras esperaba, otra ejecución de hoy ha gastado casi todo el tope.
        var gastada = EjecucionAgente.Nueva(TareaAgente.Rastreo, "{}", 3m, 60, null, _reloj.AhoraUtc);
        gastada.Empezar(_reloj.AhoraUtc, null, null);
        gastada.Terminar(_reloj.AhoraUtc, 14.8m, 1, null, "x", null, null, null);
        await GuardarAsync(gastada);

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal(MensajesEjecucion.TopeDiario, final.Error);
        Assert.Empty(_falso.Peticiones);
    }

    [Fact]
    public async Task BloqueoDeLaTareaDeLosLunes_EsperaYEmpiezaCuandoSeLibera()
    {
        EscribirBloqueo(BloqueoEjecucion.QuienTareaLunes, Environment.ProcessId, _reloj.AhoraUtc.AddHours(1));
        var ejecucion = await EncolarAsync();

        var procesando = _procesador.ProcesarSiguienteAsync(CancellationToken.None);
        var esperando = await EsperarAsync(ejecucion.Id, e => e.Espera == MotivoEspera.BloqueoOcupado);
        Assert.Equal(EstadoEjecucion.EnCola, esperando.Estado);
        Assert.Equal(BloqueoEjecucion.NombreVisible(BloqueoEjecucion.QuienTareaLunes), esperando.QuienBloquea);
        Assert.Empty(_falso.Peticiones);

        File.Delete(RutaBloqueo);
        await procesando;

        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(ejecucion.Id)).Estado);
    }

    [Fact]
    public async Task BloqueoOcupadoMasDeLaEsperaMaxima_QuedaError()
    {
        // La espera máxima es de una hora, igual que el tope de cualquier bloqueo: se acorta para ver la espera sola.
        _opciones = _opciones with { EsperaMaximaBloqueo = TimeSpan.FromMinutes(30) };
        EscribirBloqueo(BloqueoEjecucion.QuienEmpleo, Environment.ProcessId, _reloj.AhoraUtc.AddHours(3));
        var ejecucion = await EncolarAsync();

        var procesando = Procesador(_cola).ProcesarSiguienteAsync(CancellationToken.None);
        await EsperarAsync(ejecucion.Id, e => e.Espera == MotivoEspera.BloqueoOcupado);
        _reloj.Avanzar(TimeSpan.FromMinutes(31));
        await procesando;

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal(MensajesEjecucion.BloqueoDemasiadoTiempo(BloqueoEjecucion.NombreVisible(BloqueoEjecucion.QuienEmpleo)), final.Error);
        Assert.True(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task BloqueoConElPidMuerto_SeLimpiaYSeCoge()
    {
        EscribirBloqueo(BloqueoEjecucion.QuienTareaLunes, EjecutorClaudeFalso.PidFalso, _reloj.AhoraUtc.AddHours(1));
        var ejecucion = await EncolarAsync();

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(ejecucion.Id)).Estado);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task CancelarEnCurso_QuedaCanceladaYElFalsoRecibeLaCancelacion()
    {
        var ejecucion = await EncolarAsync();
        _falso.Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(ModoFalso.NoResponde));

        var procesando = _procesador.ProcesarSiguienteAsync(CancellationToken.None);
        await EsperarAsync(ejecucion.Id, e => e.Estado == EstadoEjecucion.EnCurso && e.Pid is not null);
        var cancelada = await _cola.CancelarAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.EnCurso, cancelada!.Estado);
        Assert.True(cancelada.CancelacionPedida);
        await procesando;

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Cancelada, final.Estado);
        Assert.Equal(1, _falso.Canceladas);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task CancelarEnCola_QuedaCanceladaYNoSeLanza()
    {
        var ejecucion = await EncolarAsync();
        Assert.Equal(EstadoEjecucion.Cancelada, (await _cola.CancelarAsync(ejecucion.Id))!.Estado);

        Assert.False(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));
        Assert.Empty(_falso.Peticiones);
        await Assert.ThrowsAsync<ReglaNegocioException>(() => _cola.CancelarAsync(ejecucion.Id));
        Assert.Null(await _cola.CancelarAsync(9999));
    }

    [Fact]
    public async Task Reintentar_CreaOtraConLosMismosParametros()
    {
        _falso.Programar(TareaAgente.AnalizarEncaje, new RespuestaFalsa(ModoFalso.Error));
        var ejecucion = await EncolarAsync(TareaAgente.AnalizarEncaje, _candidatura);
        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var otra = await _cola.ReintentarAsync(ejecucion.Id);

        Assert.True(otra!.Value.Nueva);
        Assert.NotEqual(ejecucion.Id, otra.Value.Ejecucion.Id);
        Assert.Equal(_candidatura, otra.Value.Ejecucion.CandidaturaId);
        Assert.Equal($$"""{"candidaturaId":{{_candidatura}}}""", otra.Value.Ejecucion.ParametrosJson);
        await Assert.ThrowsAsync<ReglaNegocioException>(() => _cola.ReintentarAsync(otra.Value.Ejecucion.Id));
    }

    [Fact]
    public async Task AlArrancar_EnCursoQuedaErrorEnColaCanceladaYSeBorraElBloqueoPropioCaducado()
    {
        var enCurso = EjecucionAgente.Nueva(TareaAgente.ExtraerRegistro, "{}", 1m, 600, null, _reloj.AhoraUtc);
        enCurso.Empezar(_reloj.AhoraUtc, "aaa", "ppp");
        enCurso.AnotarProceso(EjecutorClaudeFalso.PidFalso, _reloj.AhoraUtc);
        await GuardarAsync(enCurso);
        var enCola = EjecucionAgente.Nueva(TareaAgente.AnalizarEncaje, "{}", 0.5m, 300, null, _reloj.AhoraUtc);
        await GuardarAsync(enCola);
        // El bloqueo que dejó la API anterior: su pid ya no existe.
        EscribirBloqueo(BloqueoEjecucion.QuienRadar, EjecutorClaudeFalso.PidFalso, _reloj.AhoraUtc.AddMinutes(10));

        await _procesador.RecuperarAsync();

        var curso = await LeerAsync(enCurso.Id);
        Assert.Equal(EstadoEjecucion.Error, curso.Estado);
        Assert.Equal(EjecucionAgente.MensajeInterrumpida, curso.Error);
        var cola = await LeerAsync(enCola.Id);
        Assert.Equal(EstadoEjecucion.Cancelada, cola.Estado);
        Assert.Equal(EjecucionAgente.MensajeReiniciadaAntesDeEmpezar, cola.Resumen);
        Assert.False(File.Exists(RutaBloqueo));
        Assert.False(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AlArrancar_ElBloqueoDeOtroNoSeToca()
    {
        EscribirBloqueo(BloqueoEjecucion.QuienTareaLunes, EjecutorClaudeFalso.PidFalso, _reloj.AhoraUtc.AddMinutes(-5));

        await _procesador.RecuperarAsync();

        Assert.True(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task AlArrancar_BorraLosLogsDeMasDeSesentaDias()
    {
        Directory.CreateDirectory(_opciones.CarpetaLogs);
        var viejo = Path.Combine(_opciones.CarpetaLogs, "ejecucion-1.log");
        var nuevo = Path.Combine(_opciones.CarpetaLogs, "ejecucion-2.log");
        File.WriteAllText(viejo, "x");
        File.WriteAllText(nuevo, "x");
        File.SetLastWriteTimeUtc(viejo, _reloj.AhoraUtc.AddDays(-61));
        File.SetLastWriteTimeUtc(nuevo, _reloj.AhoraUtc);
        var carpetaVieja = Directory.CreateDirectory(Path.Combine(_opciones.CarpetaEjecuciones, "1"));
        carpetaVieja.LastWriteTimeUtc = _reloj.AhoraUtc.AddDays(-61);

        await _procesador.RecuperarAsync();

        Assert.False(File.Exists(viejo));
        Assert.True(File.Exists(nuevo));
        Assert.False(carpetaVieja.Exists && Directory.Exists(carpetaVieja.FullName));
    }

    [Fact]
    public async Task UnaExcepcionDelManejador_QuedaErrorYSigueConLaSiguiente()
    {
        var primera = await EncolarAsync(TareaAgente.ExtraerRegistro);
        var segunda = await EncolarAsync(TareaAgente.AnalizarEncaje, _candidatura);
        ManejadorDePrueba.AlAplicar = respuesta =>
            _falso.Peticiones.Count == 1 ? throw new InvalidOperationException("Fallo interno") : Task.CompletedTask;

        Assert.True(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));
        Assert.True(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));

        var a = await LeerAsync(primera.Id);
        Assert.Equal(EstadoEjecucion.Error, a.Estado);
        Assert.Equal(MensajesEjecucion.Inesperado, a.Error);
        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(segunda.Id)).Estado);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task UnaSalidaQueElManejadorNoAcepta_QuedaErrorConSuMensaje()
    {
        var ejecucion = await EncolarAsync();
        ManejadorDePrueba.AlAplicar = _ => throw new ReglaNegocioException(MensajesEjecucion.SalidaNoValida);

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(MensajesEjecucion.SalidaNoValida, (await LeerAsync(ejecucion.Id)).Error);
    }

    [Fact]
    public async Task UnaTareaSinManejadorEnLaCola_QuedaError()
    {
        var huerfana = EjecucionAgente.Nueva(TareaAgente.Redactar, "{}", 0.5m, 60, null, _reloj.AhoraUtc);
        await GuardarAsync(huerfana);

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(MensajesEjecucion.SinManejador, (await LeerAsync(huerfana.Id)).Error);
    }

    // ---------------------------------------------------------------- por partes

    private static JsonElement Salida(int parte) => JsonSerializer.SerializeToElement(new { parte });

    [Fact]
    public async Task PorPartes_SeLanzanEnSerieYEnOrdenEnUnaSolaEjecucionConElCosteYLosTurnosSumados()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        _falso.ProgramarSecuencia(TareaAgente.ExtraerRegistro,
            new RespuestaFalsa(Salida: Salida(1), CosteUsd: 0.10m, Turnos: 1),
            new RespuestaFalsa(Salida: Salida(2), CosteUsd: 0.20m, Turnos: 2),
            new RespuestaFalsa(Salida: Salida(3), CosteUsd: 0.30m, Turnos: 3));
        // Si fueran a la vez, la primera ya vería las tres peticiones.
        var vistas = new List<int>();
        _falso.AntesDeResponder = (_, _) =>
        {
            vistas.Add(_falso.Peticiones.Count);
            return Task.CompletedTask;
        };

        Assert.True(await _procesador.ProcesarSiguienteAsync(CancellationToken.None));

        Assert.Equal([1, 2, 3], vistas);
        var peticiones = _falso.Peticiones.ToList();
        Assert.Equal(["1 de 3", "2 de 3", "3 de 3"], peticiones.Select(p => p.Parte));
        Assert.Equal(["Prompt de prueba 1", "Prompt de prueba 2", "Prompt de prueba 3"], peticiones.Select(p => p.EntradaEstandar));
        // Todas en el mismo log y con el tiempo de su parte.
        Assert.Single(peticiones.Select(p => p.RutaLog).Distinct());
        Assert.All(peticiones, p => Assert.Equal(TimeSpan.FromSeconds(100), p.TiempoMaximo));
        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Ok, final.Estado);
        Assert.Equal(0.60m, final.CosteUsd);
        Assert.Equal(6, final.Turnos);
        Assert.Equal("Hecho en 3 de 3 partes.", final.Resumen);
        Assert.Equal("""[{"parte":1},{"parte":2},{"parte":3}]""", final.ResultadoJson);
        Assert.Equal([1, 2, 3],
            ManejadorDePrueba.PartesRecibidas!.Select(p => p.Respuesta.SalidaEstructurada!.Value.GetProperty("parte").GetInt32()));
        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, await db.EjecucionesAgente.CountAsync());
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task PorPartes_CadaParteLlevaSuPresupuestoEnLosArgumentos()
    {
        ManejadorDePrueba.Partes = 3;
        await EncolarAsync();

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        // 0,50 entre 3, hacia abajo, y por debajo del máximo de 0,20.
        Assert.Equal(3, _falso.Peticiones.Count);
        Assert.All(_falso.Peticiones, p =>
            Assert.Equal("0.16", p.Argumentos[p.Argumentos.ToList().IndexOf("--max-budget-usd") + 1]));
    }

    [Theory]
    [InlineData(ModoFalso.Error)]
    [InlineData(ModoFalso.PresupuestoAgotado)]
    [InlineData(ModoFalso.SalidaNoValida)]
    public async Task PorPartes_UnaParteQueFalla_NoParaLasDemasYElManejadorRecibeLasTres(ModoFalso modo)
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        _falso.ProgramarSecuencia(TareaAgente.ExtraerRegistro,
            new RespuestaFalsa(Salida: Salida(1), CosteUsd: 0.10m),
            new RespuestaFalsa(modo, CosteUsd: 0.05m),
            new RespuestaFalsa(Salida: Salida(3), CosteUsd: 0.10m));

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(3, _falso.Peticiones.Count);
        var recibidas = ManejadorDePrueba.PartesRecibidas!;
        Assert.Equal(["1 de 3", "2 de 3", "3 de 3"], recibidas.Select(p => p.Peticion.Parte));
        Assert.Equal([true, false, true], recibidas.Select(p => p.Respuesta.Correcta));
        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Ok, final.Estado);
        Assert.Equal("Hecho en 2 de 3 partes.", final.Resumen);
        // La salida no válida del falso no trae coste: entonces no se sabe cuánto ha gastado.
        Assert.Equal(modo == ModoFalso.SalidaNoValida ? null : 0.25m, final.CosteUsd);
    }

    [Fact]
    public async Task PorPartes_SiFallanTodas_QuedaErrorConElMensajeDeLaPrimeraYElCosteSumado()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        _falso.ProgramarSecuencia(TareaAgente.ExtraerRegistro,
            new RespuestaFalsa(ModoFalso.PresupuestoAgotado, CosteUsd: 0.16m),
            new RespuestaFalsa(ModoFalso.Error, CosteUsd: 0.02m),
            new RespuestaFalsa(ModoFalso.Error, CosteUsd: 0.03m));

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal(MensajesEjecucion.TopeTarea, final.Error);
        Assert.Equal(0.21m, final.CosteUsd);
        Assert.Equal(3, _falso.Peticiones.Count);
        Assert.Null(ManejadorDePrueba.PartesRecibidas);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task PorPartes_SiTodasSeQuedanSinTiempo_ElMensajeDiceElTiempoDeUnaParteYElCosteNoSeSabe()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        _falso.Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(ModoFalso.NoResponde));

        await Procesador(_cola, ejecutor: new PeticionAcortada(_falso)).ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal("Ha tardado más de 1 min 40 s y la he parado.", final.Error);
        Assert.Null(final.CosteUsd);
        Assert.Equal(3, _falso.Peticiones.Count);
    }

    [Fact]
    public async Task PorPartes_UnaParteCortadaSinCoste_DejaElCosteSinSaberAunqueLasDemasSalganBien()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        _falso.ProgramarSecuencia(TareaAgente.ExtraerRegistro,
            new RespuestaFalsa(Salida: Salida(1), CosteUsd: 0.10m),
            new RespuestaFalsa(ModoFalso.NoResponde),
            new RespuestaFalsa(Salida: Salida(3), CosteUsd: 0.10m));

        await Procesador(_cola, ejecutor: new PeticionAcortada(_falso)).ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Ok, final.Estado);
        Assert.Null(final.CosteUsd);
        // En el tope del día cuenta el presupuesto entero de la ejecución.
        Assert.Equal(final.PresupuestoUsd, ReglasGasto.GastoComputable(final));
        Assert.Equal(FalloClaude.TiempoAgotado, ManejadorDePrueba.PartesRecibidas![1].Respuesta.Fallo);
    }

    [Fact]
    public async Task PorPartes_CancelarDuranteLaSegunda_QuedaCanceladaYLaTerceraNoSeLanza()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        _falso.ProgramarSecuencia(TareaAgente.ExtraerRegistro,
            new RespuestaFalsa(Salida: Salida(1), CosteUsd: 0.10m),
            new RespuestaFalsa(ModoFalso.NoResponde));

        var procesando = _procesador.ProcesarSiguienteAsync(CancellationToken.None);
        await Esperar.HastaAsync(() => _falso.Peticiones.Count == 2, "La segunda parte no ha empezado.");
        await _cola.CancelarAsync(ejecucion.Id);
        await procesando;

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Cancelada, final.Estado);
        Assert.Equal(2, _falso.Peticiones.Count);
        Assert.Equal(1, _falso.Canceladas);
        // La segunda se ha cortado sin coste: no se sabe lo que ha gastado.
        Assert.Null(final.CosteUsd);
        Assert.Null(ManejadorDePrueba.PartesRecibidas);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task PorPartes_CancelarJustoAlAcabarUnaParte_NoLanzaLaSiguienteYCuentaLoGastado()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        _falso.Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(Salida: Salida(1), CosteUsd: 0.10m));
        // Se cancela mientras responde la primera, que aun así sale bien.
        _falso.AntesDeResponder = async (_, _) =>
        {
            if (_falso.Peticiones.Count == 1) await _cola.CancelarAsync(ejecucion.Id);
        };

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Cancelada, final.Estado);
        Assert.Single(_falso.Peticiones);
        Assert.Equal(0.10m, final.CosteUsd);
        Assert.Null(ManejadorDePrueba.PartesRecibidas);
    }

    [Fact]
    public async Task PorPartes_PararLaApiDuranteUnaParte_QuedaInterrumpidaYNoLanzaMas()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        using var parada = new CancellationTokenSource();
        _falso.AntesDeResponder = async (_, corte) =>
        {
            if (_falso.Peticiones.Count < 2) return;
            await parada.CancelAsync();
            await Task.Delay(Timeout.Infinite, corte);
        };

        try
        {
            await _procesador.ProcesarSiguienteAsync(parada.Token);
        }
        catch (OperationCanceledException)
        {
            // Parar la API puede acabar en la cancelación: lo que importa es cómo queda la ejecución.
        }

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal(EjecucionAgente.MensajeInterrumpida, final.Error);
        Assert.Equal(2, _falso.Peticiones.Count);
        Assert.Null(ManejadorDePrueba.PartesRecibidas);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task PorPartes_ElPidDeCadaParteSeApuntaAntesDeLanzarLaSiguiente()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        var contado = new EjecutorContado(_falso);
        var vistos = new List<int?>();
        contado.AlEmpezarLlamada = async _ => vistos.Add((await LeerAsync(ejecucion.Id)).Pid);

        await Procesador(_cola, ejecutor: contado).ProcesarSiguienteAsync(CancellationToken.None);

        // Antes de la primera no hay ninguno; antes de cada siguiente, ya está el de la anterior.
        Assert.Equal([null, 1001, 1002], vistos);
        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(1003, final.Pid);
        Assert.Equal(EstadoEjecucion.Ok, final.Estado);
    }

    [Fact]
    public async Task PorPartes_SinClaudeInstalado_NoLanzaLasDemasYElManejadorLasRecibeSinLanzar()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        var contado = new EjecutorContado(_falso)
        {
            Sustituir = n => n == 2 ? RespuestaClaude.Fallida(FalloClaude.NoInstalado) : null
        };

        await Procesador(_cola, ejecutor: contado).ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(2, contado.Llamadas);
        var recibidas = ManejadorDePrueba.PartesRecibidas!;
        Assert.Equal([null, FalloClaude.NoInstalado, FalloClaude.NoInstalado], recibidas.Select(p => p.Respuesta.Fallo));
        Assert.Equal("3 de 3", recibidas[2].Peticion.Parte);
        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(ejecucion.Id)).Estado);
    }

    [Fact]
    public async Task PorPartes_SinClaudeInstaladoEnLaPrimera_QuedaErrorSinLanzarLasDemas()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        var contado = new EjecutorContado(_falso) { Sustituir = _ => RespuestaClaude.Fallida(FalloClaude.NoInstalado) };

        await Procesador(_cola, ejecutor: contado).ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(1, contado.Llamadas);
        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal(MensajesEjecucion.NoInstalado, final.Error);
        Assert.Null(ManejadorDePrueba.PartesRecibidas);
    }

    /// <summary>Una petición de prueba con ese presupuesto y ese tiempo.</summary>
    private static PeticionClaude Parte(ContextoTarea contexto, DefinicionTarea definicion, decimal presupuesto, TimeSpan tiempo, string parte) =>
        contexto.Peticion(definicion, ConstructorArgumentos.Construir(definicion, """{"type":"object"}""",
            new ValoresArgumentos(contexto.CarpetaDatos), presupuesto), "Prompt de prueba", tiempo, parte);

    public static TheoryData<int?, int, double, int> PartesQueNoCaben => new()
    {
        // Sin partes en la definición.
        { null, 2, 0.10, 1 },
        // Más de las seis partes que admite.
        { 3, 7, 0.05, 0 },
        // Tres de 2 min con 5 min en total: el bloqueo caducaría antes.
        { 3, 3, 0.10, 2 },
        // Tres de 0,20 con 0,50 en total.
        { 3, 3, 0.20, 1 }
    };

    [Theory]
    [MemberData(nameof(PartesQueNoCaben))]
    public async Task PorPartes_LasQueNoCabenEnLaTarea_FallanSinLanzarNadaNiGastar(int? partes, int peticiones, double presupuesto, int minutos)
    {
        ManejadorDePrueba.Partes = partes;
        ManejadorDePrueba.OtrasPeticiones = (contexto, definicion) => Enumerable.Range(1, peticiones)
            .Select(k => Parte(contexto, definicion, (decimal)presupuesto, minutos == 0 ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(minutos),
                $"{k} de {peticiones}"))
            .ToList();
        var ejecucion = await EncolarAsync();

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Error, final.Estado);
        Assert.Equal(MensajesEjecucion.PartesNoCaben, final.Error);
        Assert.Equal(0m, final.CosteUsd);
        Assert.Empty(_falso.Peticiones);
        Assert.False(File.Exists(RutaBloqueo));
    }

    [Fact]
    public async Task PorPartes_ConUnPresupuestoQueNoLlegaAUnCentimoPorParte_FallaConSuMensajeSinGastar()
    {
        ManejadorDePrueba.Partes = 3;
        ManejadorDePrueba.OtrasPeticiones = (contexto, definicion) =>
            [Parte(contexto, definicion with { PresupuestoUsd = 0.02m }, (definicion with { PresupuestoUsd = 0.02m }).PresupuestoParte(3),
                TimeSpan.FromMinutes(1), "1 de 3")];
        var ejecucion = await EncolarAsync();

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(MensajesEjecucion.PresupuestoNoLlegaParaLasPartes, final.Error);
        Assert.Equal(0m, final.CosteUsd);
        Assert.Empty(_falso.Peticiones);
    }

    [Fact]
    public async Task PorPartes_PararLaApiAlAcabarUnaParte_InterrumpeConLoQueHaGastado()
    {
        ManejadorDePrueba.Partes = 3;
        var ejecucion = await EncolarAsync();
        _falso.Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(Salida: Salida(1), CosteUsd: 0.10m));
        using var parada = new CancellationTokenSource();
        // Se para mientras responde la primera, que aun así sale bien.
        _falso.AntesDeResponder = async (_, _) => await parada.CancelAsync();

        try
        {
            await _procesador.ProcesarSiguienteAsync(parada.Token);
        }
        catch (OperationCanceledException)
        {
            // Lo que importa es cómo queda la ejecución.
        }

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EjecucionAgente.MensajeInterrumpida, final.Error);
        Assert.Single(_falso.Peticiones);
        Assert.Equal(0.10m, final.CosteUsd);
    }

    [Fact]
    public async Task PorPartes_SiUnaParteNoTraeTurnos_ElTotalNoSeSabe()
    {
        ManejadorDePrueba.Partes = 2;
        var ejecucion = await EncolarAsync();
        _falso.ProgramarSecuencia(TareaAgente.ExtraerRegistro,
            new RespuestaFalsa(Salida: Salida(1), Turnos: 2),
            new RespuestaFalsa(Salida: Salida(2), Turnos: null));

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var final = await LeerAsync(ejecucion.Id);
        Assert.Equal(EstadoEjecucion.Ok, final.Estado);
        Assert.Null(final.Turnos);
    }

    [Fact]
    public async Task UnaSolaPeticion_ElManejadorLaRecibeComoUnaParteSinNumero()
    {
        var ejecucion = await EncolarAsync();

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        var (peticion, respuesta) = Assert.Single(ManejadorDePrueba.PartesRecibidas!);
        Assert.Null(peticion.Parte);
        Assert.True(respuesta.Correcta);
        Assert.Equal("Hecho por la tarea de prueba.", (await LeerAsync(ejecucion.Id)).Resumen);
    }

    /// <summary>
    /// Envuelve al falso: cada llamada anuncia su propio pid (1000 más su número) y, si <see cref="Sustituir"/> da una
    /// respuesta, la devuelve sin llamar al falso.
    /// </summary>
    private sealed class EjecutorContado(IEjecutorClaude interior) : IEjecutorClaude
    {
        public int Llamadas { get; private set; }

        /// <summary>Se llama al empezar cada llamada, con su número (desde 1).</summary>
        public Func<int, Task>? AlEmpezarLlamada { get; set; }

        public Func<int, RespuestaClaude?>? Sustituir { get; init; }

        public async Task<RespuestaClaude> EjecutarAsync(PeticionClaude peticion, Action<int, DateTime> alEmpezar, CancellationToken cancelacion)
        {
            var numero = ++Llamadas;
            if (AlEmpezarLlamada is { } alEmpezarLlamada) await alEmpezarLlamada(numero);
            if (Sustituir?.Invoke(numero) is { } sustituta) return sustituta;
            return await interior.EjecutarAsync(peticion, (_, inicio) => alEmpezar(1000 + numero, inicio), cancelacion);
        }

        public Task<string?> VersionAsync(CancellationToken cancelacion) => interior.VersionAsync(cancelacion);
    }

    /// <summary>Envuelve al falso acortando el tiempo máximo de cada petición a 200 ms.</summary>
    private sealed class PeticionAcortada(IEjecutorClaude interior) : IEjecutorClaude
    {
        public Task<RespuestaClaude> EjecutarAsync(PeticionClaude peticion, Action<int, DateTime> alEmpezar, CancellationToken cancelacion) =>
            interior.EjecutarAsync(peticion with { TiempoMaximo = TimeSpan.FromMilliseconds(200) }, alEmpezar, cancelacion);

        public Task<string?> VersionAsync(CancellationToken cancelacion) => interior.VersionAsync(cancelacion);
    }
}

/// <summary>Los tests que cambian <see cref="ManejadorDePrueba.AlAplicar"/> (estático) no van en paralelo.</summary>
[CollectionDefinition(nameof(ColeccionManejadorDePrueba), DisableParallelization = true)]
public sealed class ColeccionManejadorDePrueba;
