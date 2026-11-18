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

    public ManejadorDePrueba(DefinicionTarea definicion) => Definicion = definicion;

    public DefinicionTarea Definicion { get; }

    /// <summary>Lo que hace al aplicar la respuesta (por defecto, nada).</summary>
    public static Func<RespuestaClaude, Task>? AlAplicar { get; set; }

    /// <summary>Lo que hace al montar la petición, con el corte de la ejecución (por defecto, nada).</summary>
    public static Func<CancellationToken, Task>? AlConstruir { get; set; }

    /// <summary>Si no es null, la tarea termina al prepararla con este resultado, sin lanzar Claude.</summary>
    public static ResultadoTarea? SinClaude { get; set; }

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
        return PreparacionTarea.ConClaude(contexto.Peticion(Definicion,
            ConstructorArgumentos.Construir(Definicion, """{"type":"object"}""", new ValoresArgumentos(contexto.CarpetaDatos)), "Prompt de prueba"));
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
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), $"radar-cola-{Guid.NewGuid():N}");
    private readonly RelojFijo _reloj = new(DateTime.UtcNow);
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
        ManejadorDePrueba.AlAplicar = null;
        ManejadorDePrueba.AlConstruir = null;
        ManejadorDePrueba.SinClaude = null;

        // Sin ruta de exclusión: esta «API» lleva siempre la cola (los tests de dos APIs crean sus propias guardias).
        _guardia = new GuardiaCola(_opciones);
        _cola = Cola(_opciones, _guardia);
        _bloqueo = new BloqueoCompartido(_rutas, NullLogger<BloqueoCompartido>.Instance);
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
        ManejadorDePrueba.AlAplicar = null;
        ManejadorDePrueba.AlConstruir = null;
        ManejadorDePrueba.SinClaude = null;
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

    private async Task<EjecucionAgente> EsperarAsync(int id, Func<EjecucionAgente, bool> condicion)
    {
        var limite = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var ejecucion = await LeerAsync(id);
            if (condicion(ejecucion)) return ejecucion;
            if (DateTime.UtcNow > limite) throw new TimeoutException($"La ejecución {id} sigue en {ejecucion.Estado}.");
            await Task.Delay(20);
        }
    }

    private async Task GuardarAsync(EjecucionAgente ejecucion)
    {
        await using var db = _factory.CreateDbContext();
        db.EjecucionesAgente.Add(ejecucion);
        await db.SaveChangesAsync();
    }

    private void EscribirBloqueo(string quien, int? pid, DateTime caduca, DateTime? inicio = null) =>
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_rutas.CarpetaRadar!).FullName, BloqueoCompartido.NombreArchivo),
            BloqueoEjecucion.Serializar(new BloqueoLeido(1, quien, "Rastreo", null, pid, inicio ?? DateTime.UtcNow, caduca, true)));

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
        Assert.Equal("Ha llegado al tope de uso de esta tarea y la he parado.", final.Error);
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
    [InlineData(ModoFalso.Error, "Claude ha devuelto un error.")]
    [InlineData(ModoFalso.SalidaNoValida, "Claude ha respondido en un formato que no entiendo.")]
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
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_rutas.CarpetaRadar!).FullName, BloqueoCompartido.NombreArchivo),
            BloqueoEjecucion.Serializar(BloqueoEjecucion.DeLaApp(TareaAgente.AnalizarEncaje, 9999, Environment.ProcessId,
                DateTime.UtcNow, TimeSpan.FromMinutes(5))));
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
        Assert.Equal("otra tarea", esperando.QuienBloquea);

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
        var tomado = _bloqueo.Intentar(BloqueoEjecucion.DeLaApp(TareaAgente.ExtraerRegistro, 1, Environment.ProcessId, DateTime.UtcNow,
            TimeSpan.FromMinutes(5)), _reloj.AhoraUtc);
        Assert.True(tomado.Cogido);
        // Un lector que no comparte el borrado (como Get-Content en la tarea de los lunes), durante un momento.
        var lector = new FileStream(RutaBloqueo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var cerrar = Task.Run(async () =>
        {
            await Task.Delay(250);
            await lector.DisposeAsync();
        });

        tomado.Dispose();
        await cerrar;

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
        File.SetLastWriteTimeUtc(RutaBloqueo, DateTime.UtcNow.AddDays(3));
        var ejecucion = await EncolarAsync();

        await _procesador.ProcesarSiguienteAsync(CancellationToken.None);

        Assert.Equal(EstadoEjecucion.Ok, (await LeerAsync(ejecucion.Id)).Estado);
    }

    [Fact]
    public async Task UnQuienRaroSaleSaneado()
    {
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_rutas.CarpetaRadar!).FullName, BloqueoCompartido.NombreArchivo),
            BloqueoEjecucion.Serializar(new BloqueoLeido(1, "<b>Hola</b>\u202E", null, null, Environment.ProcessId, DateTime.UtcNow,
                _reloj.AhoraUtc.AddHours(1), false)));
        var ejecucion = await EncolarAsync();

        var procesando = _procesador.ProcesarSiguienteAsync(CancellationToken.None);
        var esperando = await EsperarAsync(ejecucion.Id, e => e.Espera == MotivoEspera.BloqueoOcupado);
        Assert.Equal("bHola/b", esperando.QuienBloquea);
        File.Delete(RutaBloqueo);
        await procesando;
    }

    [Fact]
    public async Task DosEncoladas_VanDeUnaEnUnaYPorOrden()
    {
        // Arranca antes de encolar: al arrancar, lo que ya estaba en cola se cancela.
        await _procesador.StartAsync(CancellationToken.None);
        _falso.Programar(TareaAgente.ExtraerRegistro, new RespuestaFalsa(EsperaMs: 300));
        EjecucionAgente? segundaDurante = null;
        var segundaId = 0;
        _falso.AntesDeResponder = async (p, _) =>
        {
            if (p.Tarea == TareaAgente.ExtraerRegistro) segundaDurante = await LeerAsync(segundaId);
        };
        var primera = await EncolarAsync(TareaAgente.ExtraerRegistro);
        var segunda = await EncolarAsync(TareaAgente.AnalizarEncaje, _candidatura);
        segundaId = segunda.Id;

        await EsperarAsync(segunda.Id, e => e.EsTerminal);

        Assert.Equal(EstadoEjecucion.EnCola, segundaDurante!.Estado);
        Assert.Equal([TareaAgente.ExtraerRegistro, TareaAgente.AnalizarEncaje], _falso.Peticiones.Select(p => p.Tarea));
        var a = await LeerAsync(primera.Id);
        var b = await LeerAsync(segunda.Id);
        Assert.Equal(EstadoEjecucion.Ok, a.Estado);
        Assert.Equal(EstadoEjecucion.Ok, b.Estado);
        Assert.True(a.Fin <= b.Inicio);
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
        Assert.Equal("Has llegado al tope de uso estimado de hoy. Mañana se puede volver a lanzar.", error.Message);
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
        Assert.Equal("Has llegado al tope de uso estimado de hoy. Mañana se puede volver a lanzar.", final.Error);
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
        Assert.Equal("la tarea de los lunes", esperando.QuienBloquea);
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
        Assert.Equal("La otra ejecución (/empleo) sigue en marcha después de una hora.", final.Error);
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
        Assert.Equal("Se interrumpió: la API se cerró mientras trabajaba.", curso.Error);
        var cola = await LeerAsync(enCola.Id);
        Assert.Equal(EstadoEjecucion.Cancelada, cola.Estado);
        Assert.Equal("La API se reinició antes de empezar.", cola.Resumen);
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
        Assert.Equal("Algo ha fallado al lanzar Claude.", a.Error);
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
