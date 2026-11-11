using System.Globalization;
using System.Text.RegularExpressions;

namespace RadarEmpleo.Domain;

/// <summary>
/// Nombre de la carpeta semanal de <c>Curriculum\Candidaturas\</c> en la que el equipo guarda los CV
/// de cada semana (de lunes a domingo): «Semana 2026-11-02 (2 - 8 nov)» o, si cambia de mes,
/// «Semana 2026-10-26 (26 oct - 1 nov)». El año es el del lunes y no se repite entre paréntesis.
/// </summary>
public static partial class CarpetasSemana
{
    /// <summary>Prefijo común de todas las carpetas de semana.</summary>
    public const string Prefijo = "Semana ";

    // Array fijo a propósito: CultureInfo("es-ES") con ICU abrevia septiembre como «sept.».
    private static readonly string[] Meses = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    [GeneratedRegex(@"^Semana (\d{4}-\d{2}-\d{2}) \(", RegexOptions.CultureInvariant)]
    private static partial Regex FechaDelNombre();

    /// <summary>Carpeta de la semana (de lunes a domingo) que contiene <paramref name="dia"/>.</summary>
    public static string Nombre(DateOnly dia)
    {
        var lunes = dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7));
        var domingo = lunes.AddDays(6);
        var rango = lunes.Month == domingo.Month
            ? $"{lunes.Day} - {domingo.Day} {Mes(domingo)}"
            : $"{lunes.Day} {Mes(lunes)} - {domingo.Day} {Mes(domingo)}";
        return $"{Prefijo}{lunes.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} ({rango})";
    }

    /// <summary>Carpeta de la semana de un instante UTC, contando el día en hora de Madrid.</summary>
    public static string Nombre(DateTime utc) => Nombre(DateOnly.FromDateTime(Fechas.UtcAMadrid(utc)));

    /// <summary>Lunes que da nombre a la carpeta, o null si el nombre no tiene el formato de una carpeta de semana.</summary>
    public static DateOnly? Lunes(string? nombreCarpeta)
    {
        var encontrado = FechaDelNombre().Match(nombreCarpeta ?? "");
        return encontrado.Success && DateOnly.TryParseExact(encontrado.Groups[1].Value, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var lunes)
            ? lunes
            : null;
    }

    private static string Mes(DateOnly dia) => Meses[dia.Month - 1];
}
