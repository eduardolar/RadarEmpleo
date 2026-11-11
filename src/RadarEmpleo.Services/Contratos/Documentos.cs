using System.ComponentModel.DataAnnotations;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Contratos;

/// <summary>Por qué se sugiere un documento para una candidatura.</summary>
public enum CoincidenciaSugerencia
{
    /// <summary>El nombre del archivo lleva el nombre completo de la empresa.</summary>
    Empresa,
    /// <summary>El nombre del archivo lleva la palabra más distintiva de la empresa.</summary>
    Parcial,
    /// <summary>No hay ninguno con la empresa: es de la carpeta de la semana de la candidatura.</summary>
    MismaSemana
}

/// <summary>Documento vinculado a una candidatura, como sale en la ficha.</summary>
/// <param name="Carpeta">Carpeta que lo contiene, con «/» («» para la raíz de Curriculum).</param>
/// <param name="Extension">«pdf» o «docx».</param>
/// <param name="Encontrado">Si el archivo está ahora mismo en su sitio (se mira al pedir la ficha).</param>
public sealed record DocumentoDto(
    int Id,
    int CandidaturaId,
    TipoDocumento Tipo,
    string NombreArchivo,
    string RutaRelativa,
    string Carpeta,
    string Extension,
    bool Encontrado,
    OrigenDocumento Origen,
    bool Vigente,
    DateTime FechaAlta);

/// <summary>Archivo de Curriculum que se propone vincular a una candidatura.</summary>
/// <param name="EsAntiguo">Está en <c>Archivo\</c>.</param>
/// <param name="YaVinculado">Ya está en la ficha de esta candidatura.</param>
/// <param name="PosteriorAlEnvio">Es de una semana posterior al envío: no puede ser el CV que se mandó.</param>
public sealed record DocumentoSugeridoDto(
    string RutaRelativa,
    string NombreArchivo,
    string Carpeta,
    TipoDocumento Tipo,
    string Extension,
    DateTime FechaModificacion,
    CoincidenciaSugerencia Coincidencia,
    bool EsAntiguo,
    bool YaVinculado,
    bool PosteriorAlEnvio);

/// <summary>
/// Vincular un archivo de Curriculum a la ficha. Si no se indica <paramref name="Tipo"/>, se deduce del
/// nombre («CV_…» es CV, «Carta_…» es carta…).
/// </summary>
public sealed record VincularDocumentoRequest(
    [Required(ErrorMessage = "Falta la ruta del documento."),
     MaxLength(Documento.LargoRutaRelativa, ErrorMessage = "La ruta del documento no puede tener más de 500 caracteres.")]
    string RutaRelativa,
    TipoDocumento? Tipo = null);
