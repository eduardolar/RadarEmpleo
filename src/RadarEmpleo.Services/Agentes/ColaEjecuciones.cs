using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Services.Agentes;

/// <summary>Lo que cuenta para el tope en este momento.</summary>
/// <param name="EnCola">El presupuesto de lo que está en cola (todavía sin <see cref="EjecucionAgente.Inicio"/>).</param>
public sealed record GastoActual(GastoDelDia Dia, decimal EnCola)
{
    /// <summary>Lo comprometido además de lo gastado: lo que está en curso y lo que está en cola.</summary>
    public decimal Reservado => Dia.Reservado + EnCola;
}

/// <summary>
/// La cola de ejecuciones (singleton). La fuente de verdad es la BD: el <see cref="Channel{T}"/> solo despierta al
/// <see cref="ProcesadorEjecuciones"/>. Todos los cambios de estado que vienen de fuera (encolar, cancelar) y los del
/// procesador pasan por <see cref="Cerrojo"/>, en contextos cortos, para que no se pisen.
/// </summary>
public sealed class ColaEjecuciones
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _reloj;
    private readonly RutasCurriculum _rutas;
    private readonly OpcionesEjecutor _opciones;
    private readonly IServiceScopeFactory _ambitos;
    private readonly GuardiaCola _guardia;
    private readonly Channel<int> _senal = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activas = new();

    public ColaEjecuciones(IDbContextFactory<AppDbContext> factory, IClock reloj, RutasCurriculum rutas, OpcionesEjecutor opciones,
        IServiceScopeFactory ambitos, GuardiaCola guardia)
    {
        _guardia = guardia;
        _factory = factory;
        _reloj = reloj;
        _rutas = rutas;
        _opciones = opciones;
        _ambitos = ambitos;
    }

    /// <summary>Reintentar una que salió bien no tiene sentido: se lanza de nuevo desde su pantalla.</summary>
    public const string MensajeYaTerminoBien = "Ya terminó bien: si quieres repetirla, lánzala otra vez desde su pantalla.";

    /// <summary>De uno en uno: encolar, cancelar y los cambios de estado del procesador.</summary>
    internal SemaphoreSlim Cerrojo { get; } = new(1, 1);

    /// <summary>
    /// Encola una tarea. Devuelve null si lo que se pide no existe (404). Si ya hay una en cola o en curso de la misma
    /// tarea (y candidatura), la devuelve con <c>Nueva = false</c> en vez de crear otra (doble clic).
    /// </summary>
    /// <exception cref="ReglaNegocioException">Esta API no lleva la cola (no se procesaría ni debe reservar presupuesto), tarea
    /// sin manejador, sin carpeta Curriculum o sin sitio en el tope de hoy.</exception>
    public async Task<(EjecucionAgente Ejecucion, bool Nueva)?> EncolarAsync(TareaAgente tarea, int? candidaturaId = null,
        CancellationToken cancelacion = default)
    {
        ValidacionException.ExigirDefinido(tarea, "tarea");
        if (!_guardia.LlevaLaCola) throw new ReglaNegocioException(_guardia.Motivo ?? MensajesEjecucion.ColaSinArrancar);
        using var ambito = _ambitos.CreateScope();
        var manejador = Manejador(ambito.ServiceProvider, tarea) ?? throw new ReglaNegocioException(MensajesEjecucion.SinManejador);
        if (!await manejador.ValidarAsync(candidaturaId, cancelacion)) return null;
        if (!_rutas.HayCarpeta) throw new ReglaNegocioException(RutasCurriculum.MensajeSinCarpeta);

        var definicion = manejador.Definicion;
        await Cerrojo.WaitAsync(cancelacion);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(cancelacion);
            var activa = await db.EjecucionesAgente
                .Where(e => e.Tarea == tarea && e.CandidaturaId == candidaturaId
                    && (e.Estado == EstadoEjecucion.EnCola || e.Estado == EstadoEjecucion.EnCurso))
                .OrderBy(e => e.Id)
                .FirstOrDefaultAsync(cancelacion);
            if (activa is not null) return (activa, false);

            var ahora = _reloj.AhoraUtc;
            var gasto = await LeerGastoAsync(db, ahora, cancelacion);
            if (!ReglasGasto.CabeEnElTope(gasto.Dia.Gastado, gasto.Reservado, definicion.PresupuestoUsd, _opciones.TopeGastoDiarioUsd))
                throw new ReglaNegocioException(MensajesEjecucion.TopeDiario(_opciones.TopeGastoDiarioUsd));

            var parametros = candidaturaId is { } id ? JsonSerializer.Serialize(new { candidaturaId = id }) : "{}";
            var ejecucion = EjecucionAgente.Nueva(tarea, parametros, definicion.PresupuestoUsd,
                (int)definicion.TiempoMaximo.TotalSeconds, definicion.Modelo, ahora, candidaturaId);
            db.EjecucionesAgente.Add(ejecucion);
            await db.SaveChangesAsync(cancelacion);
            Avisar(ejecucion.Id);
            return (ejecucion, true);
        }
        finally
        {
            Cerrojo.Release();
        }
    }

    /// <summary>
    /// Cancela: en cola, al momento; en curso, la marca y corta el proceso (el procesador la deja Cancelada en cuanto
    /// muere). Si la lleva otra copia de la API, solo se marca y esa la corta al verlo (la vigila mientras corre). Si esta
    /// API lleva la cola y no la está procesando (no tiene su corte), nadie la va a terminar: queda Cancelada al momento,
    /// para que no se quede colgada con el presupuesto reservado. Devuelve null si no existe.
    /// </summary>
    /// <exception cref="ReglaNegocioException">Ya ha terminado.</exception>
    public async Task<EjecucionAgente?> CancelarAsync(int id, CancellationToken cancelacion = default)
    {
        await Cerrojo.WaitAsync(cancelacion);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(cancelacion);
            var ejecucion = await db.EjecucionesAgente.FirstOrDefaultAsync(e => e.Id == id, cancelacion);
            if (ejecucion is null) return null;

            ejecucion.PedirCancelacion(_reloj.AhoraUtc);
            if (ejecucion.Estado == EstadoEjecucion.EnCurso && _guardia.LlevaLaCola && !_activas.ContainsKey(id))
                ejecucion.ConfirmarCancelacion(_reloj.AhoraUtc, null, null, null);
            await db.SaveChangesAsync(cancelacion);
            if (ejecucion.Estado == EstadoEjecucion.EnCurso && _activas.TryGetValue(id, out var corte))
            {
                try
                {
                    await corte.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                    // El procesador acaba de terminarla: su estado final ya no depende de este corte.
                }
            }
            return ejecucion;
        }
        finally
        {
            Cerrojo.Release();
        }
    }

    /// <summary>Vuelve a encolar la misma tarea con los mismos parámetros, revalidados. Null si no existe la ejecución o lo que pedía.</summary>
    /// <exception cref="ReglaNegocioException">Todavía no ha terminado, o no se puede encolar.</exception>
    public async Task<(EjecucionAgente Ejecucion, bool Nueva)?> ReintentarAsync(int id, CancellationToken cancelacion = default)
    {
        EjecucionAgente? anterior;
        await using (var db = await _factory.CreateDbContextAsync(cancelacion))
            anterior = await db.EjecucionesAgente.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancelacion);
        if (anterior is null) return null;
        if (!anterior.EsTerminal) throw new ReglaNegocioException("Todavía no ha terminado.");
        if (anterior.Estado == EstadoEjecucion.Ok) throw new ReglaNegocioException(MensajeYaTerminoBien);
        return await EncolarAsync(anterior.Tarea, anterior.CandidaturaId, cancelacion);
    }

    /// <summary>¿Hay un manejador registrado para esa tarea?</summary>
    public bool TieneManejador(TareaAgente tarea)
    {
        using var ambito = _ambitos.CreateScope();
        return Manejador(ambito.ServiceProvider, tarea) is not null;
    }

    /// <summary>Gasto de hoy y presupuesto comprometido, leídos de la BD (la suma es en memoria: SQLite no suma decimal).</summary>
    public static async Task<GastoActual> LeerGastoAsync(AppDbContext db, DateTime ahoraUtc, CancellationToken cancelacion = default)
    {
        // Un margen de un día por delante y por detrás cubre cualquier desfase de Madrid; GastadoHoy filtra el día exacto.
        var desde = ahoraUtc.AddDays(-1);
        var candidatas = await db.EjecucionesAgente.AsNoTracking()
            .Where(e => (e.Inicio != null && e.Inicio >= desde) || e.Estado == EstadoEjecucion.EnCola || e.Estado == EstadoEjecucion.EnCurso)
            .ToListAsync(cancelacion);
        var dia = ReglasGasto.GastadoHoy(candidatas, ahoraUtc);
        var enCola = candidatas.Where(e => e.Estado == EstadoEjecucion.EnCola).Sum(e => e.PresupuestoUsd);
        return new GastoActual(dia, enCola);
    }

    internal static IManejadorTarea? Manejador(IServiceProvider servicios, TareaAgente tarea) =>
        servicios.GetServices<IManejadorTarea>().FirstOrDefault(m => m.Definicion.Tarea == tarea);

    /// <summary>Despierta al procesador.</summary>
    internal void Avisar(int id) => _senal.Writer.TryWrite(id);

    /// <summary>Espera un aviso o a que pase el intervalo de sondeo.</summary>
    internal async Task EsperarAvisoAsync(CancellationToken cancelacion)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        limite.CancelAfter(_opciones.IntervaloSondeo);
        try
        {
            await _senal.Reader.WaitToReadAsync(limite.Token);
            while (_senal.Reader.TryRead(out _)) { }
        }
        catch (OperationCanceledException) when (!cancelacion.IsCancellationRequested)
        {
            // Ha pasado el intervalo: se vuelve a mirar la BD.
        }
    }

    /// <summary>Apunta el corte de una ejecución que empieza (dentro del <see cref="Cerrojo"/>).</summary>
    internal void Registrar(int id, CancellationTokenSource corte) => _activas[id] = corte;

    internal void Quitar(int id) => _activas.TryRemove(id, out _);
}
