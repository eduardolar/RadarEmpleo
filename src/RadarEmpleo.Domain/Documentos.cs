using System.Text.RegularExpressions;

namespace RadarEmpleo.Domain;

/// <summary>
/// Un archivo de la carpeta Curriculum (CV, carta…) vinculado a una candidatura. Solo se guarda la ruta:
/// la app nunca copia, mueve ni borra el archivo. Por cada tipo hay un único documento
/// <see cref="Vigente"/>, y esa regla vive en <see cref="Candidatura.VincularDocumento"/> y
/// <see cref="Candidatura.QuitarDocumento"/>.
/// </summary>
public class Documento
{
    public const int LargoNombreArchivo = 260;
    public const int LargoRutaRelativa = 500;
    public const int LargoHash = 64;

    public int Id { get; set; }
    public int CandidaturaId { get; set; }
    public Candidatura Candidatura { get; set; } = null!;
    public TipoDocumento Tipo { get; set; }
    public string NombreArchivo { get; set; } = "";
    /// <summary>Ruta dentro de la carpeta Curriculum, siempre con «/» («Candidaturas/Semana …/CV_….pdf»).</summary>
    public string RutaRelativa { get; set; } = "";
    /// <summary>Si estaba en su sitio al vincularlo. La ficha lo vuelve a mirar en el disco cada vez.</summary>
    public bool Encontrado { get; set; }
    public OrigenDocumento Origen { get; set; } = OrigenDocumento.Manual;
    /// <summary>Preparación de la F8 que lo generó. Sin clave foránea hasta que exista esa tabla.</summary>
    public int? PreparacionId { get; set; }
    public bool Vigente { get; set; }
    /// <summary>SHA-256 en hexadecimal del archivo al vincularlo.</summary>
    public string? HashSha256 { get; set; }
    /// <summary>Páginas del PDF. Queda en null hasta la F8 (contarlas sin librería no es fiable).</summary>
    public int? Paginas { get; set; }
    public DateTime FechaAlta { get; set; }
}

/// <summary>Cuánto se parece el nombre de un archivo al de una empresa (<see cref="ReglasDocumento.CoincidenciaConEmpresa"/>).</summary>
public enum CoincidenciaEmpresa
{
    Ninguna,
    Parcial,
    Completa
}

/// <summary>
/// Reglas puras sobre los archivos de Curriculum: qué extensiones se pueden vincular, qué tipo de
/// documento es cada nombre y si un nombre encaja con una empresa. Los nombres siguen la costumbre del
/// equipo: «CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf», «Carta_Presentacion_Eduardo_Lara_Navarro_ABM.pdf».
/// </summary>
public static partial class ReglasDocumento
{
    /// <summary>Únicas extensiones que se pueden vincular y servir (en minúsculas, con el punto).</summary>
    public static readonly IReadOnlyList<string> ExtensionesPermitidas = [".pdf", ".docx"];

    // Prefijos fijos que no dicen nada de la empresa. «Eduardo_Lara_Navarro» se quita en cualquier
    // sitio porque en los dosieres va al final («Dosier_Competencias_NovaIT_Eduardo_Lara_Navarro»).
    private static readonly string[] Prefijos = ["CV_", "Carta_Presentacion_", "Carta_Motivacion_", "Carta_"];
    private const string NombrePersona = "Eduardo_Lara_Navarro";

    private static readonly HashSet<string> PalabrasVacias = new(StringComparer.Ordinal)
    {
        "de", "del", "la", "las", "el", "los", "y", "e", "en", "a", "al", "con", "por", "para",
        "the", "of", "and", "&"
    };

    // Palabras que comparten muchas empresas, o que llevan muchos CV por el puesto o la tecnología
    // («NET», «Python», «Soporte», «Operario»): no bastan para una coincidencia parcial.
    private static readonly HashSet<string> PalabrasGenericas = new(StringComparer.Ordinal)
    {
        // de empresa
        "grupo", "group", "servicios", "services", "consultores", "consultoria", "consulting", "consultants",
        "sistemas", "systems", "soluciones", "solutions", "tecnologia", "tecnologias", "technology",
        "technologies", "tech", "informatica", "informaticos", "informaticas", "ingenieria", "software",
        "digital", "global", "spain", "espana", "iberica", "empresa", "company", "corporacion",
        "centro", "especial", "empleo", "ayto", "ayuntamiento", "cliente", "via",
        // de puesto o tecnología (van en el nombre de muchos CV)
        "net", "ia", "it", "ti", "data", "java", "python", "angular", "react", "frontend", "fullstack",
        "backend", "soporte", "operario", "dependiente", "cajero", "reponedor", "vendedor", "tecnico",
        "movil", "erp", "saas", "sqlserver", "gestion", "seguridad"
    };

    // Lo que va entre paréntesis suele ser una aclaración («OrbiSystem (Silver Soft)», «Ranstek (cliente
    // cárnico)»); un paréntesis sin cerrar llega hasta el final.
    [GeneratedRegex(@"\([^)]*(\)|$)")]
    private static partial Regex Parentesis();

    // Por debajo de este largo una palabra tiene que ser una palabra entera del nombre del archivo.
    private const int LargoMinimoSubcadena = 4;

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex SeparadoresPalabra();

    // Cortes de CamelCase: «GrupoNS» → «Grupo NS», «IAVectech» → «IA Vectech».
    [GeneratedRegex(@"(?<=[\p{Ll}\p{N}])(?=\p{Lu})|(?<=\p{Lu})(?=\p{Lu}\p{Ll})")]
    private static partial Regex CortesCamelCase();

    /// <summary>¿Tiene el archivo una extensión que se puede vincular (.pdf o .docx, sin distinguir mayúsculas)?</summary>
    public static bool EsExtensionPermitida(string? nombreArchivo)
    {
        var extension = Path.GetExtension(nombreArchivo ?? "");
        return ExtensionesPermitidas.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tipo deducido del nombre: «CV_» es CV, «Carta_» es carta, si contiene «Entrevista» o «Guia» es una
    /// guía de entrevista, «Correo_» o «Mensaje_» es un correo, y lo demás (un dosier, por ejemplo) es Otro.
    /// </summary>
    public static TipoDocumento TipoDesdeNombre(string? nombreArchivo)
    {
        var nombre = Path.GetFileName(nombreArchivo ?? "");
        if (nombre.StartsWith("CV_", StringComparison.OrdinalIgnoreCase)) return TipoDocumento.CV;
        if (nombre.StartsWith("Carta_", StringComparison.OrdinalIgnoreCase)) return TipoDocumento.Carta;
        if (nombre.Contains("Entrevista", StringComparison.OrdinalIgnoreCase) ||
            nombre.Contains("Guia", StringComparison.OrdinalIgnoreCase))
            return TipoDocumento.GuiaEntrevista;
        if (nombre.StartsWith("Correo_", StringComparison.OrdinalIgnoreCase) ||
            nombre.StartsWith("Mensaje_", StringComparison.OrdinalIgnoreCase))
            return TipoDocumento.Correo;
        return TipoDocumento.Otro;
    }

    /// <summary>
    /// ¿El nombre del archivo es de esta empresa? Se compara la empresa ya normalizada
    /// (<see cref="NombresEmpresa.Normalizar"/>) partida en palabras, sin palabras vacías, con el nombre del
    /// archivo sin los prefijos fijos, normalizado y sin separadores.
    /// <list type="bullet">
    /// <item>Completa: están todas las palabras («club ibérico libro» en «ClubIbericoLibro»).</item>
    /// <item>Parcial: está alguna palabra distintiva, es decir, que no sea genérica como «grupo» o
    /// «sistemas»: «Grupo DATALUX» con «DATALUX» y «ABM Grupo Venturi» con «ABM».</item>
    /// </list>
    /// El nombre del archivo se parte en palabras por «_» y por las mayúsculas («ClubIbericoLibro»).
    /// Cada palabra de la empresa tiene que empezar y acabar en uno de esos cortes: «orbisystem» encaja con
    /// «OrbiSystem», pero «soft» no con «Logisoft». Las de menos de 4 letras («NS», «UVT», «ABM») tienen que
    /// ser una palabra entera: si no, «NS» encajaría con cualquier «Transportes». Lo que va entre
    /// paréntesis no cuenta, salvo que fuera no quede nada.
    /// </summary>
    public static CoincidenciaEmpresa CoincidenciaConEmpresa(string? nombreArchivo, string? empresaNormalizada)
    {
        var empresa = TextoComparable.Normalizar(empresaNormalizada);
        var palabras = Palabras(Parentesis().Replace(empresa, " "));
        if (palabras.Count == 0) palabras = Palabras(empresa);
        if (palabras.Count == 0) return CoincidenciaEmpresa.Ninguna;

        var trozos = SeparadoresPalabra().Split(CortesCamelCase().Replace(RestoDelNombre(nombreArchivo), " "))
            .Select(TextoComparable.Normalizar)
            .Select(t => SeparadoresPalabra().Replace(t, ""))
            .Where(t => t.Length > 0)
            .ToList();
        var junto = string.Concat(trozos);
        var cortes = new HashSet<int> { 0 };
        var posicion = 0;
        foreach (var trozo in trozos) cortes.Add(posicion += trozo.Length);

        bool Esta(string palabra) => palabra.Length < LargoMinimoSubcadena
            ? trozos.Contains(palabra)
            : cortes.Any(inicio => cortes.Contains(inicio + palabra.Length) &&
                                   string.CompareOrdinal(junto, inicio, palabra, 0, palabra.Length) == 0);

        if (palabras.All(Esta)) return CoincidenciaEmpresa.Completa;

        // Cualquiera y no solo «la más distintiva»: en «ABM Grupo Venturi» o «Ad7 Hexágono» el archivo
        // solo lleva la palabra corta.
        return palabras.Where(p => !PalabrasGenericas.Contains(p)).Any(Esta)
            ? CoincidenciaEmpresa.Parcial
            : CoincidenciaEmpresa.Ninguna;
    }

    private static List<string> Palabras(string texto) =>
        SeparadoresPalabra().Split(texto)
            .Where(p => p.Length > 0 && !PalabrasVacias.Contains(p))
            .Distinct()
            .ToList();

    /// <summary>Nombre sin extensión, sin el prefijo fijo y sin el nombre de la persona.</summary>
    private static string RestoDelNombre(string? nombreArchivo)
    {
        var nombre = Path.GetFileNameWithoutExtension(nombreArchivo ?? "");
        foreach (var prefijo in Prefijos)
        {
            if (!nombre.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)) continue;
            nombre = nombre[prefijo.Length..];
            break;
        }
        return nombre.Replace(NombrePersona, "", StringComparison.OrdinalIgnoreCase);
    }
}
