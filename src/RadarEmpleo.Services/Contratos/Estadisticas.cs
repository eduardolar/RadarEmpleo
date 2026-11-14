using RadarEmpleo.Domain;

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

/// <summary>
/// Cómo va la búsqueda, para la página de Estadísticas. Solo cuenta lo que está en la app; las
/// definiciones de cada cifra van en sus tipos.
/// </summary>
/// <param name="Total">Todas las candidaturas, también las descartadas y las cerradas.</param>
/// <param name="PorEstado">Los siete estados, en el orden del tablero, también los que tienen 0.</param>
/// <param name="EnviadasPorSemana">Las 8 últimas semanas (de lunes a domingo, en hora de Madrid), la actual la
/// última y con ceros.</param>
/// <param name="MotivosDescarte">Solo las que están descartadas ahora: los motivos con algún caso, de más a menos.</param>
public sealed record EstadisticasDto(
    int Total,
    IReadOnlyList<TotalPorEstadoDto> PorEstado,
    IReadOnlyList<EnviadasSemanaDto> EnviadasPorSemana,
    RespuestaDto Respuesta,
    IReadOnlyList<TotalPorMotivoDto> MotivosDescarte);

/// <summary>Cuántas candidaturas hay ahora en un estado.</summary>
public sealed record TotalPorEstadoDto(EstadoCandidatura Estado, int Total);

/// <summary>
/// Cuántas se enviaron en una semana: cada apunte del historial que pasa a Enviada cuenta en la semana de
/// Madrid en que se hizo, así que un reenvío tras reabrir cuenta otra vez. Las importadas que ya estaban en
/// Entrevista no tienen ese apunte y no cuentan.
/// </summary>
/// <param name="Lunes">El lunes de la semana.</param>
public sealed record EnviadasSemanaDto(DateOnly Lunes, int Total);

/// <summary>
/// Las respuestas de las empresas. <paramref name="Enviadas"/>, <paramref name="ConRespuesta"/> y
/// <paramref name="Porcentaje"/> son los de <see cref="TasaRespuestaDto"/>.
/// </summary>
/// <param name="DiasMedios">Días naturales de Madrid entre el último paso a Enviada y el primer paso a
/// Entrevista u Oferta que va después, de media y con un decimal. Solo cuentan las candidaturas que tienen
/// los dos pasos en ese orden: las importadas que saltan directamente a Entrevista no, porque su fecha de
/// envío es la de la entrevista. Null si no hay ningún caso.</param>
/// <param name="CasosDias">Sobre cuántas candidaturas se calcula <paramref name="DiasMedios"/>.</param>
public sealed record RespuestaDto(int Enviadas, int ConRespuesta, int? Porcentaje, double? DiasMedios, int CasosDias);

/// <summary>Cuántas de las descartadas ahora lo están por un motivo.</summary>
public sealed record TotalPorMotivoDto(MotivoDescarte Motivo, int Total);
