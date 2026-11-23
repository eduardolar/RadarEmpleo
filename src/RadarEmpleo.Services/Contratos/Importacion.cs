using System.ComponentModel.DataAnnotations;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Contratos;

/// <summary>De dónde sale un lote. La F6 añadirá el texto libre.</summary>
public enum OrigenLote
{
    Tabla
}

/// <summary>Qué pasaría con una fila del lote al aplicarlo.</summary>
public enum ResultadoFila
{
    Nueva,
    AvanzaEstado,
    YaExiste,
    Conflicto
}

/// <summary>
/// Límites de un lote. Los de los campos son los mismos que los del alta manual; el analizador los
/// respeta para que lo que enseña la vista previa nunca dé un 400 al aplicarlo.
/// </summary>
public static class LimitesLote
{
    public const int VersionActual = 1;
    public const int Filas = 1000;
    public const int Empresa = 200;
    public const int Puesto = 300;
    public const int Enlace = 2000;
    public const int Portal = 100;
    public const int ModalidadDetalle = 300;
    public const int Ubicacion = 200;
    public const int MotivoDescarteTexto = 1000;
    public const int Nota = 5000;
}

/// <summary>Una candidatura leída del registro. <paramref name="Enlace"/> va tal cual se escribió:
/// se normaliza al comparar y al aplicar, y si no vale la fila se importa sin enlace y con aviso.</summary>
/// <param name="Linea">Línea del registro, empezando en 1.</param>
/// <param name="Fecha">Día del registro (AAAA-MM-DD).</param>
/// <param name="FechaAproximada">El registro solo dice «antes del DD-MM».</param>
/// <param name="EstadoDeducido">El estado sale de una palabra del medio del texto, no de cómo empieza:
/// la vista previa avisa para que se compruebe.</param>
public sealed record FilaLote(
    int Linea,
    DateOnly Fecha,
    bool FechaAproximada,
    [Required(ErrorMessage = "La empresa es obligatoria."), MaxLength(LimitesLote.Empresa, ErrorMessage = "La empresa no puede tener más de 200 caracteres.")] string Empresa,
    [Required(ErrorMessage = "El puesto es obligatorio."), MaxLength(LimitesLote.Puesto, ErrorMessage = "El puesto no puede tener más de 300 caracteres.")] string Puesto,
    Modalidad Modalidad = Modalidad.NoIndicada,
    [MaxLength(LimitesLote.ModalidadDetalle, ErrorMessage = "El detalle de la modalidad no puede tener más de 300 caracteres.")] string? ModalidadDetalle = null,
    [MaxLength(LimitesLote.Ubicacion, ErrorMessage = "La ubicación no puede tener más de 200 caracteres.")] string? Ubicacion = null,
    [MaxLength(LimitesLote.Portal, ErrorMessage = "El portal no puede tener más de 100 caracteres.")] string? Portal = null,
    [MaxLength(LimitesLote.Enlace, ErrorMessage = "El enlace no puede tener más de 2000 caracteres.")] string? Enlace = null,
    EstadoCandidatura Estado = EstadoCandidatura.Detectada,
    MotivoDescarte? MotivoDescarte = null,
    [MaxLength(LimitesLote.MotivoDescarteTexto, ErrorMessage = "El texto del motivo no puede tener más de 1000 caracteres.")] string? MotivoDescarteTexto = null,
    [MaxLength(LimitesLote.Nota, ErrorMessage = "La nota no puede tener más de 5000 caracteres.")] string? Nota = null,
    bool EstadoDeducido = false);

/// <summary>Lote de candidaturas leído del registro. Solo es interno: el servidor lo rehace al aplicar y nunca viaja.</summary>
public sealed record LoteImportacion(
    int Version,
    OrigenLote Origen,
    [Required(ErrorMessage = "Faltan las filas del lote."), MaxLength(LimitesLote.Filas, ErrorMessage = "El lote no puede tener más de 1000 filas.")] IReadOnlyList<FilaLote> Filas);

/// <summary>Cuántas filas hay de cada tipo en la vista previa.</summary>
/// <param name="EnviosEnTexto">Líneas con «ENVIADA» fuera de la tabla: solo se cuentan, no se importan.</param>
public sealed record ResumenImportacionDto(int Nuevas, int Avanzan, int YaExisten, int ParaRevisar, int SinLeer, int EnviosEnTexto);

/// <summary>Una fila de la vista previa, comparada con lo que ya hay en la app.</summary>
/// <param name="EstadoActual">Estado en la app, si ya hay una candidatura igual o parecida.</param>
/// <param name="CandidaturaId">Esa candidatura, si la hay.</param>
/// <param name="Motivo">Por qué es un conflicto, para enseñarlo tal cual.</param>
public sealed record FilaVistaPreviaDto(
    string Clave,
    ResultadoFila Resultado,
    string Empresa,
    string Puesto,
    DateOnly Fecha,
    EstadoCandidatura EstadoRegistro,
    EstadoCandidatura? EstadoActual,
    int? CandidaturaId,
    string? Motivo,
    IReadOnlyList<string> Avisos);

/// <summary>Una fila de la tabla que no se ha podido leer.</summary>
public sealed record FilaSinLeerDto(int Linea, string Motivo);

/// <summary>Una fila extraída que se ha quedado fuera del lote porque ya estaba, con el aviso para enseñarlo tal cual.</summary>
/// <param name="Linea">Línea de su <c>cita</c> en el registro, empezando en 1.</param>
public sealed record FilaFueraDelLote(int Linea, string Empresa, string Puesto, DateOnly Fecha, string Aviso);

/// <summary>Respuesta de la vista previa. No se ha escrito nada.</summary>
/// <param name="HashRegistro">SHA-256 (hexadecimal en minúsculas) de los bytes del registro leído: aplicar lo exige igual.</param>
/// <param name="EjecucionId">La extracción de Claude de la que sale el lote, o null con la tabla.</param>
/// <param name="Fuera">Las extraídas que se quedan fuera porque ya estaban; vacía con la tabla.</param>
public sealed record VistaPreviaImportacionDto(
    OrigenLote Origen,
    string HashRegistro,
    int? EjecucionId,
    ResumenImportacionDto Resumen,
    IReadOnlyList<FilaVistaPreviaDto> Filas,
    IReadOnlyList<FilaSinLeerDto> SinLeer,
    IReadOnlyList<FilaFueraDelLote> Fuera);

/// <summary>
/// Petición de aplicar: de dónde sale el lote, el hash del registro de la vista previa y las claves sobre las que actuar.
/// El cliente no manda filas: el servidor vuelve a leer el registro y rehace el lote.
/// </summary>
/// <param name="EjecucionId">La extracción de Claude; con la tabla no va.</param>
public sealed record AplicarImportacionRequest(
    [Required(ErrorMessage = "Falta el origen.")] OrigenLote? Origen,
    int? EjecucionId,
    [Required(ErrorMessage = "Falta el hash del registro."),
     StringLength(64, MinimumLength = 64, ErrorMessage = "El hash del registro tiene que tener 64 caracteres.")] string HashRegistro,
    [Required(ErrorMessage = "Faltan las claves."),
     MaxLength(LimitesLote.Filas, ErrorMessage = "No puede haber más de 1000 claves.")] IReadOnlyList<string> Claves);

/// <summary>
/// Resultado de aplicar. Las cuatro cifras suman todas las filas del lote.
/// </summary>
/// <param name="Creadas">Candidaturas añadidas.</param>
/// <param name="Avanzadas">Candidaturas que han cambiado de estado.</param>
/// <param name="SinCambios">Filas que ya existían o que no estaban en las claves pedidas.</param>
/// <param name="ParaRevisar">Conflictos del lote, pedidos o no: se saltan y se cuentan.</param>
/// <param name="RadarActualizado">Si se ha podido escribir <c>radar\candidaturas.md</c>.</param>
public sealed record ResultadoAplicacionDto(int Creadas, int Avanzadas, int SinCambios, int ParaRevisar, bool RadarActualizado);
