using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Estadisticas;

namespace RadarEmpleo.Services;

/// <summary>
/// Recordatorios (sueltos o de una candidatura) y el panel de Hoy. Cada operación abre su propio contexto.
/// Los métodos que buscan por id devuelven null (o false) si no existe, y el controlador lo traduce a 404.
/// Los datos inválidos lanzan <see cref="ValidacionException"/> (400) y lo que no se permite,
/// <see cref="ReglaNegocioException"/> (409), también cuando otra petición cambia el mismo recordatorio
/// entre medias. Nada de esto toca la candidatura ni el radar: en él no salen recordatorios.
/// </summary>
public class RecordatoriosService
{
    public const string MensajeCambiadoEnOtraPestana =
        "Otra pestaña acaba de cambiar este recordatorio. Vuelve a cargar Inicio para verlo como ha quedado.";

    public const string MensajeCandidaturaFinal =
        "Esta candidatura está descartada o cerrada: no se le pueden apuntar recordatorios. Reábrela si sigue viva.";

    /// <summary>Días sin respuesta a partir de los que una enviada sale en Hoy, y hasta los que sale en la lista.</summary>
    public const int DiasSinRespuestaMinimo = 7;
    public const int DiasSinRespuestaMaximo = 30;

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _reloj;
    private readonly EstadisticasService _estadisticas;

    public RecordatoriosService(IDbContextFactory<AppDbContext> factory, IClock reloj, EstadisticasService estadisticas)
    {
        _factory = factory;
        _reloj = reloj;
        _estadisticas = estadisticas;
    }

    /// <summary>Un recordatorio por su id; null si no existe.</summary>
    public async Task<RecordatorioDto?> ObtenerAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var recordatorio = await db.Recordatorios.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return recordatorio is null ? null : ADto(recordatorio);
    }

    /// <summary>
    /// Apunta un recordatorio, suelto o de la candidatura <paramref name="candidaturaId"/>. Devuelve null
    /// si esa candidatura no existe.
    /// </summary>
    /// <exception cref="ReglaNegocioException">La candidatura está descartada o cerrada: lo que se le apuntara no saldría en Hoy.</exception>
    public async Task<RecordatorioDto?> CrearAsync(GuardarRecordatorioRequest datos, int? candidaturaId = null)
    {
        var validos = Validar(datos);
        await using var db = await _factory.CreateDbContextAsync();
        if (candidaturaId is { } id)
        {
            var estado = await db.Candidaturas.Where(c => c.Id == id).Select(c => (EstadoCandidatura?)c.Estado).FirstOrDefaultAsync();
            if (estado is null) return null;
            if (ReglasEstado.EsFinal(estado.Value)) throw new ReglaNegocioException(MensajeCandidaturaFinal);
        }

        var recordatorio = new Recordatorio { CandidaturaId = candidaturaId };
        Copiar(validos, recordatorio);
        db.Recordatorios.Add(recordatorio);
        await db.SaveChangesAsync();
        return ADto(recordatorio);
    }

    /// <summary>
    /// Cambia el tipo, el título, la fecha y las notas. No toca si está hecho ni de qué candidatura es.
    /// Devuelve null si no existe.
    /// </summary>
    public async Task<RecordatorioDto?> EditarAsync(int id, GuardarRecordatorioRequest datos)
    {
        var validos = Validar(datos);
        return await CambiarAsync(id, r => Copiar(validos, r));
    }

    /// <summary>
    /// Lo marca como hecho (con la fecha de ahora) o lo deja pendiente otra vez. Repetirlo no cambia nada,
    /// tampoco si las dos peticiones llegan a la vez: la segunda lo encuentra ya así y lo devuelve tal cual.
    /// Devuelve null si no existe.
    /// </summary>
    public Task<RecordatorioDto?> MarcarHechoAsync(int id, MarcarHechoRequest peticion)
    {
        // La API ya lo exige con [Required]; esto cubre a quien llame al servicio directamente.
        if (peticion.Hecho is not { } hecho)
            throw new ValidacionException("Falta decir si está hecho.", "hecho");

        var ahora = _reloj.AhoraUtc;
        return CambiarAsync(id, r =>
        {
            if (hecho) r.MarcarHecho(ahora);
            else if (r.Hecho) r.MarcarPendiente();
        }, r => r.Hecho == hecho);
    }

    /// <summary>
    /// Lo pasa a otro día de Madrid, a la misma hora. Admite días pasados, para que deshacer un posponer
    /// lo devuelva a su día. Devuelve null si no existe.
    /// </summary>
    /// <exception cref="ReglaNegocioException">Ya está hecho.</exception>
    public Task<RecordatorioDto?> PosponerAsync(int id, PosponerRecordatorioRequest peticion)
    {
        if (peticion.Dia is not { } dia)
            throw new ValidacionException("Falta el día.", "dia");
        ReglasRecordatorio.ValidarDia(dia);

        return CambiarAsync(id, r => r.PosponerA(dia));
    }

    /// <summary>Borra un recordatorio. Devuelve false si no existe, también si otra petición lo acaba de borrar.</summary>
    public async Task<bool> BorrarAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var recordatorio = await db.Recordatorios.FirstOrDefaultAsync(r => r.Id == id);
        if (recordatorio is null) return false;

        db.Recordatorios.Remove(recordatorio);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (await ExisteAsync(id)) throw new ReglaNegocioException(MensajeCambiadoEnOtraPestana);
            return false;
        }
        return true;
    }

    /// <summary>
    /// El panel de Hoy. Los cortes de día salen de la medianoche de Madrid de cada día (no de sumar 24 h,
    /// que falla los días de cambio de hora). Lleva el resumen de <see cref="EstadisticasService.ResumenHoyAsync"/>.
    /// </summary>
    public async Task<PanelHoyDto> PanelHoyAsync()
    {
        var ahora = _reloj.AhoraUtc;
        var hoy = ReglasRecordatorio.DiaMadrid(ahora);
        var inicioHoy = Medianoche(hoy);
        var manana = Medianoche(hoy.AddDays(1));
        var finProximos = Medianoche(hoy.AddDays(1 + ReglasRecordatorio.DiasProximos));

        await using var db = await _factory.CreateDbContextAsync();

        // Los de una candidatura descartada o cerrada no se enseñan mientras siga así: vuelven solos si se reabre.
        var visibles = db.Recordatorios.AsNoTracking()
            .Include(r => r.Candidatura!).ThenInclude(c => c.Empresa)
            .Where(ReglasRecordatorio.SeEnsena);

        var pendientes = (await visibles.Where(r => !r.Hecho).ToListAsync())
            .OrderBy(r => r.FechaHora).ThenBy(r => r.Id).ToList();
        var hechosHoy = (await visibles
                .Where(r => r.Hecho && r.FechaHecho != null && r.FechaHecho >= inicioHoy && r.FechaHecho < manana)
                .ToListAsync())
            .OrderByDescending(r => r.FechaHecho).ThenByDescending(r => r.Id).ToList();
        var cerradosSolos = await CerradosAlCambiarEstadoAsync(db, hechosHoy);

        var enviadas = await db.Candidaturas.AsNoTracking()
            .Where(c => c.Estado == EstadoCandidatura.Enviada && c.FechaEnvio != null
                && !c.Recordatorios.Any(r => r.Tipo == TipoRecordatorio.Seguimiento && !r.Hecho))
            .Select(c => new { c.Id, Empresa = c.Empresa.Nombre, c.Puesto, FechaEnvio = c.FechaEnvio!.Value })
            .ToListAsync();
        var sinRespuesta = enviadas
            .Select(c => new EnviadaSinRespuestaDto(c.Id, c.Empresa, c.Puesto, c.FechaEnvio,
                hoy.DayNumber - ReglasRecordatorio.DiaMadrid(c.FechaEnvio).DayNumber))
            .ToList();

        return new PanelHoyDto(
            hoy,
            pendientes.Where(r => r.FechaHora < manana).Select(AAgenda).ToList(),
            pendientes.Where(r => r.FechaHora >= manana && r.FechaHora < finProximos).Select(AAgenda).ToList(),
            pendientes.Where(r => r.FechaHora >= finProximos).Select(AAgenda).ToList(),
            hechosHoy.Select(r => AAgenda(r) with { CerradoAlPasarA = cerradosSolos.GetValueOrDefault(r.Id) }).ToList(),
            sinRespuesta.Where(c => c.DiasDesdeEnvio is >= DiasSinRespuestaMinimo and <= DiasSinRespuestaMaximo)
                .OrderByDescending(c => c.FechaEnvio).ThenByDescending(c => c.Id).ToList(),
            sinRespuesta.Count(c => c.DiasDesdeEnvio > DiasSinRespuestaMaximo),
            await _estadisticas.ResumenHoyAsync());
    }

    public static RecordatorioDto ADto(Recordatorio r) =>
        new(r.Id, r.CandidaturaId, r.Tipo, r.Titulo, r.FechaHora, r.Hecho, r.Notas, r.FechaHecho);

    // ---------------------------------------------------------------- auxiliares

    /// <summary>
    /// De los hechos de <paramref name="hechos"/>, los seguimientos que se dieron por hechos solos al
    /// cambiar la candidatura de estado (al pasar a Entrevista u Oferta), con el estado al que pasó. Se
    /// sabe por el historial: el cambio que lo cerró lo apunta y lleva su misma fecha (si después se
    /// dejó pendiente y se volvió a marcar a mano, la fecha ya no coincide).
    /// </summary>
    private static async Task<Dictionary<int, EstadoCandidatura?>> CerradosAlCambiarEstadoAsync(
        AppDbContext db, IReadOnlyList<Recordatorio> hechos)
    {
        var seguimientos = hechos.Where(r => r.Tipo == TipoRecordatorio.Seguimiento).ToDictionary(r => r.Id);
        if (seguimientos.Count == 0) return [];

        var ids = seguimientos.Keys.ToList();
        var cambios = await db.CambiosEstado.AsNoTracking()
            .Where(h => h.SeguimientoCerradoId != null && ids.Contains(h.SeguimientoCerradoId.Value))
            .Select(h => new { Id = h.SeguimientoCerradoId!.Value, h.A, h.Fecha })
            .ToListAsync();
        return cambios
            .Where(h => seguimientos[h.Id].FechaHecho == h.Fecha)
            .GroupBy(h => h.Id)
            .ToDictionary(g => g.Key, g => (EstadoCandidatura?)g.First().A);
    }

    /// <summary>
    /// Lee el recordatorio, le aplica <paramref name="cambio"/> y guarda. Si otra petición lo ha cambiado
    /// entre la lectura y el guardado, salta el token de concurrencia y se dice con el mensaje del
    /// recordatorio (no con el de la candidatura); si lo ha borrado, es como si no existiera. Con
    /// <paramref name="yaHecho"/>, si la otra petición lo ha dejado justo como se pedía, no es un choque:
    /// se devuelve como ha quedado.
    /// </summary>
    private async Task<RecordatorioDto?> CambiarAsync(int id, Action<Recordatorio> cambio, Func<Recordatorio, bool>? yaHecho = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var recordatorio = await db.Recordatorios.FirstOrDefaultAsync(r => r.Id == id);
        if (recordatorio is null) return null;

        cambio(recordatorio);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            var actual = await LeerAsync(id);
            if (actual is null) return null;
            if (yaHecho?.Invoke(actual) == true) return ADto(actual);
            throw new ReglaNegocioException(MensajeCambiadoEnOtraPestana);
        }
        return ADto(recordatorio);
    }

    private async Task<Recordatorio?> LeerAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Recordatorios.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
    }

    private async Task<bool> ExisteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Recordatorios.AnyAsync(r => r.Id == id);
    }

    private sealed record Datos(TipoRecordatorio Tipo, string Titulo, DateTime FechaHora, string? Notas);

    private static Datos Validar(GuardarRecordatorioRequest d)
    {
        // La API ya exige con anotaciones lo obligatorio y los largos; esto cubre a quien llame al servicio directamente.
        if (d.Tipo is not { } tipo)
            throw new ValidacionException("Elige qué tipo de recordatorio es.", "tipo");
        ValidacionException.ExigirDefinido(tipo, "tipo");

        var titulo = d.Titulo?.Trim();
        if (string.IsNullOrEmpty(titulo))
            throw new ValidacionException("El título es obligatorio.", "titulo");
        if (titulo.Length > ReglasRecordatorio.LargoTitulo)
            throw new ValidacionException($"El título no puede tener más de {ReglasRecordatorio.LargoTitulo} caracteres.", "titulo");

        if (d.Dia is not { } dia)
            throw new ValidacionException("El día es obligatorio.", "dia");
        ReglasRecordatorio.ValidarDia(dia);
        var hora = ReglasRecordatorio.LeerHora(tipo, d.Hora);

        var notas = string.IsNullOrWhiteSpace(d.Notas) ? null : d.Notas.Trim();
        if (notas?.Length > ReglasRecordatorio.LargoNotas)
            throw new ValidacionException($"Las notas no pueden tener más de {ReglasRecordatorio.LargoNotas} caracteres.", "notas");

        return new Datos(tipo, titulo, ReglasRecordatorio.FechaHoraUtc(dia, hora), notas);
    }

    private static void Copiar(Datos d, Recordatorio r)
    {
        r.Tipo = d.Tipo;
        r.Titulo = d.Titulo;
        r.FechaHora = d.FechaHora;
        r.Notas = d.Notas;
    }

    private static DateTime Medianoche(DateOnly dia) => Fechas.MedianocheMadridEnUtc(dia.ToDateTime(TimeOnly.MinValue));

    private static RecordatorioAgendaDto AAgenda(Recordatorio r) => new(
        r.Id, r.CandidaturaId, r.Tipo, r.Titulo, r.FechaHora, r.Hecho, r.FechaHecho, r.Notas,
        r.Candidatura is { } c ? new CandidaturaBreveDto(c.Id, c.Empresa.Nombre, c.Puesto, c.Estado, c.Enlace) : null);
}
