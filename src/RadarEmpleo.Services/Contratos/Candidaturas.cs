using System.ComponentModel.DataAnnotations;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Contratos;

/// <summary>Filtros del listado de candidaturas (todos opcionales).</summary>
/// <param name="Estado">Solo las que están en este estado.</param>
/// <param name="Texto">Busca en puesto, empresa, ubicación, portal y pega, sin distinguir mayúsculas ni tildes.</param>
/// <param name="Desde">Solo las detectadas a partir de este instante. Una fecha sin hora ni zona
/// (<see cref="DateTimeKind.Unspecified"/> a las 00:00) se toma como medianoche de Madrid; el resto, como UTC.</param>
public sealed record FiltroCandidaturas(EstadoCandidatura? Estado = null, string? Texto = null, DateTime? Desde = null);

/// <summary>Fila del listado y del tablero.</summary>
/// <param name="FechaEstado">Cuándo entró en el estado actual (UTC). Es la que debe usar el front para
/// los «días en el estado»: en el alta es la fecha de detección y luego solo cambia con los cambios de
/// estado, no al editar datos.</param>
/// <param name="FechaUltimoCambio">Última modificación de cualquier tipo (también editar datos); ordena
/// el listado. NO sirve para los «días en el estado».</param>
public sealed record CandidaturaResumenDto(
    int Id,
    int EmpresaId,
    string Empresa,
    string Puesto,
    string? Enlace,
    string? Portal,
    Modalidad Modalidad,
    string? Ubicacion,
    Encaje Encaje,
    string? Pega,
    EstadoCandidatura Estado,
    DateTime FechaDeteccion,
    DateTime? FechaEnvio,
    DateTime FechaEstado,
    DateTime FechaUltimoCambio,
    MotivoDescarte? MotivoDescarte,
    Origen Origen);

/// <summary>Ficha completa: datos, historial (del más antiguo al más reciente), notas (de la más
/// reciente a la más antigua), recordatorios (por fecha), documentos (por tipo, el vigente primero) y
/// los estados a los que se puede mover.</summary>
/// <param name="FechaEstado">Cuándo entró en el estado actual (UTC). Es la que debe usar el front para
/// los «días en el estado»: en el alta es la fecha de detección y luego solo cambia con los cambios de
/// estado, no al editar datos.</param>
/// <param name="FechaUltimoCambio">Última modificación de cualquier tipo (también editar datos). NO sirve
/// para los «días en el estado».</param>
/// <param name="HayCarpetaCurriculum">Si la carpeta Curriculum está ahora en su sitio. Sin ella no se
/// encuentra ningún documento, y la ficha lo dice una vez en vez de en cada uno.</param>
public sealed record CandidaturaDetalleDto(
    int Id,
    int EmpresaId,
    string Empresa,
    string Puesto,
    string? Enlace,
    string? Portal,
    Modalidad Modalidad,
    string? ModalidadDetalle,
    string? Ubicacion,
    Encaje Encaje,
    string? Pega,
    string? Analisis,
    string? Salario,
    EstadoCandidatura Estado,
    DateTime FechaDeteccion,
    DateTime? FechaEnvio,
    DateTime FechaEstado,
    DateTime FechaUltimoCambio,
    MotivoDescarte? MotivoDescarte,
    string? MotivoDescarteTexto,
    Origen Origen,
    string? SeccionOrigen,
    IReadOnlyList<EstadoCandidatura> EstadosPosibles,
    IReadOnlyList<CambioEstadoDto> Historial,
    IReadOnlyList<NotaDto> Notas,
    IReadOnlyList<RecordatorioDto> Recordatorios,
    IReadOnlyList<DocumentoDto> Documentos,
    bool HayCarpetaCurriculum);

/// <summary>Apunte del historial de estados (<paramref name="De"/> es null en el alta).</summary>
public sealed record CambioEstadoDto(int Id, EstadoCandidatura? De, EstadoCandidatura A, DateTime Fecha, string? Comentario, Origen Origen);

public sealed record NotaDto(int Id, int CandidaturaId, string Texto, DateTime Fecha, Origen Origen);

public sealed record RecordatorioDto(int Id, int? CandidaturaId, TipoRecordatorio Tipo, string Titulo, DateTime FechaHora, bool Hecho, string? Notas);

/// <summary>
/// Datos de una candidatura para crearla o editarla. La empresa va por NOMBRE: se reutiliza la que
/// tenga el mismo nombre normalizado o se crea una nueva. El estado no se toca aquí (va por
/// <c>POST /candidaturas/{id}/estado</c>). Si no se envían, <paramref name="Modalidad"/>,
/// <paramref name="Encaje"/> y <paramref name="Origen"/> toman a propósito el valor «sin dato»
/// (NoIndicada, SinValorar y Manual): omitirlos es legítimo y no cambia ningún estado.
/// </summary>
public sealed record GuardarCandidaturaRequest(
    [Required(ErrorMessage = "La empresa es obligatoria."), MaxLength(200, ErrorMessage = "La empresa no puede tener más de 200 caracteres.")] string Empresa,
    [Required(ErrorMessage = "El puesto es obligatorio."), MaxLength(300, ErrorMessage = "El puesto no puede tener más de 300 caracteres.")] string Puesto,
    [MaxLength(2000, ErrorMessage = "El enlace no puede tener más de 2000 caracteres.")] string? Enlace = null,
    [MaxLength(100, ErrorMessage = "El portal no puede tener más de 100 caracteres.")] string? Portal = null,
    Modalidad Modalidad = Modalidad.NoIndicada,
    [MaxLength(300, ErrorMessage = "El detalle de la modalidad no puede tener más de 300 caracteres.")] string? ModalidadDetalle = null,
    [MaxLength(200, ErrorMessage = "La ubicación no puede tener más de 200 caracteres.")] string? Ubicacion = null,
    Encaje Encaje = Encaje.SinValorar,
    [MaxLength(2000, ErrorMessage = "La pega no puede tener más de 2000 caracteres.")] string? Pega = null,
    string? Analisis = null,
    [MaxLength(200, ErrorMessage = "El salario no puede tener más de 200 caracteres.")] string? Salario = null,
    DateTime? FechaDeteccion = null,
    Origen Origen = Origen.Manual);

/// <summary>
/// Petición de cambio de estado. <paramref name="Estado"/> es obligatorio (es nullable solo para que
/// un <c>{}</c> dé 400 en vez de tomarse como «Detectada», el primer valor del enum).
/// <paramref name="MotivoDescarte"/> es obligatorio al descartar.
/// </summary>
public sealed record CambiarEstadoRequest(
    [Required(ErrorMessage = "El estado es obligatorio.")] EstadoCandidatura? Estado,
    MotivoDescarte? MotivoDescarte = null,
    [MaxLength(1000, ErrorMessage = "El texto del motivo no puede tener más de 1000 caracteres.")] string? MotivoDescarteTexto = null,
    [MaxLength(1000, ErrorMessage = "El comentario no puede tener más de 1000 caracteres.")] string? Comentario = null,
    Origen Origen = Origen.Manual);

/// <summary>
/// Petición de deshacer un cambio de estado. <paramref name="CambioId"/> es el apunte del historial que
/// creó el movimiento (el front lo saca de la respuesta del cambio): si ya no es el último, no se deshace.
/// </summary>
public sealed record DeshacerCambioEstadoRequest(
    [Required(ErrorMessage = "Falta el cambio que se quiere deshacer.")] int? CambioId);

public sealed record CrearNotaRequest(
    [Required(ErrorMessage = "El texto de la nota es obligatorio."), MaxLength(5000, ErrorMessage = "La nota no puede tener más de 5000 caracteres.")] string Texto,
    Origen Origen = Origen.Manual);

/// <summary>Empresa para el autocompletado.</summary>
public sealed record EmpresaDto(int Id, string Nombre, string? Web, int Candidaturas);
