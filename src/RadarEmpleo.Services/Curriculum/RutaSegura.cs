using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Curriculum;

/// <summary>
/// Convierte la ruta relativa de un documento (la que llega del front o está guardada en la BD) en una
/// ruta completa dentro de la carpeta Curriculum, o la rechaza. Se usa al vincular y otra vez al servir,
/// porque el archivo se puede sustituir por un enlace después de vincularlo. Además, con el archivo ya
/// abierto, <see cref="ComprobarAbierto"/> revisa la ruta real que da Windows. Los mensajes no incluyen
/// rutas absolutas.
/// </summary>
public static class RutaSegura
{
    /// <summary>Campo del JSON al que se asocian los errores.</summary>
    public const string Campo = "rutaRelativa";

    private const string MensajeNoValida = "La ruta del documento no es válida. Elige uno de los documentos sugeridos.";

    /// <summary>
    /// De la raíz de Curriculum solo se abren los CV y las cartas: ahí también puede acabar, por ejemplo,
    /// un DNI recién descargado.
    /// </summary>
    public static readonly IReadOnlyList<string> PrefijosRaiz = ["CV_", "Carta_"];

    private static readonly char[] Separadores = ['/', '\\'];

    // Los no válidos en Windows, también en Linux: así la regla es la misma en el PC y en la demo.
    private static readonly HashSet<char> CaracteresNoValidos =
        [.. Path.GetInvalidFileNameChars(), .. Path.GetInvalidPathChars(), '<', '>', ':', '"', '|', '?', '*'];

    private static StringComparison Comparacion =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer Comparador =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Ruta completa del documento. Exige una ruta relativa sin «..», «.», «:» ni caracteres no válidos,
    /// con extensión .pdf o .docx, que quede dentro de <paramref name="raiz"/>, en una carpeta permitida
    /// (<see cref="RutasCurriculum.CarpetasPermitidas"/>, o un CV o carta suelto de la raíz) y sin enlaces
    /// simbólicos ni uniones por el camino. No comprueba que el archivo exista.
    /// </summary>
    /// <exception cref="ValidacionException">La ruta no cumple alguna de las reglas.</exception>
    public static string Resolver(string raiz, string? rutaRelativa)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativa))
            throw new ValidacionException("Falta la ruta del documento.", Campo);
        if (rutaRelativa.Length > Documento.LargoRutaRelativa)
            throw new ValidacionException($"La ruta del documento no puede tener más de {Documento.LargoRutaRelativa} caracteres.", Campo);

        // Cualquier «:» fuera: unidades («C:\», «C:foo»), \\?\ y flujos alternativos («cv.pdf:secreto»).
        if (rutaRelativa.Contains(':') || Path.IsPathRooted(rutaRelativa) || Separadores.Contains(rutaRelativa[0]))
            throw NoValida();

        ValidarPartes(rutaRelativa.Split(Separadores));

        var raizCompleta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raiz));
        var completa = Path.GetFullPath(Path.Combine([raizCompleta, .. rutaRelativa.Split(Separadores)]));

        // Otra vez sobre la ruta que ha quedado: lo que se abre es esta, no la que llegó.
        var partes = PartesDentroDe(raizCompleta, completa) ?? throw NoValida();
        ValidarPartes(partes);

        // LinkTarget y no el atributo ReparsePoint: los archivos de OneDrive también son reparse points.
        var actual = raizCompleta;
        for (var i = 0; i < partes.Length; i++)
        {
            actual = Path.Combine(actual, partes[i]);
            FileSystemInfo info = i == partes.Length - 1 ? new FileInfo(actual) : new DirectoryInfo(actual);
            if (info.LinkTarget is not null) throw NoValida();
        }

        return completa;
    }

    /// <summary>
    /// Con el archivo ya abierto (lo que se va a leer o servir de verdad, aunque lo cambien entre medias):
    /// en Windows, pide su ruta real y la vuelve a validar como <see cref="Resolver"/>, y rechaza los
    /// archivos con enlaces duros (otro nombre del mismo archivo podría estar fuera, en «Documentos
    /// personales»). La ruta real también resuelve los nombres cortos («NOTAS~1.PDF») y trae las
    /// mayúsculas del disco. En otros sistemas, con <see cref="Resolver"/> basta.
    /// </summary>
    /// <returns>La ruta relativa real, con «/».</returns>
    /// <exception cref="ValidacionException">El archivo abierto no se puede servir.</exception>
    public static string ComprobarAbierto(string raiz, FileStream archivo)
    {
        var raizCompleta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raiz));
        if (!OperatingSystem.IsWindows())
            return Relativa(raizCompleta, archivo.Name);

        string raizReal, rutaReal;
        uint enlaces;
        try
        {
            raizReal = Path.TrimEndingDirectorySeparator(ArchivosWindows.RutaFinalCarpeta(raizCompleta));
            rutaReal = ArchivosWindows.RutaFinal(archivo.SafeFileHandle);
            enlaces = ArchivosWindows.NumeroEnlaces(archivo.SafeFileHandle);
        }
        catch (IOException)
        {
            throw NoValida();
        }

        if (enlaces > 1) throw NoValida();
        var partes = PartesDentroDe(raizReal, rutaReal) ?? throw NoValida();
        ValidarPartes(partes);
        return string.Join('/', partes);
    }

    /// <summary>
    /// ¿La ruta es segura y el archivo está en su sitio? En Windows, además, sin enlaces duros: es lo que
    /// rechazará <see cref="ComprobarAbierto"/> al servirlo, así que ni se sugiere ni sale como encontrado.
    /// Nunca lanza.
    /// </summary>
    public static bool Existe(string? raiz, string? rutaRelativa)
    {
        if (raiz is null) return false;
        try
        {
            var ruta = Resolver(raiz, rutaRelativa);
            if (!File.Exists(ruta)) return false;
            return !OperatingSystem.IsWindows() || ArchivosWindows.NumeroEnlaces(ruta) <= 1;
        }
        catch (Exception error) when (error is ValidacionException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Ruta relativa a <paramref name="raiz"/> con «/», como se guarda en la BD.</summary>
    public static string Relativa(string raiz, string rutaCompleta) =>
        Path.GetRelativePath(raiz, rutaCompleta).Replace('\\', '/');

    /// <summary>Partes de <paramref name="ruta"/> por debajo de <paramref name="raiz"/>, o null si queda fuera.</summary>
    private static string[]? PartesDentroDe(string raiz, string ruta) =>
        ruta.StartsWith(raiz + Path.DirectorySeparatorChar, Comparacion)
            ? Path.GetRelativePath(raiz, ruta).Split(Path.DirectorySeparatorChar)
            : null;

    /// <summary>Las reglas de cada trozo, de la extensión y de las carpetas permitidas.</summary>
    private static void ValidarPartes(string[] partes)
    {
        foreach (var parte in partes)
        {
            // Windows quita los espacios y puntos finales: «cv.pdf.» acabaría abriendo «cv.pdf».
            if (parte.Length == 0 || parte is "." or ".." || parte != parte.Trim() || parte.EndsWith('.'))
                throw NoValida();
            if (parte.Any(c => c < ' ' || CaracteresNoValidos.Contains(c)))
                throw NoValida();
        }

        var nombre = partes[^1];
        if (!ReglasDocumento.EsExtensionPermitida(nombre))
            throw new ValidacionException("Solo se pueden abrir documentos PDF o Word (.docx).", Campo);
        // «~$CV.docx» es el archivo de bloqueo que deja Word mientras el documento está abierto.
        if (nombre.StartsWith("~$", StringComparison.Ordinal))
            throw NoValida();

        if (partes.Length == 1)
        {
            if (!PrefijosRaiz.Any(p => nombre.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                throw new ValidacionException("De la carpeta Curriculum solo se abren los CV y las cartas («CV_…», «Carta_…»).", Campo);
        }
        else if (!RutasCurriculum.CarpetasPermitidas.Contains(partes[0], Comparador))
        {
            throw new ValidacionException("La app no abre documentos de esa carpeta.", Campo);
        }
    }

    private static ValidacionException NoValida() => new(MensajeNoValida, Campo);
}
