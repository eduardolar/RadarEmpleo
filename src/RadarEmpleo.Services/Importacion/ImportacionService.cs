using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Services.Importacion;

/// <summary>
/// Importación del registro: vista previa (no escribe nada) y aplicar. El registro nunca se escribe.
/// Aplicar no se fía del cliente: vuelve a leer el registro, exige que sea el de la vista previa (por su hash),
/// rehace el lote y lo compara otra vez, porque desde la vista previa han podido cambiar las candidaturas. Solo
/// actúa sobre las claves pedidas. No borra nada ni toca datos o notas existentes.
/// </summary>
public class ImportacionService
{
    /// <summary>Otra aplicación ha guardado antes alguna de las mismas filas nuevas.</summary>
    public const string MensajeYaImportadas = "Estas candidaturas ya se han importado desde otra pestaña. Vuelvo a leer el registro.";

    /// <summary>Otra petición ha cambiado entre medias alguna de las candidaturas que cambian de estado.</summary>
    public const string MensajeCambiadasEnOtraPestana = "Otra pestaña acaba de cambiar estas candidaturas. Vuelvo a leer el registro.";

    /// <summary>El registro de la vista previa ya no es el que hay en el disco.</summary>
    public const string MensajeRegistroCambiado = "El registro ha cambiado desde que lo leíste. Vuelvo a leerlo.";

    public const string MensajeSinTabla = "No se encuentra la tabla en el registro: tiene que tener las columnas Fecha, Empresa, Puesto y Estado.";

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

    /// <summary>
    /// Lee la tabla del registro y la compara con lo que ya hay en la app. El hash sale de los mismos bytes que el texto.
    /// </summary>
    /// <exception cref="ReglaNegocioException">Falta la carpeta, el registro o la tabla.</exception>
    public async Task<VistaPreviaImportacionDto> PrevisualizarTablaAsync()
    {
        var leido = await _lector.LeerRegistroConFechaAsync();
        var analisis = AnalizarTabla(leido.Texto);

        await using var db = await _factory.CreateDbContextAsync();
        var filas = ReglasImportacion.Comparar(analisis.Lote.Filas, await LeerExistentesAsync(db));

        return new VistaPreviaImportacionDto(
            OrigenLote.Tabla,
            leido.Hash,
            null,
            ReglasImportacion.Resumir(filas, analisis.SinLeer.Count, analisis.EnviosEnTexto),
            filas,
            analisis.SinLeer,
            []);
    }

    /// <summary>
    /// Rehace el lote con el registro actual, que tiene que ser el de la vista previa
    /// (<see cref="AplicarImportacionRequest.HashRegistro"/>; si no, 409 <see cref="MensajeRegistroCambiado"/>): el texto
    /// y el hash salen de la misma lectura. Crea las nuevas y avanza las que cambian de estado entre las
    /// <see cref="AplicarImportacionRequest.Claves"/>, en un solo guardado: o entra todo o no entra nada. Si otra
    /// aplicación se adelanta (salta el índice único de la clave, o el control de concurrencia de una candidatura que
    /// cambia de estado), no se guarda nada y se lanza <see cref="ReglaNegocioException"/> (409). Si solo choca una
    /// empresa nueva que otra petición acaba de crear, se reutiliza esa y se reintenta. Los conflictos no dan error: se
    /// saltan y se cuentan. El radar se exporta después de guardar: si falla, lo importado sigue siendo válido.
    /// </summary>
    /// <exception cref="ValidacionException">Origen, ejecución o claves no válidos.</exception>
    /// <exception cref="ReglaNegocioException">El registro ha cambiado o ya no tiene la tabla, o otra aplicación ha
    /// guardado antes alguna de las filas.</exception>
    public async Task<ResultadoAplicacionDto> AplicarAsync(AplicarImportacionRequest peticion)
    {
        var origen = peticion.Origen ?? throw new ValidacionException("Falta el origen.", "origen");
        ValidacionException.ExigirDefinido(origen, "origen");
        if (peticion.EjecucionId is not null)
            throw new ValidacionException("Con la tabla no va ninguna ejecución.", "ejecucionId");

        var leido = await _lector.LeerRegistroConFechaAsync();
        if (!string.Equals(peticion.HashRegistro, leido.Hash, StringComparison.Ordinal))
            throw new ReglaNegocioException(MensajeRegistroCambiado);

        var filas = ValidarLote(AnalizarTabla(leido.Texto).Lote.Filas);
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

    /// <summary>El análisis del registro, o 409 si no tiene la tabla.</summary>
    private static AnalisisRegistro AnalizarTabla(string registro)
    {
        var analisis = AnalizadorRegistro.Analizar(registro);
        return analisis.TablaEncontrada ? analisis : throw new ReglaNegocioException(MensajeSinTabla);
    }

    /// <summary>
    /// Defensa sobre el lote que monta el servidor: el analizador y el combinador ya lo dejan así, y si algo se escapa es
    /// un error nuestro que da 400 antes de guardar nada. La fecha va acotada porque una que se salga del rango de
    /// <see cref="DateTime"/> rompería el cálculo del seguimiento.
    /// </summary>
    private static IReadOnlyList<FilaLote> ValidarLote(IReadOnlyList<FilaLote> filas)
    {
        if (filas.Count > LimitesLote.Filas)
            throw new ValidacionException($"El lote no puede tener más de {LimitesLote.Filas} filas.", "filas");

        foreach (var fila in filas)
        {
            if (fila.Fecha.Year is < CombinadorLotes.AnioMinimo or > CombinadorLotes.AnioMaximo)
                throw new ValidacionException($"La fecha de la línea {fila.Linea} no es válida.", "fecha");
            ValidacionException.ExigirDefinido(fila.Modalidad, "modalidad");
            ValidacionException.ExigirDefinido(fila.Estado, "estado");
            if (fila.MotivoDescarte is { } motivo)
                ValidacionException.ExigirDefinido(motivo, "motivoDescarte");
            // Sin esto, el dominio lo rechazaría al guardar como regla de negocio (409) y es un dato que falta (400).
            if (fila.Estado == EstadoCandidatura.Descartada && fila.MotivoDescarte is null)
                throw new ValidacionException($"La línea {fila.Linea} está descartada y le falta el motivo.", "motivoDescarte");
        }
        return filas;
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
        candidatura.SeccionOrigen = fila.Seccion ?? AnalizadorRegistro.SeccionTabla;
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
        // Si una fila trae en la nota el mismo texto del motivo, en la ficha saldría dos veces.
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
