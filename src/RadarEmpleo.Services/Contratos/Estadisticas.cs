namespace RadarEmpleo.Services.Contratos;

/// <summary>
/// Cuántas candidaturas enviadas han tenido respuesta. <paramref name="Enviadas"/> son las que tienen
/// fecha de envío; <paramref name="ConRespuesta"/>, las que tienen algún paso a Entrevista u Oferta en
/// el historial, aunque luego se descartaran.
/// </summary>
/// <param name="Porcentaje">Redondeado a entero; null si no hay ninguna enviada (no se inventa un 0 %).</param>
public sealed record TasaRespuestaDto(int Enviadas, int ConRespuesta, int? Porcentaje);

/// <summary>Un día de la semana del resumen, con sus recordatorios pendientes.</summary>
/// <param name="Dia">Día de Madrid.</param>
public sealed record DiaSemanaDto(DateOnly Dia, int Recordatorios);

/// <summary>
/// El resumen que acompaña a Hoy. La semana va de lunes a domingo en hora de Madrid. Como en el panel,
/// no cuentan los recordatorios de candidaturas descartadas o cerradas.
/// </summary>
/// <param name="CandidaturasActivas">Las que no están descartadas ni cerradas.</param>
/// <param name="EntrevistasSemana">Recordatorios de tipo Entrevista de esta semana que siguen pendientes
/// (también los de un día ya pasado de la semana sin marcar como hechos).</param>
/// <param name="Semana">Los siete días de esta semana, de lunes a domingo, con sus pendientes.</param>
/// <param name="SiguienteDia">Día de Madrid del primer pendiente posterior a esta semana (a partir del
/// lunes que viene); null si no hay ninguno.</param>
public sealed record ResumenHoyDto(
    int CandidaturasActivas,
    int EntrevistasSemana,
    TasaRespuestaDto Respuesta,
    IReadOnlyList<DiaSemanaDto> Semana,
    DateOnly? SiguienteDia);
