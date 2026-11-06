using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Services.Importacion;

/// <summary>
/// Importación del registro: vista previa (no escribe nada) y aplicar. El registro nunca se escribe.
/// Aplicar recalcula todo con el lote que llega, porque desde la vista previa han podido cambiar las
/// candidaturas, y solo actúa sobre las claves pedidas. No borra nada ni toca datos o notas existentes.
/// </summary>
public class ImportacionService
{
    /// <summary>Otra aplicación ha guardado antes alguna de las mismas filas nuevas.</summary>
    public const string MensajeYaImportadas = "Estas candidaturas ya se han importado desde otra pestaña. Vuelvo a leer el registro.";

    /// <summary>Otra petición ha cambiado entre medias alguna de las candidaturas que cambian de estado.</summary>
    public const string MensajeCambiadasEnOtraPestana = "Otra pestaña acaba de cambiar estas candidaturas. Vuelvo a leer el registro.";

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILectorCurriculum _lector;
    private readonly IExportadorRadar _exportador;
    private readonly IClock _reloj;

    public ImportacionService(IDbContextFactory<AppDbContext> factory, ILectorCurriculum lector, IExportadorRadar exportador, IClock reloj)
    {
        _factory = factory;
        _lector = lector;
        _exportador = exportador;
        _reloj = reloj;
    }

    /// <summary>Lee la tabla del registro y la compara con lo que ya hay en la app.</summary>
    /// <exception cref="ReglaNegocioException">Falta la carpeta, el registro o la tabla.</exception>
    public async Task<VistaPreviaImportacionDto> PrevisualizarTablaAsync()
    {
        var analisis = AnalizadorRegistro.Analizar(await _lector.LeerRegistroAsync());
        if (!analisis.TablaEncontrada)
            throw new ReglaNegocioException("No se encuentra la tabla en el registro: tiene que tener las columnas Fecha, Empresa, Puesto y Estado.");

        await using var db = await _factory.CreateDbContextAsync();
        var filas = ReglasImportacion.Comparar(analisis.Lote.Filas, await LeerExistentesAsync(db));

        return new VistaPreviaImportacionDto(
            analisis.Lote,
            ReglasImportacion.Resumir(filas, analisis.SinLeer.Count, analisis.EnviosEnTexto),
            filas,
            analisis.SinLeer);
    }

    /// <summary>
    /// Crea las nuevas y avanza las que cambian de estado entre las <see cref="AplicarLoteRequest.Claves"/>,
    /// en un solo guardado: o entra todo o no entra nada. Si otra aplicación se adelanta (salta el índice
    /// único de la clave, o el control de concurrencia de una candidatura que cambia de estado), no se
    /// guarda nada y se lanza <see cref="ReglaNegocioException"/> (409). Si solo choca una empresa nueva
    /// que otra petición acaba de crear, se reutiliza esa y se reintenta. Los conflictos no dan error: se
    /// saltan y se cuentan. El radar se exporta después de guardar: si falla, lo importado sigue siendo válido.
    /// </summary>
    /// <exception cref="ValidacionException">Lote o claves no válidos.</exception>
    /// <exception cref="ReglaNegocioException">Otra aplicación ha guardado antes alguna de las filas.</exception>
    public async Task<ResultadoAplicacionDto> AplicarAsync(AplicarLoteRequest peticion)
    {
        var filas = ValidarLote(peticion.Lote);
        await using var db = await _factory.CreateDbContextAsync();

        var comparadas = ReglasImportacion.Comparar(filas, await LeerExistentesAsync(db));
        var claves = peticion.Claves.ToHashSet(StringComparer.Ordinal);
        if (claves.Except(comparadas.Select(f => f.Clave), StringComparer.Ordinal).Any())
            throw new ValidacionException("Hay claves que no pertenecen al lote.", "claves");

        var ahora = _reloj.AhoraUtc;
        int creadas = 0, avanzadas = 0;
        foreach (var (fila, comparada) in filas.Zip(comparadas))
        {
            if (!claves.Contains(comparada.Clave)) continue;

            if (comparada.Resultado == ResultadoFila.Nueva)
            {
                await CrearAsync(db, fila, comparada.Clave, ahora);
                creadas++;
            }
            else if (comparada.Resultado == ResultadoFila.AvanzaEstado)
            {
                await AvanzarAsync(db, fila, comparada, ahora);
                avanzadas++;
            }
        }

        await GuardarAsync(db);
        var radarActualizado = await _exportador.ExportarAsync();

        var paraRevisar = comparadas.Count(f => f.Resultado == ResultadoFila.Conflicto);
        var sinCambios = comparadas.Count - creadas - avanzadas - paraRevisar;
        return new ResultadoAplicacionDto(creadas, avanzadas, sinCambios, paraRevisar, radarActualizado);
    }

    // ---------------------------------------------------------------- auxiliares

    /// <summary>
    /// Lo que no comprueban las anotaciones de los DTO. La fecha va acotada porque un lote escrito a
    /// mano podría traer una que se salga del rango de <see cref="DateTime"/> al calcular el seguimiento.
    /// </summary>
    private static IReadOnlyList<FilaLote> ValidarLote(LoteImportacion lote)
    {
        if (lote.Version != LimitesLote.VersionActual)
            throw new ValidacionException($"La versión del lote tiene que ser {LimitesLote.VersionActual}.", "version");
        if (lote.Filas.Count > LimitesLote.Filas)
            throw new ValidacionException($"El lote no puede tener más de {LimitesLote.Filas} filas.", "filas");
        ValidacionException.ExigirDefinido(lote.Origen, "origen");

        foreach (var fila in lote.Filas)
        {
            if (fila is null)
                throw new ValidacionException("El lote tiene filas vacías.", "filas");
            if (fila.Fecha.Year is < 2000 or > 2100)
                throw new ValidacionException($"La fecha de la línea {fila.Linea} no es válida.", "fecha");
            ValidacionException.ExigirDefinido(fila.Modalidad, "modalidad");
            ValidacionException.ExigirDefinido(fila.Estado, "estado");
            if (fila.MotivoDescarte is { } motivo)
                ValidacionException.ExigirDefinido(motivo, "motivoDescarte");
            // Sin esto, el dominio lo rechazaría al guardar como regla de negocio (409) y es un dato que falta (400).
            if (fila.Estado == EstadoCandidatura.Descartada && fila.MotivoDescarte is null)
                throw new ValidacionException($"La línea {fila.Linea} está descartada y le falta el motivo.", "motivoDescarte");
        }
        return lote.Filas;
    }

    /// <summary>Traduce los choques con otra aplicación a un 409 que dice qué ha pasado.</summary>
    private static async Task GuardarAsync(AppDbContext db)
    {
        try
        {
            await EmpresasService.GuardarReutilizandoEmpresasAsync(db);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ReglaNegocioException(MensajeCambiadasEnOtraPestana);
        }
        catch (DbUpdateException error) when (EsClaveDuplicada(error))
        {
            throw new ReglaNegocioException(MensajeYaImportadas);
        }
    }

    private static bool EsClaveDuplicada(DbUpdateException error) =>
        ErroresBaseDatos.EsViolacionUnica(error, out var detalle)
        && detalle.Contains("Candidaturas.ClaveImportacion", StringComparison.Ordinal);

    private static Task<List<CandidaturaExistente>> LeerExistentesAsync(AppDbContext db) =>
        db.Candidaturas.AsNoTracking()
            .Select(c => new CandidaturaExistente(c.Id, c.ClaveImportacion, c.Empresa.NombreNormalizado, c.Puesto, c.Estado))
            .ToListAsync();

    /// <summary>
    /// Todo lleva la fecha del registro y no la de hoy, para que el historial, el envío y los días en el
    /// estado sean los reales. El seguimiento solo se programa si aún no ha vencido.
    /// </summary>
    private static async Task CrearAsync(AppDbContext db, FilaLote fila, string clave, DateTime ahora)
    {
        var fecha = Fechas.MedianocheMadridEnUtc(fila.Fecha.ToDateTime(TimeOnly.MinValue));
        var empresa = await EmpresasService.ObtenerOCrearAsync(db, fila.Empresa);

        var candidatura = Candidatura.Nueva(empresa, fila.Puesto, fecha, Origen.Registro);
        candidatura.ClaveImportacion = clave;
        candidatura.SeccionOrigen = AnalizadorRegistro.SeccionTabla;
        candidatura.Enlace = ReglasImportacion.EnlaceParaGuardar(fila.Enlace);
        candidatura.Portal = fila.Portal;
        candidatura.Modalidad = fila.Modalidad;
        candidatura.ModalidadDetalle = fila.ModalidadDetalle;
        candidatura.Ubicacion = fila.Ubicacion;

        if (fila.Estado != EstadoCandidatura.Detectada)
        {
            candidatura.CambiarEstado(fila.Estado, fecha, Origen.Registro, fila.MotivoDescarte, fila.MotivoDescarteTexto,
                programarSeguimiento: ReglasEstado.FechaSeguimiento(fecha) > ahora);
        }
        // Un lote hecho a mano puede traer en la nota el mismo texto del motivo: en la ficha saldría dos veces.
        if (fila.Nota is { } nota && nota != fila.MotivoDescarteTexto)
            candidatura.Notas.Add(new Nota { Texto = nota, Fecha = fecha, Origen = Origen.Registro });

        db.Candidaturas.Add(candidatura);
    }

    /// <summary>
    /// El cambio lleva la fecha de hoy: la del registro es la de la fila, no la de este cambio, y usarla
    /// colocaría el cambio antes que otros que ya están en el historial. Si entre la comparación y esta
    /// lectura otra petición ha cambiado el estado (o borrado la candidatura), lo comparado ya no vale.
    /// </summary>
    private static async Task AvanzarAsync(AppDbContext db, FilaLote fila, FilaVistaPreviaDto comparada, DateTime ahora)
    {
        var candidatura = await db.Candidaturas
            .Include(c => c.Empresa)
            .Include(c => c.Recordatorios)
            .FirstOrDefaultAsync(c => c.Id == comparada.CandidaturaId);
        if (candidatura is null || candidatura.Estado != comparada.EstadoActual)
            throw new ReglaNegocioException(MensajeCambiadasEnOtraPestana);

        candidatura.CambiarEstado(fila.Estado, ahora, Origen.Registro, fila.MotivoDescarte, fila.MotivoDescarteTexto, fila.Nota);
    }
}
