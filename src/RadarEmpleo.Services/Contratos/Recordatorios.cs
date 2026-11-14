using System.ComponentModel.DataAnnotations;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Contratos;

/// <summary>
/// Datos de un recordatorio, para crearlo (suelto o de una candidatura) y para editarlo. La candidatura
/// va en la URL, nunca en el cuerpo. <paramref name="Tipo"/> y <paramref name="Dia"/> son obligatorios
/// (son nullable solo para que un <c>{}</c> dé 400 en vez de tomar el primer valor).
/// </summary>
/// <param name="Dia">Día de Madrid, como «2026-11-11».</param>
/// <param name="Hora">Hora de Madrid como «15:00», que es como la da el campo de hora del navegador
/// (por eso es texto y no <see cref="TimeOnly"/>, que en JSON sería «15:00:00»). Vacía, las 09:00, salvo
/// en una entrevista, que la exige.</param>
public sealed record GuardarRecordatorioRequest(
    [Required(ErrorMessage = "Elige qué tipo de recordatorio es.")] TipoRecordatorio? Tipo,
    [Required(ErrorMessage = "El título es obligatorio."), MaxLength(ReglasRecordatorio.LargoTitulo, ErrorMessage = "El título no puede tener más de 200 caracteres.")] string Titulo,
    [Required(ErrorMessage = "El día es obligatorio.")] DateOnly? Dia,
    string? Hora = null,
    [MaxLength(ReglasRecordatorio.LargoNotas, ErrorMessage = "Las notas no pueden tener más de 2000 caracteres.")] string? Notas = null);

/// <summary>Marca un recordatorio como hecho (<c>true</c>) o lo deja pendiente otra vez (<c>false</c>).</summary>
public sealed record MarcarHechoRequest([Required(ErrorMessage = "Falta decir si está hecho.")] bool? Hecho);

/// <summary>Pasa un recordatorio a otro día de Madrid, a la misma hora.</summary>
public sealed record PosponerRecordatorioRequest([Required(ErrorMessage = "Falta el día.")] DateOnly? Dia);

/// <summary>Lo justo de una candidatura para enseñarla junto a su recordatorio.</summary>
/// <param name="Enlace">La URL de la oferta, para abrirla desde la tarjeta; null si no la tiene.</param>
public sealed record CandidaturaBreveDto(int Id, string Empresa, string Puesto, EstadoCandidatura Estado, string? Enlace);

/// <summary>Un recordatorio en Hoy, con su candidatura si la tiene.</summary>
/// <param name="CerradoAlPasarA">Solo en «Hechos hoy»: si es un seguimiento que se dio por hecho solo al
/// mover la candidatura (a Entrevista u Oferta), el estado al que pasó; null si lo marcó el usuario.</param>
public sealed record RecordatorioAgendaDto(int Id, int? CandidaturaId, TipoRecordatorio Tipo, string Titulo,
    DateTime FechaHora, bool Hecho, DateTime? FechaHecho, string? Notas, CandidaturaBreveDto? Candidatura,
    EstadoCandidatura? CerradoAlPasarA = null);

/// <summary>Candidatura enviada que sigue sin respuesta y sin seguimiento pendiente.</summary>
/// <param name="DiasDesdeEnvio">Días naturales de Madrid desde el envío.</param>
public sealed record EnviadaSinRespuestaDto(int Id, string Empresa, string Puesto, DateTime FechaEnvio, int DiasDesdeEnvio);

/// <summary>
/// Lo que pinta la pantalla Hoy. Los cortes de día son los de Madrid. Los recordatorios de candidaturas
/// descartadas o cerradas no salen en ninguna lista.
/// </summary>
/// <param name="Hoy">Día de Madrid según el servidor: el front no lo calcula.</param>
/// <param name="ParaHoy">Pendientes de hoy y vencidos, por fecha.</param>
/// <param name="Proximos">Pendientes de los 7 días siguientes a hoy.</param>
/// <param name="MasAdelante">Pendientes a partir del octavo día.</param>
/// <param name="HechosHoy">Los que se han marcado como hechos hoy, el más reciente primero.</param>
/// <param name="SinRespuesta">Enviadas hace entre 7 y 30 días, de la más reciente a la más antigua.</param>
/// <param name="SinRespuestaAntiguas">Cuántas enviadas llevan más de 30 días sin respuesta.</param>
/// <param name="Resumen">Las cifras y la semana que acompañan a la lista.</param>
public sealed record PanelHoyDto(
    DateOnly Hoy,
    IReadOnlyList<RecordatorioAgendaDto> ParaHoy,
    IReadOnlyList<RecordatorioAgendaDto> Proximos,
    IReadOnlyList<RecordatorioAgendaDto> MasAdelante,
    IReadOnlyList<RecordatorioAgendaDto> HechosHoy,
    IReadOnlyList<EnviadaSinRespuestaDto> SinRespuesta,
    int SinRespuestaAntiguas,
    ResumenHoyDto Resumen);
