using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Services;

/// <summary>
/// Candidaturas: listado con filtros, ficha, alta, edición, borrado, cambio de estado y notas.
/// Cada operación abre su propio contexto con la factory. Los métodos que buscan por id
/// devuelven null (o false) si no existe, y el controlador lo traduce a 404. Los datos inválidos
/// lanzan <see cref="ValidacionException"/> (400 en la API) y las reglas de negocio incumplidas,
/// <see cref="ReglaNegocioException"/> (409). Lo que sale en el radar se exporta después de guardar;
/// las notas no salen en él.
/// </summary>
public class CandidaturasService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _reloj;
    private readonly IExportadorRadar _exportador;
    private readonly RutasCurriculum? _rutas;

    /// <param name="rutas">Carpeta Curriculum, para decir en la ficha si cada documento sigue en su sitio.
    /// Sin ella, todos salen como no encontrados.</param>
    public CandidaturasService(IDbContextFactory<AppDbContext> factory, IClock reloj, IExportadorRadar exportador,
        RutasCurriculum? rutas = null)
    {
        _factory = factory;
        _reloj = reloj;
        _exportador = exportador;
        _rutas = rutas;
    }

    /// <summary>
    /// Listado ordenado por último cambio (más reciente primero). Estado y fecha se filtran en SQL;
    /// el texto se filtra en memoria con <see cref="TextoComparable"/> para ignorar las tildes, que
    /// el LIKE de SQLite no sabe ignorar (el volumen es de cientos de filas, no de millones).
    /// </summary>
    public async Task<List<CandidaturaResumenDto>> ListarAsync(FiltroCandidaturas? filtro = null)
    {
        filtro ??= new FiltroCandidaturas();
        // En la query string ASP.NET acepta «?estado=99» como número: aquí se rechaza.
        if (filtro.Estado is { } estadoFiltro)
            ValidacionException.ExigirDefinido(estadoFiltro, "estado");

        await using var db = await _factory.CreateDbContextAsync();

        var consulta = db.Candidaturas.AsNoTracking().Include(c => c.Empresa).AsQueryable();
        if (filtro.Estado is { } estado)
            consulta = consulta.Where(c => c.Estado == estado);
        if (filtro.Desde is { } desde)
        {
            var desdeUtc = InicioFiltroDesde(desde);
            consulta = consulta.Where(c => c.FechaDeteccion >= desdeUtc);
        }

        var lista = await consulta.OrderByDescending(c => c.FechaUltimoCambio).ThenByDescending(c => c.Id).ToListAsync();

        var texto = TextoComparable.Normalizar(filtro.Texto);
        if (texto.Length > 0)
        {
            lista = lista.Where(c =>
                TextoComparable.Contiene(c.Puesto, texto) ||
                TextoComparable.Contiene(c.Empresa.Nombre, texto) ||
                TextoComparable.Contiene(c.Ubicacion, texto) ||
                TextoComparable.Contiene(c.Portal, texto) ||
                TextoComparable.Contiene(c.Pega, texto)).ToList();
        }

        return lista.Select(AResumen).ToList();
    }

    /// <summary>Ficha completa con historial, notas, recordatorios y documentos; null si no existe.</summary>
    public async Task<CandidaturaDetalleDto?> ObtenerAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var candidatura = await CargarCompletaAsync(db, id, seguimiento: false);
        return candidatura is null ? null : ADetalle(candidatura);
    }

    /// <summary>
    /// Alta en «Detectada». La empresa se reutiliza si ya existe una con el mismo nombre
    /// normalizado (minúsculas, sin tildes, sin S.L./S.A.); si no, se crea.
    /// </summary>
    public async Task<CandidaturaDetalleDto> CrearAsync(GuardarCandidaturaRequest datos)
    {
        var puesto = Obligatorio(datos.Puesto, "El puesto es obligatorio.", "puesto");
        ValidarEnums(datos);
        await using var db = await _factory.CreateDbContextAsync();

        var empresa = await EmpresasService.ObtenerOCrearAsync(db, datos.Empresa);
        var ahora = _reloj.AhoraUtc;
        var candidatura = Candidatura.Nueva(empresa, puesto, ahora, datos.Origen,
            datos.FechaDeteccion is { } f ? Fechas.AUtc(f) : null);
        CopiarDatos(datos, candidatura);

        db.Candidaturas.Add(candidatura);
        await EmpresasService.GuardarReutilizandoEmpresasAsync(db);
        await _exportador.ExportarAsync();

        // La entidad recién guardada ya tiene todo lo que pinta la ficha (sin abrir otro contexto).
        return ADetalle(candidatura);
    }

    /// <summary>
    /// Edita los datos (no el estado). Si cambia el nombre de la empresa, se resuelve de nuevo
    /// (reutilizar o crear). Devuelve null si no existe.
    /// </summary>
    public async Task<CandidaturaDetalleDto?> EditarAsync(int id, GuardarCandidaturaRequest datos)
    {
        var puesto = Obligatorio(datos.Puesto, "El puesto es obligatorio.", "puesto");
        ValidarEnums(datos);
        await using var db = await _factory.CreateDbContextAsync();

        var candidatura = await CargarCompletaAsync(db, id, seguimiento: true);
        if (candidatura is null) return null;

        if (NombresEmpresa.Normalizar(datos.Empresa) != candidatura.Empresa.NombreNormalizado)
            candidatura.Empresa = await EmpresasService.ObtenerOCrearAsync(db, datos.Empresa);

        candidatura.Puesto = puesto;
        CopiarDatos(datos, candidatura);
        if (datos.FechaDeteccion is { } f) candidatura.FechaDeteccion = Fechas.AUtc(f);
        // Solo la última modificación: FechaEstado (los «días en el estado») no se toca al editar.
        candidatura.FechaUltimoCambio = _reloj.AhoraUtc;

        await EmpresasService.GuardarReutilizandoEmpresasAsync(db);
        await _exportador.ExportarAsync();
        return ADetalle(candidatura);
    }

    /// <summary>
    /// Borra la candidatura con su historial, notas y recordatorios (cascada). La empresa se queda.
    /// Devuelve false si no existe, también si otra petición la acaba de borrar. Si otra la acaba de
    /// cambiar, sigue saltando el control de concurrencia (409): lo que se iba a borrar ya no es lo que se vio.
    /// </summary>
    public async Task<bool> BorrarAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var candidatura = await db.Candidaturas.FirstOrDefaultAsync(c => c.Id == id);
        if (candidatura is null) return false;

        db.Candidaturas.Remove(candidatura);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await using var otra = await _factory.CreateDbContextAsync();
            if (await otra.Candidaturas.AnyAsync(c => c.Id == id)) throw;
            return false;
        }
        await _exportador.ExportarAsync();
        return true;
    }

    /// <summary>
    /// Cambia el estado aplicando <see cref="ReglasEstado"/> y los efectos de
    /// <see cref="Candidatura.CambiarEstado"/> (historial, fecha de envío, seguimiento a las 09:00 de
    /// Madrid a +7 días, cierre de seguimientos al descartar, cerrar o reabrir).
    /// Devuelve la ficha actualizada o null si no existe.
    /// </summary>
    public async Task<CandidaturaDetalleDto?> CambiarEstadoAsync(int id, CambiarEstadoRequest peticion)
    {
        // La API ya lo exige con [Required]; esto cubre a quien llame al servicio directamente.
        if (peticion.Estado is not { } estado)
            throw new ValidacionException("El estado es obligatorio.", "estado");
        ValidacionException.ExigirDefinido(peticion.Origen, "origen");

        await using var db = await _factory.CreateDbContextAsync();
        var candidatura = await CargarCompletaAsync(db, id, seguimiento: true);
        if (candidatura is null) return null;

        candidatura.CambiarEstado(
            estado,
            _reloj.AhoraUtc,
            peticion.Origen,
            peticion.MotivoDescarte,
            TextoOpcional(peticion.MotivoDescarteTexto),
            TextoOpcional(peticion.Comentario));

        await db.SaveChangesAsync();
        await _exportador.ExportarAsync();
        return ADetalle(candidatura);
    }

    /// <summary>Añade una nota con la fecha actual. Devuelve null si la candidatura no existe.</summary>
    public async Task<NotaDto?> AnadirNotaAsync(int candidaturaId, CrearNotaRequest peticion)
    {
        var texto = Obligatorio(peticion.Texto, "El texto de la nota es obligatorio.", "texto");
        ValidacionException.ExigirDefinido(peticion.Origen, "origen");
        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.Candidaturas.AnyAsync(c => c.Id == candidaturaId)) return null;

        var nota = new Nota
        {
            CandidaturaId = candidaturaId,
            Texto = texto,
            Fecha = _reloj.AhoraUtc,
            Origen = peticion.Origen
        };
        db.Notas.Add(nota);
        await db.SaveChangesAsync();
        return ANota(nota);
    }

    /// <summary>Borra una nota. Devuelve false si no existe, también si otra petición la acaba de borrar.</summary>
    public async Task<bool> BorrarNotaAsync(int notaId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var nota = await db.Notas.FirstOrDefaultAsync(n => n.Id == notaId);
        if (nota is null) return false;

        db.Notas.Remove(nota);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Las notas no tienen token: el DELETE solo no borra nada si ya no estaba.
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- auxiliares

    private static Task<Candidatura?> CargarCompletaAsync(AppDbContext db, int id, bool seguimiento)
    {
        var consulta = db.Candidaturas
            .Include(c => c.Empresa)
            .Include(c => c.Historial)
            .Include(c => c.Notas)
            .Include(c => c.Recordatorios)
            .Include(c => c.Documentos)
            .AsSplitQuery();
        if (!seguimiento) consulta = consulta.AsNoTracking();
        return consulta.FirstOrDefaultAsync(c => c.Id == id);
    }

    private static void CopiarDatos(GuardarCandidaturaRequest d, Candidatura c)
    {
        c.Enlace = EnlaceValidado(d.Enlace);
        c.Portal = TextoOpcional(d.Portal);
        c.Modalidad = d.Modalidad;
        c.ModalidadDetalle = TextoOpcional(d.ModalidadDetalle);
        c.Ubicacion = TextoOpcional(d.Ubicacion);
        c.Encaje = d.Encaje;
        c.Pega = TextoOpcional(d.Pega);
        c.Analisis = TextoOpcional(d.Analisis);
        c.Salario = TextoOpcional(d.Salario);
    }

    private static void ValidarEnums(GuardarCandidaturaRequest d)
    {
        ValidacionException.ExigirDefinido(d.Modalidad, "modalidad");
        ValidacionException.ExigirDefinido(d.Encaje, "encaje");
        ValidacionException.ExigirDefinido(d.Origen, "origen");
    }

    private static string Obligatorio(string? valor, string mensaje, string campo) =>
        string.IsNullOrWhiteSpace(valor) ? throw new ValidacionException(mensaje, campo) : valor.Trim();

    private static string? TextoOpcional(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string? EnlaceValidado(string? enlace)
    {
        var normalizado = Enlaces.Normalizar(enlace)
            ?? throw new ValidacionException("El enlace tiene que ser una dirección web.", "enlace");
        return TextoOpcional(normalizado);
    }

    /// <summary>
    /// Inicio del filtro «desde» en UTC. Una fecha sola (sin hora ni zona: <see cref="DateTimeKind.Unspecified"/>
    /// a las 00:00, que es como llega <c>?desde=2026-10-27</c>) se toma como medianoche en Madrid, que es
    /// lo que quiere decir quien filtra por día. Cualquier otra se trata como el resto de fechas de
    /// entrada (<see cref="Fechas.AUtc"/>).
    /// </summary>
    private static DateTime InicioFiltroDesde(DateTime desde) =>
        desde.Kind == DateTimeKind.Unspecified && desde.TimeOfDay == TimeSpan.Zero
            ? Fechas.MedianocheMadridEnUtc(desde)
            : Fechas.AUtc(desde);

    private static CandidaturaResumenDto AResumen(Candidatura c) => new(
        c.Id, c.EmpresaId, c.Empresa.Nombre, c.Puesto, c.Enlace, c.Portal, c.Modalidad, c.Ubicacion,
        c.Encaje, c.Pega, c.Estado, c.FechaDeteccion, c.FechaEnvio, c.FechaEstado, c.FechaUltimoCambio, c.MotivoDescarte, c.Origen);

    private CandidaturaDetalleDto ADetalle(Candidatura c) => new(
        c.Id, c.EmpresaId, c.Empresa.Nombre, c.Puesto, c.Enlace, c.Portal, c.Modalidad, c.ModalidadDetalle,
        c.Ubicacion, c.Encaje, c.Pega, c.Analisis, c.Salario, c.Estado, c.FechaDeteccion, c.FechaEnvio,
        c.FechaEstado, c.FechaUltimoCambio, c.MotivoDescarte, c.MotivoDescarteTexto, c.Origen, c.SeccionOrigen,
        ReglasEstado.EstadosPosibles(c.Estado),
        c.Historial.OrderBy(h => h.Fecha).ThenBy(h => h.Id)
            .Select(h => new CambioEstadoDto(h.Id, h.De, h.A, h.Fecha, h.Comentario, h.Origen)).ToList(),
        c.Notas.OrderByDescending(n => n.Fecha).ThenByDescending(n => n.Id).Select(ANota).ToList(),
        c.Recordatorios.OrderBy(r => r.FechaHora).ThenBy(r => r.Id)
            .Select(r => new RecordatorioDto(r.Id, r.CandidaturaId, r.Tipo, r.Titulo, r.FechaHora, r.Hecho, r.Notas)).ToList(),
        c.Documentos.OrderBy(d => d.Tipo).ThenByDescending(d => d.Vigente).ThenByDescending(d => d.FechaAlta).ThenByDescending(d => d.Id)
            .Select(d => DocumentosService.ADto(d, _rutas)).ToList(),
        _rutas?.HayCarpeta ?? false);

    private static NotaDto ANota(Nota n) => new(n.Id, n.CandidaturaId, n.Texto, n.Fecha, n.Origen);
}
