using System.Text.RegularExpressions;

namespace RadarEmpleo.Domain;

/// <summary>
/// Normalización de nombres de empresa para no duplicarlas: «Inditec Sistemas, S.A.»,
/// «INDITEC SISTEMAS SA» e «Inditec  Sistemas» colapsan en «inditec sistemas».
/// </summary>
public static partial class NombresEmpresa
{
    // Forma jurídica al FINAL del nombre (se aplica después de quitar tildes y pasar a minúsculas):
    //  - españolas: S.L., S.L.U., S.L.L., S.L.P., S.L.N.E., S.A., S.A.U., S.A.L., S.Coop., S.Coop. And.,
    //    «sociedad limitada/anónima (unipersonal/laboral/profesional)»;
    //  - extranjeras habituales en ofertas: Ltd, Inc, LLC, GmbH, B.V., S.p.A., SAS.
    // Va precedida de espacio o coma, o entre paréntesis («Acme (S.L.)»). Exigir separador evita
    // comerse el final de «Nasa», «Visa», «Sasa», «Inca» o «Saltec». Las formas que coinciden con
    // palabras corrientes solo se quitan con puntos: «S.A.L.» (no «Sal»: «Mar y Sal») y «S.p.A.»
    // (no «Spa»: «Hotel Spa»).
    private const string Formas =
        @"s\.?\s?l\.?(\s?[ulp]\.?|\s?n\.?\s?e\.?)?" +                   // S.L., S.L.U., S.L.L., S.L.P., S.L.N.E.
        @"|s\.?\s?a\.?(\s?u\.?)?" +                                     // S.A., S.A.U.
        @"|s\.\s?a\.\s?l\.?" +                                          // S.A.L. (con puntos)
        @"|s\.?\s?a\.?\s?s\.?" +                                        // SAS, S.A.S.
        @"|s\.?\s?coop\.?(\s?and\.?)?" +                                // S.Coop., S.Coop. And.
        @"|s\.\s?p\.\s?a\.?" +                                          // S.p.A. (con puntos)
        @"|ltd\.?|inc\.?|llc\.?|gmbh|b\.?\s?v\.?" +                     // Ltd, Inc, LLC, GmbH, B.V.
        @"|sociedad\s+(limitada|anonima)(\s+(unipersonal|laboral|profesional))?";

    [GeneratedRegex(
        @"(?:[\s,]+(?:" + Formas + @")|[\s,]*\(\s*(?:" + Formas + @")\s*\))[\s.,]*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex FormaJuridica();

    /// <summary>
    /// Forma canónica para COMPARAR (no para mostrar): la de <see cref="TextoComparable.Normalizar"/>
    /// (minúsculas, sin tildes, un solo espacio) y además sin la forma jurídica final (S.L., S.A., …)
    /// ni puntuación sobrante al final. Devuelve "" si el nombre está vacío.
    /// </summary>
    public static string Normalizar(string? nombre)
    {
        var texto = TextoComparable.Normalizar(nombre);

        // Se repite por si vienen dos formas encadenadas («… S.L. S.A.»): raro, pero barato.
        string anterior;
        do
        {
            anterior = texto;
            texto = FormaJuridica().Replace(texto, "");
        } while (texto != anterior);

        return texto.Trim().TrimEnd('.', ',', ';', '-').Trim();
    }
}
