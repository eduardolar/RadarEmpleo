using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Services;

/// <summary>
/// Documentos de la ficha: sugerir archivos de Curriculum, vincularlos, quitarlos y abrirlos. La app
/// nunca escribe, mueve ni borra un archivo: quitar un documento solo borra el vínculo. Los métodos que
/// buscan por id devuelven null (o false) si no existe, y el controlador lo traduce a 404.
/// </summary>
public sealed class DocumentosService
{
    /// <summary>Máximo de sugerencias que se devuelven.</summary>
    public const int MaximoSugerencias = 10;

    private const string MensajeNoEncontrado = "No encuentro el archivo. Puede que se haya movido.";
    private const string MensajeYaEnLaFicha = "Ese documento ya está en la ficha.";
    private const string MensajeALaVez =
        "Otra pestaña acaba de cambiar los documentos de esta ficha. Vuelve a abrirla para ver cómo han quedado.";

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILectorCurriculum _lector;
    private readonly RutasCurriculum _rutas;
    private readonly IClock _reloj;

    public DocumentosService(IDbContextFactory<AppDbContext> factory, ILectorCurriculum lector, RutasCurriculum rutas, IClock reloj)
    {
        _factory = factory;
        _lector = lector;
        _rutas = rutas;
        _reloj = reloj;
    }

    /// <summary>
    /// Hasta <see cref="MaximoSugerencias"/> archivos para la candidatura, uno por documento (el PDF; el
    /// .docx solo si no hay PDF). Primero los que llevan el nombre de la empresa (completo antes que
    /// parcial), de su semana, de las semanas más cercanas, de la raíz y al final de <c>Archivo\</c>; y
    /// dentro de eso, el CV antes que la carta. Si ninguno lleva el nombre de la empresa, los de la
    /// semana de la candidatura. En una candidatura enviada, los de semanas posteriores al envío no pueden
    /// ser el CV que se mandó: van al final y marcados. Devuelve null si la candidatura no existe.
    /// </summary>
    /// <exception cref="ReglaNegocioException">Falta la carpeta Curriculum.</exception>
    public async Task<List<DocumentoSugeridoDto>?> SugeridosAsync(int candidaturaId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var candidatura = await db.Candidaturas.AsNoTracking()
            .Include(c => c.Empresa)
            .Include(c => c.Documentos)
            .FirstOrDefaultAsync(c => c.Id == candidaturaId);
        if (candidatura is null) return null;

        var disponibles = _lector.LeerDocumentosDisponibles();
        var semana = CarpetasSemana.Nombre(candidatura.FechaEnvio ?? candidatura.FechaDeteccion);
        var lunes = CarpetasSemana.Lunes(semana)!.Value;
        var carpetaSemana = $"{RutasCurriculum.CarpetaCandidaturas}/{semana}";
        DateOnly? lunesEnvio = candidatura.FechaEnvio is null ? null : lunes;

        var candidatos = UnoPorDocumento(disponibles)
            .Select(a => new
            {
                Archivo = a,
                Tipo = ReglasDocumento.TipoDesdeNombre(a.NombreArchivo),
                Coincidencia = ReglasDocumento.CoincidenciaConEmpresa(a.NombreArchivo, candidatura.Empresa.NombreNormalizado),
                Orden = OrdenCarpeta(a.Carpeta, lunes),
                Posterior = LunesDe(a.Carpeta) > lunesEnvio
            })
            .ToList();

        var deLaEmpresa = candidatos.Where(c => c.Coincidencia != CoincidenciaEmpresa.Ninguna).ToList();
        // Hay CV genéricos reutilizados («CV_…_Operario.pdf»): sin coincidencias, los de su semana.
        var elegidos = deLaEmpresa.Count > 0
            ? deLaEmpresa
            : candidatos.Where(c => string.Equals(c.Archivo.Carpeta, carpetaSemana, StringComparison.OrdinalIgnoreCase)).ToList();

        return elegidos
            .OrderBy(c => c.Posterior)
            .ThenByDescending(c => c.Coincidencia)
            .ThenBy(c => c.Orden.Grupo).ThenBy(c => c.Orden.Distancia)
            .ThenBy(c => (int)c.Tipo)
            .ThenBy(c => c.Archivo.NombreArchivo, StringComparer.OrdinalIgnoreCase)
            .Take(MaximoSugerencias)
            .Select(c => new DocumentoSugeridoDto(
                c.Archivo.RutaRelativa,
                c.Archivo.NombreArchivo,
                c.Archivo.Carpeta,
                c.Tipo,
                Extension(c.Archivo.NombreArchivo),
                c.Archivo.FechaModificacion,
                c.Coincidencia switch
                {
                    CoincidenciaEmpresa.Completa => CoincidenciaSugerencia.Empresa,
                    CoincidenciaEmpresa.Parcial => CoincidenciaSugerencia.Parcial,
                    _ => CoincidenciaSugerencia.MismaSemana
                },
                EsAntiguo(c.Archivo.Carpeta),
                candidatura.Documentos.Any(d => MismaRuta(d.RutaRelativa, c.Archivo.RutaRelativa)),
                c.Posterior))
            .ToList();
    }

    /// <summary>
    /// Vincula un archivo de Curriculum a la ficha y lo deja como vigente de su tipo. El tipo, si no se
    /// indica, sale del nombre. Calcula el SHA-256 del archivo. Devuelve null si la candidatura no existe.
    /// </summary>
    /// <exception cref="ValidacionException">La ruta no es válida o no es segura (400).</exception>
    /// <exception cref="ReglaNegocioException">Falta la carpeta o el archivo, o ya está vinculado (409).</exception>
    public async Task<DocumentoDto?> VincularAsync(int candidaturaId, VincularDocumentoRequest peticion)
    {
        if (peticion.Tipo is { } tipoPedido) ValidacionException.ExigirDefinido(tipoPedido, "tipo");

        await using var db = await _factory.CreateDbContextAsync();
        var candidatura = await db.Candidaturas.Include(c => c.Documentos).FirstOrDefaultAsync(c => c.Id == candidaturaId);
        if (candidatura is null) return null;

        if (!_rutas.HayCarpeta) throw new ReglaNegocioException(RutasCurriculum.MensajeSinCarpeta);

        // Se guarda la ruta real del archivo abierto (con las mayúsculas del disco), no la que ha llegado.
        var leido = _lector.LeerDocumento(peticion.RutaRelativa) ?? throw new ReglaNegocioException(MensajeNoEncontrado);
        string hash;
        await using (leido.Contenido)
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(leido.Contenido));

        var documento = candidatura.VincularDocumento(
            peticion.Tipo ?? ReglasDocumento.TipoDesdeNombre(leido.NombreArchivo),
            leido.NombreArchivo, leido.RutaRelativa, OrigenDocumento.Manual, _reloj.AhoraUtc, hash);
        await GuardarAsync(db);
        return ADto(documento, _rutas);
    }

    /// <summary>
    /// Quita el documento de la ficha; el archivo sigue en su carpeta. Si era el vigente, pasa a serlo el
    /// más reciente de su tipo. Devuelve false si no existe.
    /// </summary>
    public async Task<bool> QuitarAsync(int documentoId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var candidatura = await db.Candidaturas.Include(c => c.Documentos)
            .FirstOrDefaultAsync(c => c.Documentos.Any(d => d.Id == documentoId));
        var documento = candidatura?.Documentos.FirstOrDefault(d => d.Id == documentoId);
        if (candidatura is null || documento is null) return false;

        candidatura.QuitarDocumento(documento);
        db.Documentos.Remove(documento);
        try
        {
            await GuardarAsync(db);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otra petición lo acaba de quitar.
            return false;
        }
        return true;
    }

    /// <summary>
    /// Guarda y traduce el choque con un índice único: la misma ruta dos veces, o dos documentos vigentes
    /// del mismo tipo porque otra petición ha vinculado otro a la vez (eso el dominio no lo puede ver).
    /// </summary>
    private static async Task GuardarAsync(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException error) when (ErroresBaseDatos.EsViolacionUnica(error, out var detalle))
        {
            throw new ReglaNegocioException(detalle.Contains(nameof(Documento.RutaRelativa), StringComparison.Ordinal)
                ? MensajeYaEnLaFicha
                : MensajeALaVez);
        }
    }

    /// <summary>
    /// Abre el archivo del documento para servirlo, volviendo a comprobar que la ruta es segura (el
    /// archivo se puede haber sustituido por un enlace después de vincularlo). Devuelve null si el
    /// documento no existe, falta el archivo o la ruta ya no es segura: para quien lo pide es lo mismo.
    /// </summary>
    public async Task<DocumentoLeido?> AbrirArchivoAsync(int documentoId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var ruta = await db.Documentos.AsNoTracking()
            .Where(d => d.Id == documentoId)
            .Select(d => d.RutaRelativa)
            .FirstOrDefaultAsync();
        if (ruta is null) return null;

        try
        {
            return _lector.LeerDocumento(ruta);
        }
        catch (ValidacionException)
        {
            return null;
        }
    }

    /// <summary>Documento tal como sale en la ficha; mira en el disco si el archivo sigue en su sitio.</summary>
    internal static DocumentoDto ADto(Documento d, RutasCurriculum? rutas) => new(
        d.Id, d.CandidaturaId, d.Tipo, d.NombreArchivo, d.RutaRelativa, CarpetaDe(d.RutaRelativa),
        Extension(d.NombreArchivo), rutas is { HayCarpeta: true } && RutaSegura.Existe(rutas.Carpeta, d.RutaRelativa),
        d.Origen, d.Vigente, d.FechaAlta);

    // ---------------------------------------------------------------- auxiliares

    /// <summary>De cada documento (misma carpeta y mismo nombre sin extensión), el PDF; el .docx solo si no hay PDF.</summary>
    private static IEnumerable<ArchivoDisponible> UnoPorDocumento(IEnumerable<ArchivoDisponible> archivos) =>
        archivos
            .GroupBy(a => $"{a.Carpeta}/{Path.GetFileNameWithoutExtension(a.NombreArchivo)}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(a => Extension(a.NombreArchivo) == "pdf" ? 0 : 1).First());

    /// <summary>Primero las semanas (por cercanía a la de la candidatura), luego la raíz y al final Archivo.</summary>
    private static (int Grupo, int Distancia) OrdenCarpeta(string carpeta, DateOnly lunesCandidatura)
    {
        if (EsAntiguo(carpeta)) return (2, 0);
        return LunesDe(carpeta) is { } lunes
            ? (0, Math.Abs(lunes.DayNumber - lunesCandidatura.DayNumber))
            : (1, 0);
    }

    /// <summary>Lunes de la carpeta de semana, o null si no es de una semana (raíz, Archivo).</summary>
    private static DateOnly? LunesDe(string carpeta) =>
        EsAntiguo(carpeta) ? null : CarpetasSemana.Lunes(carpeta.Split('/')[^1]);

    private static bool EsAntiguo(string carpeta) =>
        carpeta.Equals(RutasCurriculum.CarpetaArchivo, StringComparison.OrdinalIgnoreCase) ||
        carpeta.StartsWith(RutasCurriculum.CarpetaArchivo + "/", StringComparison.OrdinalIgnoreCase);

    private static string CarpetaDe(string rutaRelativa)
    {
        var barra = rutaRelativa.LastIndexOf('/');
        return barra < 0 ? "" : rutaRelativa[..barra];
    }

    private static string Extension(string nombreArchivo) =>
        Path.GetExtension(nombreArchivo).TrimStart('.').ToLowerInvariant();

    private static bool MismaRuta(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
