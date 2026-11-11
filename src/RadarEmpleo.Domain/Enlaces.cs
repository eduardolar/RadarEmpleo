using System.Text.RegularExpressions;

namespace RadarEmpleo.Domain;

/// <summary>
/// Normalización de enlaces a ofertas. Es la versión C# de <c>normalizar-enlace.ts</c> del front: la
/// API también tiene que comprobarlos porque los lotes de importación y Scalar no pasan por el formulario.
/// </summary>
public static partial class Enlaces
{
    // «mailto:», «ftp://»... Lo que va seguido de un número es un puerto, no un esquema («acme.com:8080»).
    [GeneratedRegex(@"^[a-z][a-z0-9+.-]*:(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OtroEsquema();

    [GeneratedRegex(@"^https?://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EsquemaWeb();

    /// <summary>
    /// Deja el enlace listo para guardar: sin esquema, el navegador abriría «www.linkedin.com/jobs/1»
    /// como una ruta de la propia app, así que se le pone «https://». Devuelve "" si no hay enlace y
    /// null si lo escrito no es una dirección web (otro esquema como mailto:, o un texto que no es un
    /// dominio).
    /// </summary>
    public static string? Normalizar(string? enlace)
    {
        var limpio = enlace?.Trim() ?? "";
        if (limpio.Length == 0) return "";

        if (EsquemaWeb().IsMatch(limpio))
            return AUrl(limpio) is null ? null : limpio;
        if (OtroEsquema().IsMatch(limpio))
            return null;

        var conEsquema = $"https://{limpio.TrimStart('/')}";
        return AUrl(conEsquema)?.Host.Contains('.') == true ? conEsquema : null;
    }

    /// <summary>
    /// Forma para COMPARAR (no para guardar) si dos enlaces son la misma oferta: normalizado, sin la «/»
    /// final, con «https» y en minúsculas. Null si no hay enlace o no es una dirección web.
    /// </summary>
    public static string? ParaComparar(string? enlace)
    {
        var normalizado = Normalizar(enlace);
        if (string.IsNullOrEmpty(normalizado)) return null;

        var sinBarra = normalizado.TrimEnd('/').ToLowerInvariant();
        return sinBarra.StartsWith("http://", StringComparison.Ordinal) ? "https://" + sinBarra[7..] : sinBarra;
    }

    private static Uri? AUrl(string texto) =>
        Uri.TryCreate(texto, UriKind.Absolute, out var url) && url.Host.Length > 0 ? url : null;
}
