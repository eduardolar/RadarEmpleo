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

    /// <summary>Cuántas hay en cada uno de los siete estados, en el orden del tablero y con ceros.</summary>
    public static IReadOnlyList<TotalPorEstadoDto> PorEstado(IEnumerable<EstadoCandidatura> estados)
    {
        var porEstado = estados.GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());
        return Enum.GetValues<EstadoCandidatura>()
            .Select(estado => new TotalPorEstadoDto(estado, porEstado.GetValueOrDefault(estado)))
            .ToList();
    }

    /// <summary>
    /// Las <paramref name="semanas"/> últimas semanas hasta la de <paramref name="hoy"/> (la última), con
    /// cuántos de los pasos a Enviada de <paramref name="enviosUtc"/> caen en cada una, en hora de Madrid.
    /// Los de antes o de después no cuentan.
    /// </summary>
    public static IReadOnlyList<EnviadasSemanaDto> EnviadasPorSemana(DateOnly hoy, IEnumerable<DateTime> enviosUtc, int semanas = 8)
    {
        var porLunes = enviosUtc
            .GroupBy(fecha => LunesDe(ReglasRecordatorio.DiaMadrid(fecha)))
            .ToDictionary(g => g.Key, g => g.Count());
        var primero = LunesDe(hoy).AddDays(-7 * (semanas - 1));
        return Enumerable.Range(0, semanas)
            .Select(i => primero.AddDays(7 * i))
            .Select(lunes => new EnviadasSemanaDto(lunes, porLunes.GetValueOrDefault(lunes)))
            .ToList();
    }

    /// <summary>
    /// Días naturales de Madrid que tardó en responder una candidatura: del último paso a Enviada anterior
    /// al primer paso a Entrevista u Oferta. Null si no hay respuesta o si no hay ningún paso a Enviada antes
    /// de ella (las importadas que saltan directamente a Entrevista).
    /// </summary>
    /// <param name="historial">Los pasos de una candidatura, ya en orden (por fecha y, a igual fecha, por id).</param>
    public static int? DiasHastaRespuesta(IEnumerable<(EstadoCandidatura A, DateTime FechaUtc)> historial)
    {
        DateTime? ultimoEnvio = null;
        foreach (var (a, fecha) in historial)
        {
            if (EsRespuesta(a))
            {
                return ultimoEnvio is { } envio
                    ? ReglasRecordatorio.DiaMadrid(fecha).DayNumber - ReglasRecordatorio.DiaMadrid(envio).DayNumber
                    : null;
            }
            if (a == EstadoCandidatura.Enviada) ultimoEnvio = fecha;
        }
        return null;
    }

    /// <summary>
    /// La tasa de <see cref="TasaRespuesta"/> más los días medios, con un decimal (null si no hay
    /// <paramref name="dias"/>).
    /// </summary>
    public static RespuestaDto Respuesta(int enviadas, int conRespuesta, IReadOnlyCollection<int> dias)
    {
        var tasa = TasaRespuesta(enviadas, conRespuesta);
        double? media = dias.Count == 0 ? null : Math.Round(dias.Average(), 1, MidpointRounding.AwayFromZero);
        return new RespuestaDto(tasa.Enviadas, tasa.ConRespuesta, tasa.Porcentaje, media, dias.Count);
    }

    /// <summary>
    /// Los motivos con algún caso, de más a menos; a igualdad, en el orden de <see cref="MotivoDescarte"/>.
    /// </summary>
    public static IReadOnlyList<TotalPorMotivoDto> MotivosDescarte(IEnumerable<MotivoDescarte> motivos) =>
        motivos.GroupBy(m => m)
            .Select(g => new TotalPorMotivoDto(g.Key, g.Count()))
            .OrderByDescending(m => m.Total)
            .ThenBy(m => m.Motivo)
            .ToList();
}
