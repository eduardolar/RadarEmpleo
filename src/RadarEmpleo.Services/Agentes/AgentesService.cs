using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Services.Agentes;

/// <summary>
/// Lo que la API necesita de los agentes: encolar, cancelar y reintentar (a través de <see cref="ColaEjecuciones"/>),
/// la lista y el detalle de las ejecuciones, el estado y el gasto de hoy. Los métodos que buscan por id devuelven null
/// si no existe (404).
/// </summary>
public sealed class AgentesService
{
    public const int LimitePorDefecto = 50;
    public const int LimiteMaximo = 200;

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _reloj;
    private readonly ColaEjecuciones _cola;
    private readonly IEjecutorClaude _ejecutor;
    private readonly OpcionesEjecutor _opciones;
    private readonly GuardiaCola _guardia;

    public AgentesService(IDbContextFactory<AppDbContext> factory, IClock reloj, ColaEjecuciones cola, IEjecutorClaude ejecutor,
        OpcionesEjecutor opciones, GuardiaCola guardia)
    {
        _guardia = guardia;
        _factory = factory;
        _reloj = reloj;
        _cola = cola;
        _ejecutor = ejecutor;
        _opciones = opciones;
    }

    /// <summary>Encola una tarea (o devuelve la que ya estaba activa, con <c>Nueva = false</c>). Null si lo pedido no existe.</summary>
    public async Task<(EjecucionAgenteDto Ejecucion, bool Nueva)?> EncolarAsync(TareaAgente tarea, int? candidaturaId = null,
        CancellationToken cancelacion = default)
    {
        var encolada = await _cola.EncolarAsync(tarea, candidaturaId, cancelacion);
        return encolada is { } e ? ((await ObtenerAsync(e.Ejecucion.Id, cancelacion))!, e.Nueva) : null;
    }

    /// <summary>Cancela una ejecución; null si no existe.</summary>
    /// <exception cref="ReglaNegocioException">Ya ha terminado.</exception>
    public async Task<EjecucionAgenteDto?> CancelarAsync(int id, CancellationToken cancelacion = default) =>
        await _cola.CancelarAsync(id, cancelacion) is null ? null : await ObtenerAsync(id, cancelacion);

    /// <summary>Crea otra ejecución con los mismos parámetros; null si no existe.</summary>
    /// <exception cref="ReglaNegocioException">Todavía no ha terminado, o no se puede encolar.</exception>
    public async Task<(EjecucionAgenteDto Ejecucion, bool Nueva)?> ReintentarAsync(int id, CancellationToken cancelacion = default)
    {
        var encolada = await _cola.ReintentarAsync(id, cancelacion);
        return encolada is { } e ? ((await ObtenerAsync(e.Ejecucion.Id, cancelacion))!, e.Nueva) : null;
    }

    /// <summary>De la más reciente a la más antigua.</summary>
    /// <exception cref="ValidacionException">El límite no está entre 1 y 200, o la tarea no existe.</exception>
    public async Task<List<EjecucionAgenteDto>> ListarAsync(TareaAgente? tarea = null, int? candidaturaId = null, int limite = LimitePorDefecto,
        CancellationToken cancelacion = default)
    {
        if (limite is < 1 or > LimiteMaximo)
            throw new ValidacionException($"El límite tiene que estar entre 1 y {LimiteMaximo}.", "limite");
        if (tarea is { } t) ValidacionException.ExigirDefinido(t, "tarea");

        await using var db = await _factory.CreateDbContextAsync(cancelacion);
        var consulta = Consulta(db);
        if (tarea is { } filtroTarea) consulta = consulta.Where(e => e.Tarea == filtroTarea);
        if (candidaturaId is { } filtroCandidatura) consulta = consulta.Where(e => e.CandidaturaId == filtroCandidatura);
        var ejecuciones = await consulta.OrderByDescending(e => e.Id).Take(limite).ToListAsync(cancelacion);
        return await ADtosAsync(db, ejecuciones, cancelacion);
    }

    public async Task<EjecucionAgenteDto?> ObtenerAsync(int id, CancellationToken cancelacion = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancelacion);
        var ejecucion = await Consulta(db).FirstOrDefaultAsync(e => e.Id == id, cancelacion);
        return ejecucion is null ? null : (await ADtosAsync(db, [ejecucion], cancelacion))[0];
    }

    /// <summary>Gasto de hoy (día de Madrid) contra el tope.</summary>
    public async Task<GastoHoyDto> GastoHoyAsync(CancellationToken cancelacion = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancelacion);
        return AGasto(await ColaEjecuciones.LeerGastoAsync(db, _reloj.AhoraUtc, cancelacion));
    }

    /// <summary>Estado para Ajustes y la página Agentes. Lo de la disponibilidad lo decide la API (canal, configuración y modo).</summary>
    public async Task<EstadoAgentesDto> EstadoAsync(bool disponibles, string? motivoNoDisponibles, CancellationToken cancelacion = default)
    {
        var version = await _ejecutor.VersionAsync(cancelacion);
        await using var db = await _factory.CreateDbContextAsync(cancelacion);
        var gasto = AGasto(await ColaEjecuciones.LeerGastoAsync(db, _reloj.AhoraUtc, cancelacion));
        var enCurso = await Consulta(db).Where(e => e.Estado == EstadoEjecucion.EnCurso).OrderBy(e => e.Id).FirstOrDefaultAsync(cancelacion);
        var enCola = await db.EjecucionesAgente.CountAsync(e => e.Estado == EstadoEjecucion.EnCola, cancelacion);
        return new EstadoAgentesDto(disponibles, disponibles ? null : motivoNoDisponibles,
            version, _opciones.VersionProbada, version is not null && version != _opciones.VersionProbada,
            gasto, enCurso is null ? null : (await ADtosAsync(db, [enCurso], cancelacion))[0], enCola,
            _guardia.LlevaLaCola, _guardia.LlevaLaCola ? null : _guardia.Motivo);
    }

    private GastoHoyDto AGasto(GastoActual gasto)
    {
        var tope = _opciones.TopeGastoDiarioUsd;
        return new GastoHoyDto(gasto.Dia.Dia, gasto.Dia.Gastado, gasto.Reservado, tope,
            Math.Max(0m, tope - gasto.Dia.Gastado - gasto.Reservado), gasto.Dia.Ejecuciones);
    }

    private static IQueryable<EjecucionAgente> Consulta(AppDbContext db) =>
        db.EjecucionesAgente.AsNoTracking().Include(e => e.Candidatura).ThenInclude(c => c!.Empresa);

    /// <summary>
    /// El <c>estadoOferta</c> que AnalizarEncaje deja en su <c>ResultadoJson</c> (<see cref="AnalisisEncajeGuardado"/>);
    /// null en otras tareas, si no lo hay o si no se entiende.
    /// </summary>
    private static EstadoOferta? EstadoOfertaDe(EjecucionAgente e)
    {
        if (e.Tarea != TareaAgente.AnalizarEncaje || string.IsNullOrWhiteSpace(e.ResultadoJson)) return null;
        try
        {
            using var json = JsonDocument.Parse(e.ResultadoJson);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("estadoOferta", out var estado) || estado.ValueKind != JsonValueKind.String)
                return null;
            var texto = estado.GetString();
            return Enum.GetValues<EstadoOferta>().Where(v => v.ToString() == texto).Cast<EstadoOferta?>().FirstOrDefault();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<List<EjecucionAgenteDto>> ADtosAsync(AppDbContext db, List<EjecucionAgente> ejecuciones, CancellationToken cancelacion)
    {
        var ids = ejecuciones.Select(e => (int?)e.Id).ToList();
        var rastreos = await db.Rastreos.AsNoTracking()
            .Where(r => ids.Contains(r.EjecucionAgenteId))
            .Select(r => new { r.Id, r.EjecucionAgenteId })
            .ToListAsync(cancelacion);
        var rastreoDe = rastreos.GroupBy(r => r.EjecucionAgenteId!.Value).ToDictionary(g => g.Key, g => g.Min(r => r.Id));
        var conManejador = ejecuciones.Select(e => e.Tarea).Distinct().ToDictionary(t => t, _cola.TieneManejador);

        return ejecuciones.Select(e => new EjecucionAgenteDto(
            e.Id, e.Tarea, e.Estado, e.Espera, e.QuienBloquea,
            e.FechaAlta, e.Inicio, e.Fin,
            e.PresupuestoUsd, e.TiempoMaximoSegundos, e.CosteUsd,
            CosteEstimado: e.EsTerminal && e.CosteUsd is null && e.Inicio is not null,
            e.Resumen, e.Error, e.RegistroCambiado, e.PerfilCambiado,
            e.CandidaturaId,
            e.Candidatura is { } c ? new CandidaturaBreveDto(c.Id, c.Empresa.Nombre, c.Puesto, c.Estado, c.Enlace) : null,
            rastreoDe.TryGetValue(e.Id, out var rastreo) ? rastreo : null,
            EstadoOfertaDe(e),
            PuedeCancelar: e.Estado == EstadoEjecucion.EnCola || (e.Estado == EstadoEjecucion.EnCurso && !e.CancelacionPedida),
            PuedeReintentar: e.Estado is EstadoEjecucion.Error or EstadoEjecucion.Cancelada && conManejador[e.Tarea])).ToList();
    }
}
