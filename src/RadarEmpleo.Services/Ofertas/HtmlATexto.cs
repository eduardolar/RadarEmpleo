using System.Net;
using System.Text;
using RadarEmpleo.Services.Agentes;

namespace RadarEmpleo.Services.Ofertas;

/// <summary>
/// Pasa la página de una oferta a texto plano para el prompt: sin scripts, estilos, plantillas, SVG ni comentarios;
/// con un salto por cada bloque (párrafo, título, fila, elemento de lista…), las entidades decodificadas, sin caracteres
/// invisibles ni de control y los espacios normalizados. Puro y de una sola pasada, sin expresiones regulares: una página
/// de 2 MB hecha a mala idea no puede dejarlo pensando. Las comillas de los atributos se respetan (un
/// <c>&lt;a title='&lt;script&gt;'&gt;</c> no abre nada). No interpreta CSS: un elemento oculto sigue pasando, pero el
/// texto va al prompt como DATOS.
/// </summary>
public static class HtmlATexto
{
    // Lo que no es texto: se quita con su contenido. Sin cierre, hasta el final (un script sin cerrar no cuela su código).
    private static readonly HashSet<string> SinContenido = ["script", "style", "noscript", "template", "svg", "iframe", "object"];

    private static readonly HashSet<string> Bloques =
    [
        "p", "div", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "tr", "table", "section", "article", "header",
        "footer", "main", "nav", "aside", "blockquote", "pre", "dl", "dt", "dd", "form", "fieldset"
    ];

    /// <summary>El texto de la página, con el título (si lo tiene) en la primera línea.</summary>
    public static string Convertir(string html)
    {
        var salida = new StringBuilder(html.Length);
        var cierres = new Dictionary<string, int>();
        var titulo = "";
        var i = 0;
        while (i < html.Length)
        {
            var menor = html.IndexOf('<', i);
            if (menor < 0)
            {
                salida.Append(html, i, html.Length - i);
                break;
            }
            salida.Append(html, i, menor - i);
            if (string.CompareOrdinal(html, menor, "<!--", 0, 4) == 0)
            {
                var finComentario = html.IndexOf("-->", menor + 4, StringComparison.Ordinal);
                i = finComentario < 0 ? html.Length : finComentario + 3;
                salida.Append(' ');
                continue;
            }
            if (LeerEtiqueta(html, menor) is not { } etiqueta)
            {
                // Un «<» que no abre etiqueta («a < b») es texto.
                salida.Append('<');
                i = menor + 1;
                continue;
            }
            var (nombre, cierre, fin) = etiqueta;
            i = fin;
            if (!cierre && (SinContenido.Contains(nombre) || nombre == "title"))
            {
                var inicioCierre = BuscarCierre(html, nombre, i, cierres);
                // Un <title> sin cerrar no se come la página: su texto se queda como texto.
                if (nombre == "title" && inicioCierre < 0) continue;
                if (nombre == "title" && titulo.Length == 0) titulo = html[i..inicioCierre];
                var finCierre = inicioCierre < 0 ? -1 : html.IndexOf('>', inicioCierre);
                i = finCierre < 0 ? html.Length : finCierre + 1;
                salida.Append(' ');
                continue;
            }
            salida.Append(!cierre && nombre == "li" ? "\n- " : Bloques.Contains(nombre) ? "\n" : cierre && nombre is "td" or "th" ? " · " : " ");
        }

        var texto = NormalizarEspacios(WebUtility.HtmlDecode(salida.ToString()));
        titulo = NormalizarEspacios(WebUtility.HtmlDecode(titulo));
        if (titulo.Length == 0 || texto.StartsWith(titulo, StringComparison.Ordinal)) return texto;
        return texto.Length == 0 ? titulo : $"{titulo}\n\n{texto}";
    }

    /// <summary>
    /// Sin invisibles ni controles (los de <see cref="PlantillaPrompt"/>), espacios seguidos en uno, cada línea sin espacios
    /// a los lados y como mucho una línea en blanco seguida. De una pasada.
    /// </summary>
    public static string NormalizarEspacios(string texto)
    {
        var limpio = PlantillaPrompt.QuitarInvisibles(texto.ReplaceLineEndings("\n"));
        var salida = new StringBuilder(limpio.Length);
        var espacio = false;
        var saltos = 0;
        foreach (var c in limpio)
        {
            if (c == '\n')
            {
                saltos++;
                espacio = false;
            }
            else if (char.IsWhiteSpace(c))
            {
                espacio = true;
            }
            else
            {
                if (salida.Length > 0)
                {
                    if (saltos > 0) salida.Append('\n', Math.Min(saltos, 2));
                    else if (espacio) salida.Append(' ');
                }
                saltos = 0;
                espacio = false;
                salida.Append(c);
            }
        }
        return salida.ToString();
    }

    /// <summary>
    /// La etiqueta que empieza en <paramref name="inicio"/> (un «&lt;»): su nombre en minúsculas, si es de cierre y dónde
    /// acaba (tras el «&gt;», o al final si no lo tiene). Las comillas tras un «=» van hasta su pareja. Null si no es etiqueta.
    /// </summary>
    private static (string Nombre, bool Cierre, int Fin)? LeerEtiqueta(string html, int inicio)
    {
        var j = inicio + 1;
        var cierre = j < html.Length && html[j] == '/';
        if (cierre) j++;
        if (j >= html.Length) return null;
        string nombre;
        if (html[j] is '!' or '?')
        {
            nombre = "!";
        }
        else
        {
            if (!char.IsAsciiLetter(html[j])) return null;
            var finNombre = j;
            while (finNombre < html.Length && (char.IsAsciiLetterOrDigit(html[finNombre]) || html[finNombre] == '-')) finNombre++;
            nombre = html[j..finNombre].ToLowerInvariant();
            j = finNombre;
        }

        var anterior = ' ';
        for (var k = j; k < html.Length; k++)
        {
            var c = html[k];
            if (c == '>') return (nombre, cierre, k + 1);
            if (c is '"' or '\'' && anterior == '=')
            {
                var pareja = html.IndexOf(c, k + 1);
                if (pareja < 0) break;
                k = pareja;
                anterior = c;
                continue;
            }
            if (!char.IsWhiteSpace(c)) anterior = c;
        }
        return (nombre, cierre, html.Length);
    }

    /// <summary>
    /// Dónde empieza el siguiente <c>&lt;/nombre</c> desde <paramref name="desde"/>, o -1. Recuerda lo ya buscado: mil
    /// aperturas sin cierre no hacen mil búsquedas hasta el final.
    /// </summary>
    private static int BuscarCierre(string html, string nombre, int desde, Dictionary<string, int> cierres)
    {
        if (cierres.TryGetValue(nombre, out var previo) && (previo < 0 || previo >= desde)) return previo;
        var encontrado = html.IndexOf("</" + nombre, desde, StringComparison.OrdinalIgnoreCase);
        cierres[nombre] = encontrado;
        return encontrado;
    }
}
