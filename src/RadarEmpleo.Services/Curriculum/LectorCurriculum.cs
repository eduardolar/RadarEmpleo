using System.Security.Cryptography;
using System.Text;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Curriculum;

/// <summary>
/// Lee la carpeta Curriculum sin bloquear nada: abre los archivos con <see cref="FileAccess.Read"/> y
/// compartidos para que Eduardo o un agente puedan tenerlos abiertos, guardarlos o sustituirlos mientras
/// tanto. Los mensajes no incluyen la ruta para no enseñarla en la demo pública.
/// </summary>
public sealed class LectorCurriculum : ILectorCurriculum
{
    private const int TamanoBufer = 4096;
    private const string TipoPdf = "application/pdf";
    private const string TipoDocx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private static readonly string[] CarpetasQueSeSaltan = ["__pycache__"];

    // Todas estrictas: un archivo guardado en Windows-1252 (o con bytes sueltos, o una mitad de par UTF-16 suelta) no se
    // lee a medias con «�» en las tildes. No se usa StreamReader: al ver un BOM cambia a su codificación, que no es estricta.
    private static readonly UTF8Encoding Utf8Estricto = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding Utf16LeEstricto = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding Utf16BeEstricto = new(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly UTF32Encoding Utf32LeEstricto = new(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true);
    private static readonly UTF32Encoding Utf32BeEstricto = new(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);

    private readonly RutasCurriculum _rutas;

    public LectorCurriculum(RutasCurriculum rutas) => _rutas = rutas;

    public async Task<string> LeerRegistroAsync(CancellationToken cancelacion = default)
    {
        ExigirCarpeta();
        return await LeerTextoAsync(_rutas.Registro!, RutasCurriculum.ArchivoRegistro, cancelacion);
    }

    public Task<TextoLeido> LeerRegistroConFechaAsync(CancellationToken cancelacion = default)
    {
        ExigirCarpeta();
        return LeerConFechaAsync(_rutas.Registro!, RutasCurriculum.ArchivoRegistro, cancelacion);
    }

    public Task<TextoLeido> LeerPerfilAsync(CancellationToken cancelacion = default)
    {
        ExigirCarpeta();
        return LeerConFechaAsync(_rutas.Perfil!, RutasCurriculum.ArchivoPerfil, cancelacion);
    }

    public async Task<string?> LeerHashAsync(ArchivoVigilado archivo, CancellationToken cancelacion = default)
    {
        if (_rutas.Carpeta is null) return null;
        if (!_rutas.HayCarpeta) return ILectorCurriculum.HashAusente;
        var (ruta, nombre) = archivo switch
        {
            ArchivoVigilado.Registro => (_rutas.Registro!, RutasCurriculum.ArchivoRegistro),
            ArchivoVigilado.Perfil => (_rutas.Perfil!, RutasCurriculum.ArchivoPerfil),
            _ => throw new ArgumentOutOfRangeException(nameof(archivo))
        };
        if (!File.Exists(ruta)) return ILectorCurriculum.HashAusente;
        try
        {
            return Hash(await LeerBytesAsync(ruta, nombre, cancelacion));
        }
        catch (ReglaNegocioException) when (!File.Exists(ruta))
        {
            // Lo han borrado entre medias.
            return ILectorCurriculum.HashAusente;
        }
    }

    public IReadOnlyList<ArchivoDisponible> LeerDocumentosDisponibles()
    {
        ExigirCarpeta();
        var raiz = _rutas.Carpeta!;
        var archivos = new List<ArchivoDisponible>();

        // Recorrido a mano y con profundidad fija: nunca se baja por todo Curriculum.
        AnadirArchivos(raiz, raiz, archivos);
        foreach (var semana in Subcarpetas(Path.Combine(raiz, RutasCurriculum.CarpetaCandidaturas))
                     .Where(c => Path.GetFileName(c).StartsWith(CarpetasSemana.Prefijo, StringComparison.Ordinal)))
            AnadirArchivos(raiz, semana, archivos);

        var archivo = Path.Combine(raiz, RutasCurriculum.CarpetaArchivo);
        if (EsCarpetaNormal(archivo))
        {
            AnadirArchivos(raiz, archivo, archivos);
            foreach (var subcarpeta in Subcarpetas(archivo))
                AnadirArchivos(raiz, subcarpeta, archivos);
        }
        return archivos;
    }

    public DocumentoLeido? LeerDocumento(string rutaRelativa)
    {
        if (!_rutas.HayCarpeta) return null;

        var raiz = _rutas.Carpeta!;
        var ruta = RutaSegura.Resolver(raiz, rutaRelativa);
        FileStream flujo;
        try
        {
            flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, TamanoBufer, FileOptions.Asynchronous);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Incluye FileNotFound y DirectoryNotFound: para quien lo pide es lo mismo, «no está».
            return null;
        }

        try
        {
            // Entre Resolver y abrirlo, el archivo o una carpeta se pueden haber cambiado por un enlace.
            var real = RutaSegura.ComprobarAbierto(raiz, flujo);
            var nombre = real[(real.LastIndexOf('/') + 1)..];
            var tipo = Path.GetExtension(nombre).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? TipoPdf : TipoDocx;
            return new DocumentoLeido(flujo, nombre, tipo, real);
        }
        catch
        {
            flujo.Dispose();
            throw;
        }
    }

    public async Task<InformesRastreo> LeerInformesRastreoAsync(CancellationToken cancelacion = default)
    {
        ExigirCarpeta();
        var carpeta = _rutas.CarpetaRastreos!;
        var informes = new List<InformeRastreoLeido>();
        var json = new List<string>();
        var noLeidos = new List<string>();
        var noUtf8 = new List<string>();
        if (!EsCarpetaNormal(carpeta)) return new InformesRastreo(informes, json, noLeidos, noUtf8);

        List<FileInfo> archivos;
        try
        {
            archivos = new DirectoryInfo(carpeta).EnumerateFiles("rastreo_*")
                .Where(f => !EsEnlace(f))
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new InformesRastreo(informes, json, noLeidos, noUtf8);
        }

        foreach (var archivo in archivos)
        {
            var relativa = $"{RutasCurriculum.CarpetaInformesRastreo}/{archivo.Name}";
            switch (archivo.Extension.ToLowerInvariant())
            {
                case ".md":
                    byte[] bytes;
                    try
                    {
                        bytes = await LeerBytesAsync(archivo.FullName, archivo.Name, cancelacion);
                    }
                    catch (ReglaNegocioException)
                    {
                        // Uno que no se puede abrir (la tarea de los lunes escribiéndolo) no impide leer los demás.
                        noLeidos.Add(relativa);
                        break;
                    }
                    if (DecodificarEstricto(bytes) is { } texto) informes.Add(new InformeRastreoLeido(relativa, texto));
                    else noUtf8.Add(relativa);
                    break;
                case ".json":
                    json.Add(relativa);
                    break;
            }
        }
        return new InformesRastreo(informes, json, noLeidos, noUtf8);
    }

    // ---------------------------------------------------------------- auxiliares

    private void ExigirCarpeta()
    {
        if (!_rutas.HayCarpeta) throw new ReglaNegocioException(RutasCurriculum.MensajeSinCarpeta);
    }

    private static async Task<TextoLeido> LeerConFechaAsync(string ruta, string nombre, CancellationToken cancelacion)
    {
        var bytes = await LeerBytesAsync(ruta, nombre, cancelacion);
        return new TextoLeido(Decodificar(bytes, nombre), File.GetLastWriteTimeUtc(ruta), Hash(bytes));
    }

    private static async Task<string> LeerTextoAsync(string ruta, string nombre, CancellationToken cancelacion) =>
        Decodificar(await LeerBytesAsync(ruta, nombre, cancelacion), nombre);

    /// <summary>
    /// El archivo entero de una vez (el registro ronda los 140 KB), para que el texto y su hash salgan de la
    /// misma lectura.
    /// </summary>
    private static async Task<byte[]> LeerBytesAsync(string ruta, string nombre, CancellationToken cancelacion)
    {
        if (!File.Exists(ruta))
            throw new ReglaNegocioException($"No se encuentra {nombre} en la carpeta Curriculum.");

        try
        {
            await using var fichero = new FileStream(ruta, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, TamanoBufer, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var memoria = new MemoryStream();
            await fichero.CopyToAsync(memoria, cancelacion);
            return memoria.ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ReglaNegocioException($"No se ha podido leer {nombre}. Vuelve a intentarlo en un momento.");
        }
    }

    /// <summary>
    /// UTF-8, o lo que diga su BOM (UTF-16 del Bloc de notas antiguo, o UTF-32), igual que detectaba StreamReader. Si no es
    /// válido en esa codificación no se adivina otra ni se cambian las letras raras por «�»: se avisa para que lo guarden en UTF-8.
    /// </summary>
    /// <exception cref="ReglaNegocioException">No es texto válido en su codificación.</exception>
    private static string Decodificar(byte[] bytes, string nombre) =>
        DecodificarEstricto(bytes) ?? throw new ReglaNegocioException(MensajeNoUtf8(nombre));

    /// <summary>El aviso de un archivo que no se puede leer sin estropear las letras.</summary>
    private static string MensajeNoUtf8(string nombre) =>
        $"{nombre} no está guardado en UTF-8 y se perderían las tildes y las eñes. Guárdalo con la codificación UTF-8 y vuelve a intentarlo.";

    /// <summary>El texto sin su BOM, o null si no es válido en la codificación que toca.</summary>
    private static string? DecodificarEstricto(byte[] bytes)
    {
        // UTF-32 LE antes que UTF-16 LE: su BOM empieza igual (FF FE).
        var (codificacion, salto) = bytes switch
        {
            [0xEF, 0xBB, 0xBF, ..] => ((Encoding)Utf8Estricto, 3),
            [0xFF, 0xFE, 0x00, 0x00, ..] => (Utf32LeEstricto, 4),
            [0x00, 0x00, 0xFE, 0xFF, ..] => (Utf32BeEstricto, 4),
            [0xFF, 0xFE, ..] => (Utf16LeEstricto, 2),
            [0xFE, 0xFF, ..] => (Utf16BeEstricto, 2),
            _ => (Utf8Estricto, 0)
        };
        try
        {
            return codificacion.GetString(bytes, salto, bytes.Length - salto);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Subcarpetas directas que no son enlaces ni carpetas que se saltan; vacío si no se puede leer.</summary>
    private static IEnumerable<string> Subcarpetas(string carpeta)
    {
        if (!EsCarpetaNormal(carpeta)) return [];
        try
        {
            return Directory.GetDirectories(carpeta)
                .Where(c => !CarpetasQueSeSaltan.Contains(Path.GetFileName(c), StringComparer.OrdinalIgnoreCase))
                .Where(EsCarpetaNormal)
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool EsCarpetaNormal(string carpeta)
    {
        var info = new DirectoryInfo(carpeta);
        return info.Exists && !EsEnlace(info);
    }

    // El atributo primero, que ya viene del listado: LinkTarget pide más datos al disco.
    private static bool EsEnlace(FileSystemInfo info) =>
        info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget is not null;

    private static void AnadirArchivos(string raiz, string carpeta, List<ArchivoDisponible> archivos)
    {
        try
        {
            var relativaCarpeta = carpeta == raiz ? "" : RutaSegura.Relativa(raiz, carpeta);
            foreach (var info in new DirectoryInfo(carpeta).EnumerateFiles().OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (!ReglasDocumento.EsExtensionPermitida(info.Name) || EsEnlace(info)) continue;
                var relativa = RutaSegura.Relativa(raiz, info.FullName);
                // Solo lo que luego se podrá vincular: ni un DNI en la raíz ni un «~$CV.docx» de Word.
                if (!RutaSegura.Existe(raiz, relativa)) continue;
                archivos.Add(new ArchivoDisponible(relativa, info.Name, relativaCarpeta, info.LastWriteTimeUtc));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Una carpeta que no se puede leer no impide sugerir lo demás.
        }
    }
}
