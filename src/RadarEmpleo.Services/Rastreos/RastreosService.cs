using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Services.Rastreos;

/// <summary>
/// Bandeja de rastreos: leer los informes de Curriculum (las secciones «## Rastreo …» del registro y
/// <c>rastreos\rastreo_*.md</c>), listarlos y pasar una oferta al tablero o dejarla de lado. Los archivos
/// solo se leen. Volver a escanear es seguro: un informe que no ha cambiado (mismo hash) no se toca, en uno
/// que ha crecido solo se añaden las ofertas nuevas, y nunca se borra nada ni se cambia el estado de una
/// oferta. Cada oferta se guarda una sola vez aunque salga en varios rastreos (por su enlace).
/// </summary>
public sealed class RastreosService
{
    // Estático: una sola escritura de rastreos a la vez en toda la API. Un doble clic en «Pasar al tablero»
    // o dos pestañas escaneando a la vez no crean dos candidaturas ni chocan con los índices únicos.
    private static readonly SemaphoreSlim Cerrojo = new(1, 1);

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILectorCurriculum _lector;
    private readonly RutasCurriculum _rutas;
    private readonly CandidaturasService _candidaturas;
    private readonly IClock _reloj;

    public RastreosService(IDbContextFactory<AppDbContext> factory, ILectorCurriculum lector, RutasCurriculum rutas,
        CandidaturasService candidaturas, IClock reloj)
    {
        _factory = factory;
        _lector = lector;
        _rutas = rutas;
        _candidaturas = candidaturas;
        _reloj = reloj;
    }

    /// <summary>Los rastreos, del más reciente al más antiguo (los del mismo día, el último leído primero).</summary>
    public async Task<List<RastreoResumenDto>> ListarAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Rastreos.AsNoTracking()
            .OrderByDescending(r => r.Fecha).ThenByDescending(r => r.Id)
            .Select(r => new RastreoResumenDto(
                r.Id, r.Fecha, r.Titulo, r.Origen,
                r.Ofertas.Count,
                r.Ofertas.Count(o => o.Estado == EstadoOfertaRastreo.Pendiente
                                     || (o.Estado == EstadoOfertaRastreo.Convertida && o.CandidaturaId == null)),
                r.FechaEscaneo))
            .ToListAsync();
    }

    /// <summary>El rastreo con su informe y sus ofertas en el orden del informe; null si no existe.</summary>
    public async Task<RastreoDetalleDto?> ObtenerAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var rastreo = await db.Rastreos.AsNoTracking().Include(r => r.Ofertas).FirstOrDefaultAsync(r => r.Id == id);
        if (rastreo is null) return null;

        var ofertas = rastreo.Ofertas.OrderBy(o => o.Linea).ThenBy(o => o.Id).Select(ADto).ToList();
        return new RastreoDetalleDto(rastreo.Id, rastreo.Fecha, rastreo.Titulo, rastreo.Origen, ofertas.Count,
            ofertas.Count(o => o.Estado == EstadoOfertaRastreo.Pendiente), rastreo.FechaEscaneo,
            rastreo.RutaInforme, rastreo.TextoInforme, ofertas);
    }

    /// <summary>
    /// Lee los informes y guarda lo nuevo. Las ofertas cuyo enlace ya está en el tablero se crean como
    /// convertidas (enlazadas a su candidatura) y las que el informe da por descartadas, como ignoradas. Lo
    /// que no se puede leer (un registro que no se abre, un <c>.json</c>, una sección sin fecha) se cuenta en
    /// los avisos y no impide leer lo demás.
    /// </summary>
    /// <exception cref="ReglaNegocioException">Falta la carpeta Curriculum.</exception>
    public async Task<ResultadoEscaneoDto> EscanearAsync()
    {
        if (!_rutas.HayCarpeta) throw new ReglaNegocioException(RutasCurriculum.MensajeSinCarpeta);

        var hoy = DateOnly.FromDateTime(Fechas.UtcAMadrid(_reloj.AhoraUtc));
        var leidos = new List<RastreoLeido>();
        var avisos = new List<string>();

        try
        {
            var registro = AnalizadorRastreo.AnalizarRegistro(await _lector.LeerRegistroAsync(), hoy);
            leidos.AddRange(registro.Rastreos);
            avisos.AddRange(registro.Avisos);
        }
        catch (ReglaNegocioException error)
        {
            // Sin registro (o sin poder abrirlo) aún se pueden leer los informes de la tarea de los lunes.
            avisos.Add(error.Message);
        }

        var informes = await _lector.LeerInformesRastreoAsync();
        foreach (var informe in informes.Informes)
        {
            var analisis = AnalizadorRastreo.AnalizarInforme(informe.RutaRelativa, informe.Texto);
            leidos.AddRange(analisis.Rastreos);
            avisos.AddRange(analisis.Avisos);
        }
        avisos.AddRange(informes.Json.Select(r => $"Todavía no sé leer «{Nombre(r)}»: lo he dejado sin leer."));
        avisos.AddRange(informes.NoLeidos.Select(r => $"No he podido abrir «{Nombre(r)}». Vuelve a buscar en un momento."));

        await Cerrojo.WaitAsync();
        try
        {
            return await GuardarAsync(leidos, avisos);
        }
        finally
        {
            Cerrojo.Release();
        }
    }

    /// <summary>
    /// «Pasar al tablero»: crea la candidatura en Detectada con los datos de la oferta (origen Rastreo, con
    /// su enlace, encaje, pega, el texto de la línea como análisis y la fecha del rastreo como fecha de
    /// detección) y la enlaza. Si ya hay una candidatura con ese enlace, la enlaza sin crear otra
    /// (<see cref="ResultadoDeteccionDto.YaExistia"/>). Vale desde Pendiente y desde Ignorada, y repetirlo no
    /// crea nada más. Devuelve null si la oferta no existe.
    /// </summary>
    public async Task<ResultadoDeteccionDto?> DetectarAsync(int ofertaId)
    {
        await Cerrojo.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var oferta = await db.OfertasRastreo.Include(o => o.Rastreo).FirstOrDefaultAsync(o => o.Id == ofertaId);
            if (oferta is null) return null;

            // Si se borra la candidatura, la BD deja CandidaturaId en null: con él puesto, la candidatura existe.
            if (oferta is { Estado: EstadoOfertaRastreo.Convertida, CandidaturaId: { } enlazada })
                return new ResultadoDeteccionDto(ADto(oferta), enlazada, YaExistia: true);

            var enElTablero = await EnlacesDelTableroAsync(db);
            int? existente = Enlaces.ParaComparar(oferta.Enlace) is { } clave && enElTablero.TryGetValue(clave, out var id)
                ? id
                : null;
            var candidaturaId = existente ?? (await _candidaturas.CrearAsync(DatosCandidatura(oferta))).Id;

            oferta.Convertir(candidaturaId, _reloj.AhoraUtc);
            await db.SaveChangesAsync();
            return new ResultadoDeteccionDto(ADto(oferta), candidaturaId, YaExistia: existente is not null);
        }
        finally
        {
            Cerrojo.Release();
        }
    }

    /// <summary>«No me interesa». Devuelve null si la oferta no existe.</summary>
    /// <exception cref="ReglaNegocioException">La oferta ya está en el tablero (409).</exception>
    public async Task<OfertaRastreoDto?> IgnorarAsync(int ofertaId)
    {
        await Cerrojo.WaitAsync();
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            var oferta = await db.OfertasRastreo.FirstOrDefaultAsync(o => o.Id == ofertaId);
            if (oferta is null) return null;

            oferta.Ignorar(_reloj.AhoraUtc);
            await db.SaveChangesAsync();
            return ADto(oferta);
        }
        finally
        {
            Cerrojo.Release();
        }
    }

    // ---------------------------------------------------------------- auxiliares

    private async Task<ResultadoEscaneoDto> GuardarAsync(List<RastreoLeido> leidos, List<string> avisos)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var ahora = _reloj.AhoraUtc;
        var existentes = await db.Rastreos.ToDictionaryAsync(r => r.Clave, StringComparer.Ordinal);
        var enlaces = (await db.OfertasRastreo.Select(o => o.Enlace).ToListAsync())
            .Select(Enlaces.ParaComparar).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var enElTablero = await EnlacesDelTableroAsync(db);

        int nuevos = 0, actualizados = 0, ofertasNuevas = 0, yaEnElTablero = 0;
        foreach (var leido in leidos)
        {
            if (existentes.TryGetValue(leido.Clave, out var rastreo))
            {
                if (rastreo.Hash == leido.Hash) continue;
                // La fecha no se toca: la de un «DD/MM» depende del día en que se lee.
                rastreo.Titulo = leido.Titulo;
                rastreo.TextoInforme = leido.Texto;
                rastreo.Hash = leido.Hash;
                rastreo.FechaEscaneo = ahora;
                actualizados++;
            }
            else
            {
                rastreo = new Rastreo
                {
                    Clave = leido.Clave,
                    Origen = leido.Origen,
                    Titulo = leido.Titulo,
                    Fecha = leido.Fecha,
                    RutaInforme = leido.RutaInforme,
                    Hash = leido.Hash,
                    TextoInforme = leido.Texto,
                    FechaEscaneo = ahora
                };
                db.Rastreos.Add(rastreo);
                existentes[leido.Clave] = rastreo;
                nuevos++;
            }

            foreach (var leida in leido.Ofertas)
            {
                // La misma oferta en otro rastreo (o dos veces en este) ya está: se queda donde salió primero.
                if (Enlaces.ParaComparar(leida.Enlace) is not { } clave || !enlaces.Add(clave)) continue;

                var oferta = new OfertaRastreo
                {
                    Linea = leida.Linea,
                    TextoOriginal = leida.TextoOriginal,
                    Empresa = leida.Empresa,
                    Puesto = leida.Puesto,
                    Enlace = leida.Enlace,
                    Encaje = leida.Encaje,
                    Pega = leida.Pega,
                    Estado = leida.Ignorada ? EstadoOfertaRastreo.Ignorada : EstadoOfertaRastreo.Pendiente,
                    FechaEstado = ahora
                };
                if (enElTablero.TryGetValue(clave, out var candidaturaId))
                {
                    oferta.Convertir(candidaturaId, ahora);
                    yaEnElTablero++;
                }
                rastreo.Ofertas.Add(oferta);
                ofertasNuevas++;
            }
        }

        await db.SaveChangesAsync();
        return new ResultadoEscaneoDto(nuevos, actualizados, ofertasNuevas, yaEnElTablero,
            leidos.Sum(l => l.LineasSinLeer), avisos);
    }

    /// <summary>Candidaturas del tablero por enlace (<see cref="Enlaces.ParaComparar"/>); con dos iguales, la más antigua.</summary>
    private static async Task<Dictionary<string, int>> EnlacesDelTableroAsync(AppDbContext db)
    {
        var filas = await db.Candidaturas.AsNoTracking()
            .Where(c => c.Enlace != null)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Enlace })
            .ToListAsync();

        var porEnlace = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fila in filas)
        {
            if (Enlaces.ParaComparar(fila.Enlace) is { } clave) porEnlace.TryAdd(clave, fila.Id);
        }
        return porEnlace;
    }

    private static GuardarCandidaturaRequest DatosCandidatura(OfertaRastreo oferta) => new(
        oferta.Empresa,
        oferta.Puesto,
        oferta.Enlace,
        Encaje: oferta.Encaje,
        Pega: oferta.Pega,
        Analisis: AnalizadorRastreo.TextoPlano(oferta.TextoOriginal),
        FechaDeteccion: Fechas.MedianocheMadridEnUtc(oferta.Rastreo.Fecha.ToDateTime(TimeOnly.MinValue)),
        Origen: Origen.Rastreo);

    private static OfertaRastreoDto ADto(OfertaRastreo o) => new(
        o.Id, o.RastreoId, o.Linea, o.Empresa, o.Puesto, o.Enlace, o.Encaje, o.Pega,
        OfertaRastreo.EstadoVisible(o.Estado, o.CandidaturaId), o.CandidaturaId, o.FechaEstado, o.TextoOriginal);

    private static string Nombre(string rutaRelativa) => rutaRelativa[(rutaRelativa.LastIndexOf('/') + 1)..];
}
