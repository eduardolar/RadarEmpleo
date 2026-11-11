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

    private readonly RutasCurriculum _rutas;

    public LectorCurriculum(RutasCurriculum rutas) => _rutas = rutas;

    public async Task<string> LeerRegistroAsync(CancellationToken cancelacion = default)
    {
        ExigirCarpeta();
        return await LeerTextoAsync(_rutas.Registro!, RutasCurriculum.ArchivoRegistro, cancelacion);
    }

    public async Task<TextoLeido> LeerPerfilAsync(CancellationToken cancelacion = default)
    {
        ExigirCarpeta();
        var ruta = _rutas.Perfil!;
        var texto = await LeerTextoAsync(ruta, RutasCurriculum.ArchivoPerfil, cancelacion);
        return new TextoLeido(texto, File.GetLastWriteTimeUtc(ruta));
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
        if (!EsCarpetaNormal(carpeta)) return new InformesRastreo(informes, json, noLeidos);

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
            return new InformesRastreo(informes, json, noLeidos);
        }

        foreach (var archivo in archivos)
        {
            var relativa = $"{RutasCurriculum.CarpetaInformesRastreo}/{archivo.Name}";
            switch (archivo.Extension.ToLowerInvariant())
            {
                case ".md":
                    try
                    {
                        informes.Add(new InformeRastreoLeido(relativa, await LeerTextoAsync(archivo.FullName, archivo.Name, cancelacion)));
                    }
                    catch (ReglaNegocioException)
                    {
                        // Uno que no se puede abrir (la tarea de los lunes escribiéndolo) no impide leer los demás.
                        noLeidos.Add(relativa);
                    }
                    break;
                case ".json":
                    json.Add(relativa);
                    break;
            }
        }
        return new InformesRastreo(informes, json, noLeidos);
    }

    // ---------------------------------------------------------------- auxiliares

    private void ExigirCarpeta()
    {
        if (!_rutas.HayCarpeta) throw new ReglaNegocioException(RutasCurriculum.MensajeSinCarpeta);
    }

    private static async Task<string> LeerTextoAsync(string ruta, string nombre, CancellationToken cancelacion)
    {
        if (!File.Exists(ruta))
            throw new ReglaNegocioException($"No se encuentra {nombre} en la carpeta Curriculum.");

        try
        {
            await using var fichero = new FileStream(ruta, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, TamanoBufer, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var lector = new StreamReader(fichero, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await lector.ReadToEndAsync(cancelacion);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ReglaNegocioException($"No se ha podido leer {nombre}. Vuelve a intentarlo en un momento.");
        }
    }

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
