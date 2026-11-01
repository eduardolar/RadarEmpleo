using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RadarEmpleo.Domain;

/// <summary>
/// Forma canónica de un texto para COMPARAR o BUSCAR ignorando mayúsculas y tildes
/// (no para guardar: lo que se muestra conserva su grafía original).
/// </summary>
public static partial class TextoComparable
{
    // Centinela de la zona de uso privado Unicode con el que se «blinda» la ñ antes de FormD.
    private const char CentinelaEnie = '';

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    /// <summary>
    /// Recorta, pasa a minúsculas (invariante), quita tildes y diéresis y deja un solo espacio entre
    /// palabras. La «ñ» se conserva como letra propia (igual que en Siessta): «Ñame» y «Name» son
    /// distintos. Devuelve "" si el texto es null o está vacío.
    /// </summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";

        // Primero se recompone (FormC): un texto que llega ya descompuesto (NFD, típico al copiar de
        // macOS o de algunos PDF) trae la «ñ» como «n» + virgulilla, y sin esto la virgulilla se
        // perdería con las tildes y «España» acabaría como «espana».
        var descompuesto = texto.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant()
            .Replace('ñ', CentinelaEnie)
            .Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var sinTildes = sb.ToString().Normalize(NormalizationForm.FormC).Replace(CentinelaEnie, 'ñ');
        return Espacios().Replace(sinTildes, " ");
    }

    /// <summary>¿Contiene <paramref name="origen"/> a <paramref name="busquedaNormalizada"/> (ya pasada por
    /// <see cref="Normalizar"/>)? Tolera null.</summary>
    public static bool Contiene(string? origen, string busquedaNormalizada) =>
        Normalizar(origen).Contains(busquedaNormalizada, StringComparison.Ordinal);
}
