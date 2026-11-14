using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Services.Estadisticas;

/// <summary>
/// Cifras de la búsqueda. Solo lee: saca de la base de datos lo justo (son decenas de filas) y hace las
/// cuentas con <see cref="CalculadoraEstadisticas"/>. Cada operación abre su propio contexto.
/// </summary>
public class EstadisticasService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _reloj;

    public EstadisticasService(IDbContextFactory<AppDbContext> factory, IClock reloj)
    {
        _factory = factory;
        _reloj = reloj;
    }

    /// <summary>
    /// El resumen de Hoy: candidaturas activas, entrevistas pendientes de esta semana, tasa de respuesta y los
    /// pendientes de cada día de la semana, más el día del primer pendiente posterior a ella. La semana va de lunes a domingo en hora de Madrid y sus
    /// cortes salen de la medianoche de Madrid (no de sumar 24 h, que falla al cambiar la hora).
    /// </summary>
    public async Task<ResumenHoyDto> ResumenHoyAsync()
    {
        var hoy = ReglasRecordatorio.DiaMadrid(_reloj.AhoraUtc);
        var lunes = CalculadoraEstadisticas.LunesDe(hoy);
        var inicio = Medianoche(lunes);
        var fin = Medianoche(lunes.AddDays(7));

        await using var db = await _factory.CreateDbContextAsync();

        var candidaturas = await db.Candidaturas.AsNoTracking()
            .Select(c => new { c.Estado, c.FechaEnvio })
            .ToListAsync();
        var pasos = await db.CambiosEstado.AsNoTracking()
            .Select(h => new { h.CandidaturaId, h.A })
            .ToListAsync();
        // Como en el panel: los de una candidatura descartada o cerrada no cuentan mientras siga así.
        // Solo lo pendiente: es lo que enseñan el calendario y la lista al pulsar las cifras
        var pendientesSemana = await db.Recordatorios.AsNoTracking()
            .Where(ReglasRecordatorio.SeEnsena)
            .Where(r => !r.Hecho && r.FechaHora >= inicio && r.FechaHora < fin)
            .Select(r => new { r.Tipo, r.FechaHora })
            .ToListAsync();

        // El primer pendiente pasada la semana, para que el calendario pueda decir qué es lo siguiente
        var siguiente = await db.Recordatorios.AsNoTracking()
            .Where(ReglasRecordatorio.SeEnsena)
            .Where(r => !r.Hecho && r.FechaHora >= fin)
            .Select(r => (DateTime?)r.FechaHora)
            .MinAsync();

        return new ResumenHoyDto(
            candidaturas.Count(c => CalculadoraEstadisticas.EsActiva(c.Estado)),
            // Las que quedan por hacer: el mismo conjunto que «Entrevistas» + «esta semana» en la lista
            pendientesSemana.Count(r => r.Tipo == TipoRecordatorio.Entrevista),
            CalculadoraEstadisticas.TasaRespuesta(
                candidaturas.Count(c => c.FechaEnvio is not null),
                pasos.Where(p => CalculadoraEstadisticas.EsRespuesta(p.A)).Select(p => p.CandidaturaId).Distinct().Count()),
            CalculadoraEstadisticas.Semana(hoy, pendientesSemana.Select(r => r.FechaHora)),
            siguiente is { } fecha ? ReglasRecordatorio.DiaMadrid(fecha) : null);
    }

    private static DateTime Medianoche(DateOnly dia) => Fechas.MedianocheMadridEnUtc(dia.ToDateTime(TimeOnly.MinValue));
}
