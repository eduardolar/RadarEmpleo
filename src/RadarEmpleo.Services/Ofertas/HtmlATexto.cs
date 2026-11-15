using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RadarEmpleo.Services.Ofertas;

/// <summary>
/// Pasa la página de una oferta a texto plano para el prompt: sin scripts, estilos, plantillas, SVG ni comentarios;
/// con un salto por cada bloque (párrafo, título, fila, elemento de lista…), las entidades decodificadas y los espacios
/// normalizados. Puro. Las expresiones van sin vuelta atrás (<see cref="RegexOptions.NonBacktracking"/>): una página
/// de 2 MB hecha a mala idea no puede dejarlas pensando.
/// </summary>
public static class HtmlATexto
{
    private const RegexOptions Opciones = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant
        | RegexOptions.NonBacktracking;

    private static readonly Regex Comentarios = new("<!--.*?-->", Opciones);
    private static readonly Regex Titulo = new(@"<title\b[^>]*>(.*?)</title\s*>", Opciones);
    private static readonly Regex AperturaSinContenido = new(@"<(script|style|noscript|template|svg|head|iframe|object)\b[^>]*>", Opciones);
    private static readonly Regex Elementos = new(@"<li\b[^>]*>", Opciones);
    private static readonly Regex Bloques = new(
        @"</?(p|div|br|hr|h[1-6]|ul|ol|tr|table|section|article|header|footer|main|nav|aside|blockquote|pre|dl|dt|dd|form|fieldset)\b[^>]*>", Opciones);
    private static readonly Regex Celdas = new(@"</t[dh]\s*>", Opciones);
    private static readonly Regex Etiquetas = new(@"</?[a-z!][^>]*>", Opciones);
    private static readonly Regex Espacios = new(@"[\s-[\n]]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex VariosSaltos = new(@"\n{3,}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>El texto de la página, con el título (si lo tiene) en la primera línea.</summary>
    public static string Convertir(string html)
    {
        var texto = Comentarios.Replace(html, " ");
        var titulo = Titulo.Match(texto) is { Success: true } encontrado ? NormalizarEspacios(Decodificar(encontrado.Groups[1].Value)) : "";
        texto = QuitarSinContenido(texto);
        texto = Elementos.Replace(texto, "\n- ");
        texto = Bloques.Replace(texto, "\n");
        texto = Celdas.Replace(texto, " · ");
        texto = Etiquetas.Replace(texto, " ");
        texto = NormalizarEspacios(Decodificar(texto));
        if (titulo.Length == 0 || texto.StartsWith(titulo, StringComparison.Ordinal)) return texto;
        return texto.Length == 0 ? titulo : $"{titulo}\n\n{texto}";
    }

    /// <summary>Espacios seguidos en uno, cada línea sin espacios a los lados y como mucho una línea en blanco seguida.</summary>
    public static string NormalizarEspacios(string texto)
    {
        var lineas = texto.ReplaceLineEndings("\n").Split('\n').Select(l => Espacios.Replace(l, " ").Trim());
        return VariosSaltos.Replace(string.Join('\n', lineas), "\n\n").Trim();
    }

    // Lo que no es texto (script, style…) se quita con su contenido. La referencia al nombre de la etiqueta no se puede
    // usar sin vuelta atrás, así que el cierre se busca a mano, de una pasada. Sin cierre, se quita hasta el final: un
    // script sin cerrar no puede colar su código como texto.
    private static string QuitarSinContenido(string html)
    {
        var resultado = new StringBuilder(html.Length);
        var posicion = 0;
        for (var m = AperturaSinContenido.Match(html, posicion); m.Success; m = AperturaSinContenido.Match(html, posicion))
        {
            resultado.Append(html, posicion, m.Index - posicion).Append(' ');
            var cierre = html.IndexOf("</" + m.Groups[1].Value, m.Index + m.Length, StringComparison.OrdinalIgnoreCase);
            if (cierre < 0) return resultado.ToString();
            var fin = html.IndexOf('>', cierre);
            posicion = fin < 0 ? html.Length : fin + 1;
        }
        return resultado.Append(html, posicion, html.Length - posicion).ToString();
    }

    private static string Decodificar(string texto) => WebUtility.HtmlDecode(texto);
}
