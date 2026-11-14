using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Services.Estadisticas;

/// <summary>
/// Las cuentas de las estadísticas, sin base de datos ni reloj: reciben los datos ya leídos. Las
/// definiciones son las de <c>docs/F6-PLAN.md</c> (apartado 4.3) y no inventan datos.
/// </summary>
public static class CalculadoraEstadisticas
{
    /// <summary>¿Sigue viva? Todas menos las descartadas y las cerradas.</summary>
    public static bool EsActiva(EstadoCandidatura estado) => !ReglasEstado.EsFinal(estado);

    /// <summary>¿Pasar a este estado cuenta como respuesta de la empresa? Entrevista y Oferta.</summary>
    public static bool EsRespuesta(EstadoCandidatura estado) =>
        estado is EstadoCandidatura.Entrevista or EstadoCandidatura.Oferta;

    /// <summary>
    /// Tasa de respuesta: el porcentaje se redondea a entero y es null si no hay enviadas.
    /// </summary>
    /// <param name="enviadas">Candidaturas con fecha de envío.</param>
    /// <param name="conRespuesta">Candidaturas con algún paso a Entrevista u Oferta en el historial.</param>
    public static TasaRespuestaDto TasaRespuesta(int enviadas, int conRespuesta) => new(
        enviadas,
        conRespuesta,
        enviadas == 0 ? null : (int)Math.Round(100.0 * conRespuesta / enviadas, MidpointRounding.AwayFromZero));

    /// <summary>El lunes de la semana (de lunes a domingo) de <paramref name="dia"/>.</summary>
    public static DateOnly LunesDe(DateOnly dia) => dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7));

    /// <summary>
    /// Los siete días de la semana de <paramref name="hoy"/>, de lunes a domingo, con cuántos de los
    /// instantes de <paramref name="fechasUtc"/> caen en cada uno, contando el día en hora de Madrid.
    /// Los que caen fuera de la semana no cuentan.
    /// </summary>
    public static IReadOnlyList<DiaSemanaDto> Semana(DateOnly hoy, IEnumerable<DateTime> fechasUtc)
    {
        var porDia = fechasUtc
            .GroupBy(ReglasRecordatorio.DiaMadrid)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Count());
        var lunes = LunesDe(hoy);
        return Enumerable.Range(0, 7)
            .Select(i => lunes.AddDays(i))
            .Select(dia => new DiaSemanaDto(dia, porDia.GetValueOrDefault(dia)))
            .ToList();
    }
}
