using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Services.Agentes;

/// <summary>
/// Lanza las ejecuciones de una en una y por orden (la más antigua en cola primero). Para cada una: vuelve a mirar el
/// tope, coge el bloqueo compartido (o espera, como mucho una hora), calcula los hashes del registro y del perfil,
/// lanza Claude, vuelve a calcular los hashes y deja la ejecución en Ok, Error o Cancelada. Al arrancar, nada de lo
/// que se quedó a medias se relanza solo (<see cref="RecuperarAsync"/>). Si otra API ya procesa la cola de la misma
/// base de datos (<see cref="ExclusionBaseDatos"/>), esta no recupera, no mata ni lanza nada: lo avisa en el log y lo
/// vuelve a intentar en cada sondeo.
/// </summary>
public sealed class ProcesadorEjecuciones : BackgroundService
{
    private const string ClaveLog = "ejecucion-";

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _reloj;
    private readonly RutasCurriculum _rutas;
    private readonly OpcionesEjecutor _opciones;
    private readonly ColaEjecuciones _cola;
    private readonly BloqueoCompartido _bloqueo;
    private readonly IEjecutorClaude _ejecutor;
    private readonly ILectorCurriculum _lector;
    private readonly IServiceScopeFactory _ambitos;
    private readonly ILogger<ProcesadorEjecuciones> _log;
    private readonly GuardiaCola _guardia;
    private bool _avisadoSinExclusion;
    private bool _recuperado;

    public ProcesadorEjecuciones(IDbContextFactory<AppDbContext> factory, IClock reloj, RutasCurriculum rutas, OpcionesEjecutor opciones,
        ColaEjecuciones cola, BloqueoCompartido bloqueo, IEjecutorClaude ejecutor, ILectorCurriculum lector, IServiceScopeFactory ambitos,
        GuardiaCola guardia, ILogger<ProcesadorEjecuciones> log)
    {
        _guardia = guardia;
        _factory = factory;
        _reloj = reloj;
        _rutas = rutas;
        _opciones = opciones;
        _cola = cola;
        _bloqueo = bloqueo;
        _ejecutor = ejecutor;
        _lector = lector;
        _ambitos = ambitos;
        _log = log;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _recuperado = await RecuperarAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Sin la exclusión, otra API lleva la cola; si esa se cierra, esta la coge (y recupera) en el siguiente sondeo.
                if (!_recuperado && (_recuperado = await RecuperarAsync(stoppingToken)))
                    _log.LogInformation("Esta API pasa a llevar la cola de agentes.");
                while (await ProcesarSiguienteAsync(stoppingToken)) { }
                await _cola.EsperarAvisoAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                // La BD no responde o similar: se vuelve a intentar en el siguiente sondeo.
                _log.LogError(error, "Fallo en la cola de agentes.");
                await Task.Delay(_opciones.IntervaloSondeo, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// Al arrancar la API: lo que estaba en curso queda en Error («Se interrumpió…») y lo que estaba en cola, Cancelada;
    /// si sigue vivo el claude.exe de una ejecución (mismo pid, hora de inicio y nombre), se mata con su árbol; se borra
    /// el bloqueo propio caducado y los logs y carpetas de ejecución de más de 60 días. Solo si esta API tiene la
    /// exclusión de la base de datos; si no, no toca nada y devuelve false.
    /// </summary>
    public async Task<bool> RecuperarAsync(CancellationToken cancelacion = default)
    {
        if (!_guardia.Intentar())
        {
            if (!_avisadoSinExclusion) _log.LogWarning("Esta API no lleva la cola de agentes: {Motivo}", _guardia.Motivo);
            _avisadoSinExclusion = true;
            return false;
        }
        var ahora = _reloj.AhoraUtc;
        await using (var db = await _factory.CreateDbContextAsync(cancelacion))
        {
            var pendientes = await db.EjecucionesAgente
                .Where(e => e.Estado == EstadoEjecucion.EnCola || e.Estado == EstadoEjecucion.EnCurso)
                .ToListAsync(cancelacion);
            foreach (var ejecucion in pendientes)
            {
                if (ejecucion is { Pid: { } pid, InicioProceso: { } inicio }) MatarHuerfano(ejecucion.Id, pid, inicio);
                ejecucion.Interrumpir(ahora);
            }
            await db.SaveChangesAsync(cancelacion);
            if (pendientes.Count > 0) _log.LogInformation("Al arrancar se han cerrado {Numero} ejecuciones a medias.", pendientes.Count);
        }

        try
        {
            _bloqueo.LimpiarPropioCaducado(ahora);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(error, "No se ha podido revisar el bloqueo compartido al arrancar.");
        }
        LimpiarAntiguos(ahora);
        return true;
    }

    /// <summary>Procesa la ejecución en cola más antigua. Devuelve false si no había ninguna o si la cola la lleva otra API.</summary>
    public async Task<bool> ProcesarSiguienteAsync(CancellationToken cancelacion)
    {
        if (!_guardia.LlevaLaCola) return false;
        int? id;
        await using (var db = await _factory.CreateDbContextAsync(cancelacion))
            id = await db.EjecucionesAgente.Where(e => e.Estado == EstadoEjecucion.EnCola)
                .OrderBy(e => e.Id).Select(e => (int?)e.Id).FirstOrDefaultAsync(cancelacion);
        if (id is not { } siguiente) return false;

        try
        {
            await ProcesarAsync(siguiente, cancelacion);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            await CerrarAsync(siguiente, e => e.Interrumpir(_reloj.AhoraUtc));
            throw;
        }
        catch (Exception error)
        {
            _log.LogError(error, "Fallo inesperado en la ejecución {Id}.", siguiente);
            await CerrarAsync(siguiente, e => e.Fallar(_reloj.AhoraUtc, MensajesEjecucion.Inesperado));
        }
        return true;
    }

    private async Task ProcesarAsync(int id, CancellationToken parada)
    {
        using var ambito = _ambitos.CreateScope();
        var ejecucion = await LeerAsync(id, parada);
        if (ejecucion?.Estado != EstadoEjecucion.EnCola) return;

        var manejador = ColaEjecuciones.Manejador(ambito.ServiceProvider, ejecucion.Tarea);
        if (manejador is null)
        {
            await CambiarAsync(id, e => e.Fallar(_reloj.AhoraUtc, MensajesEjecucion.SinManejador));
            return;
        }
        if (!_rutas.HayCarpeta)
        {
            await CambiarAsync(id, e => e.Fallar(_reloj.AhoraUtc, RutasCurriculum.MensajeSinCarpeta));
            return;
        }
        if (!await CabeAsync(ejecucion, parada))
        {
            await CambiarAsync(id, e => e.Fallar(_reloj.AhoraUtc, MensajesEjecucion.TopeDiario(_opciones.TopeGastoDiarioUsd)));
            return;
        }

        var definicion = manejador.Definicion;
        using var bloqueo = await CogerBloqueoAsync(ejecucion, definicion, parada);
        if (bloqueo is null) return;

        using var corte = CancellationTokenSource.CreateLinkedTokenSource(parada);
        var hashRegistro = await HashAsync(ArchivoVigilado.Registro);
        var hashPerfil = await HashAsync(ArchivoVigilado.Perfil);
        var contexto = Contexto(ejecucion, definicion);
        var empezada = await EmpezarAsync(id, corte, e =>
        {
            e.Empezar(_reloj.AhoraUtc, hashRegistro, hashPerfil);
            e.RutaLog = Path.GetFileName(contexto.RutaLog);
        });
        if (!empezada) return;

        try
        {
            PeticionClaude peticion;
            try
            {
                peticion = await manejador.ConstruirAsync(ejecucion, contexto, corte.Token);
            }
            catch (ReglaNegocioException error)
            {
                await CambiarAsync(id, e => e.Fallar(_reloj.AhoraUtc, error.Message, null, hashRegistro, hashPerfil));
                return;
            }
            Task? anotar = null;
            // «Cancelar» desde otra copia de la API solo marca la BD: se vigila mientras corre.
            using var finVigilancia = new CancellationTokenSource();
            var vigilancia = VigilarCancelacionAsync(id, corte, finVigilancia.Token);
            RespuestaClaude respuesta;
            try
            {
                respuesta = await _ejecutor.EjecutarAsync(peticion,
                    (pid, inicio) => anotar = CambiarAsync(id, e => e.AnotarProceso(pid, inicio)), corte.Token);
            }
            finally
            {
                await finVigilancia.CancelAsync();
                await vigilancia;
            }
            if (anotar is not null) await anotar;

            var registroDespues = await HashAsync(ArchivoVigilado.Registro);
            var perfilDespues = await HashAsync(ArchivoVigilado.Perfil);
            if (parada.IsCancellationRequested)
            {
                await CambiarAsync(id, e => e.Interrumpir(_reloj.AhoraUtc));
                return;
            }

            if (respuesta.Fallo == FalloClaude.Cancelado)
            {
                await CambiarAsync(id, e => e.ConfirmarCancelacion(_reloj.AhoraUtc, respuesta.CosteUsd, registroDespues, perfilDespues));
                return;
            }
            if (!respuesta.Correcta)
            {
                _log.LogInformation("La ejecución {Id} no ha salido: {Fallo} ({Detalle}).", id, respuesta.Fallo, respuesta.Detalle);
                var mensaje = MensajesEjecucion.De(respuesta.Fallo ?? FalloClaude.ErrorDeClaude, definicion.TiempoMaximo, definicion.PresupuestoUsd);
                await CambiarAsync(id, e => e.Fallar(_reloj.AhoraUtc, mensaje, respuesta.CosteUsd, registroDespues, perfilDespues));
                return;
            }

            ResultadoTarea resultado;
            try
            {
                resultado = await manejador.AplicarAsync(ejecucion, respuesta, CancellationToken.None);
            }
            catch (ReglaNegocioException error)
            {
                await CambiarAsync(id, e => e.Fallar(_reloj.AhoraUtc, error.Message, respuesta.CosteUsd, registroDespues, perfilDespues));
                return;
            }
            await CambiarAsync(id, e => e.Terminar(_reloj.AhoraUtc, respuesta.CosteUsd, respuesta.Turnos, respuesta.SesionId,
                resultado.Resumen, resultado.ResultadoJson, registroDespues, perfilDespues));
        }
        catch (OperationCanceledException) when (corte.IsCancellationRequested && !parada.IsCancellationRequested)
        {
            // Cancelada justo al empezar (mientras se montaba la petición o se lanzaba el proceso): no es un fallo.
            var registroDespues = await HashAsync(ArchivoVigilado.Registro);
            var perfilDespues = await HashAsync(ArchivoVigilado.Perfil);
            await CambiarAsync(id, e =>
            {
                if (e.EsTerminal) return false;
                if (e.CancelacionPedida) e.ConfirmarCancelacion(_reloj.AhoraUtc, null, registroDespues, perfilDespues);
                else e.Fallar(_reloj.AhoraUtc, MensajesEjecucion.Inesperado, null, registroDespues, perfilDespues);
                return true;
            });
        }
        finally
        {
            _cola.Quitar(id);
        }
    }

    /// <summary>
    /// Coge el bloqueo compartido o espera (en cola, con <see cref="MotivoEspera.BloqueoOcupado"/>) mientras lo tenga
    /// otro. Null si la ejecución se ha cancelado o ha fallado mientras tanto.
    /// </summary>
    private async Task<ResultadoBloqueo?> CogerBloqueoAsync(EjecucionAgente ejecucion, DefinicionTarea definicion, CancellationToken parada)
    {
        var desde = _reloj.AhoraUtc;
        while (true)
        {
            var contenido = BloqueoEjecucion.DeLaApp(ejecucion.Tarea, ejecucion.Id, Environment.ProcessId, _reloj.AhoraUtc, definicion.TiempoMaximo);
            var intento = _bloqueo.Intentar(contenido, _reloj.AhoraUtc);
            if (intento.Cogido) return intento;

            var quien = intento.OcupadoPor!;
            if (_reloj.AhoraUtc - desde >= _opciones.EsperaMaximaBloqueo)
            {
                await CambiarAsync(ejecucion.Id, e =>
                {
                    if (e.Estado == EstadoEjecucion.EnCola) e.Fallar(_reloj.AhoraUtc, MensajesEjecucion.BloqueoDemasiadoTiempo(quien));
                });
                return null;
            }
            var sigue = await CambiarAsync(ejecucion.Id, e =>
            {
                if (e.Estado != EstadoEjecucion.EnCola) return false;
                if (e.Espera != MotivoEspera.BloqueoOcupado || e.QuienBloquea != quien) e.Esperar(MotivoEspera.BloqueoOcupado, quien);
                return true;
            });
            if (!sigue) return null;
            await Task.Delay(_opciones.IntervaloSondeo, parada);
        }
    }

    private async Task<bool> CabeAsync(EjecucionAgente ejecucion, CancellationToken cancelacion)
    {
        await using var db = await _factory.CreateDbContextAsync(cancelacion);
        var gasto = await ColaEjecuciones.LeerGastoAsync(db, _reloj.AhoraUtc, cancelacion);
        // Ella misma está en cola: su presupuesto no se cuenta dos veces.
        return ReglasGasto.CabeEnElTope(gasto.Dia.Gastado, gasto.Reservado - ejecucion.PresupuestoUsd, ejecucion.PresupuestoUsd,
            _opciones.TopeGastoDiarioUsd);
    }

    private ContextoTarea Contexto(EjecucionAgente ejecucion, DefinicionTarea definicion)
    {
        var id = ejecucion.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var carpeta = definicion.Carpeta == CarpetaTrabajo.Curriculum ? _rutas.Carpeta! : Path.Combine(_opciones.CarpetaEjecuciones, id);
        Directory.CreateDirectory(carpeta);
        Directory.CreateDirectory(_opciones.CarpetaLogs);
        // Con la fecha de alta: si la BD se vuelve a crear y los ids se repiten, no se mezclan logs de ejecuciones distintas.
        var log = $"{ClaveLog}{id}-{ejecucion.FechaAlta:yyyyMMdd-HHmmss}.log";
        return new ContextoTarea(carpeta, Path.Combine(_opciones.CarpetaLogs, log), _opciones.CarpetaDatos);
    }

    /// <summary>Mientras corre, mira cada sondeo si alguien ha pedido cancelarla en la BD y, si es así, corta.</summary>
    private async Task VigilarCancelacionAsync(int id, CancellationTokenSource corte, CancellationToken fin)
    {
        try
        {
            while (!fin.IsCancellationRequested && !corte.IsCancellationRequested)
            {
                await Task.Delay(_opciones.IntervaloSondeo, fin);
                try
                {
                    await using var db = await _factory.CreateDbContextAsync(fin);
                    if (await db.EjecucionesAgente.AnyAsync(e => e.Id == id && e.CancelacionPedida, fin))
                    {
                        await corte.CancelAsync();
                        return;
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Un fallo puntual (BD ocupada, por ejemplo) no apaga la vigilancia: se mira en el siguiente sondeo.
                    _log.LogWarning(error, "No se ha podido mirar si hay que cancelar la ejecución {Id}; se reintenta.", id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ha acabado la ejecución.
        }
    }

    private async Task<string?> HashAsync(ArchivoVigilado archivo)
    {
        try
        {
            return await _lector.LeerHashAsync(archivo, CancellationToken.None);
        }
        catch (ReglaNegocioException)
        {
            // Bloqueado justo ahora: sin hash no hay aviso, pero tampoco un aviso falso.
            return null;
        }
    }

    private async Task<EjecucionAgente?> LeerAsync(int id, CancellationToken cancelacion)
    {
        await using var db = await _factory.CreateDbContextAsync(cancelacion);
        return await db.EjecucionesAgente.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancelacion);
    }

    private Task CambiarAsync(int id, Action<EjecucionAgente> cambio) =>
        CambiarAsync(id, e => { cambio(e); return true; });

    /// <summary>Lee la ejecución de nuevo, la cambia y la guarda, dentro del cerrojo de la cola. Sin parada: lo empezado se apunta.</summary>
    private async Task<bool> CambiarAsync(int id, Func<EjecucionAgente, bool> cambio)
    {
        await _cola.Cerrojo.WaitAsync(CancellationToken.None);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(CancellationToken.None);
            var ejecucion = await db.EjecucionesAgente.FirstOrDefaultAsync(e => e.Id == id, CancellationToken.None);
            if (ejecucion is null || !cambio(ejecucion)) return false;
            await db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        finally
        {
            _cola.Cerrojo.Release();
        }
    }

    /// <summary>
    /// Pasa la ejecución de EnCola a EnCurso solo si nadie la ha tocado entre medias, también desde otra API sobre
    /// la misma BD (el cerrojo de la cola solo vale dentro de este proceso). La transición se reclama con un
    /// UPDATE condicionado dentro de una transacción: si otra API la ha cancelado justo antes, no toca ninguna fila
    /// y no se lanza nada. El corte se apunta dentro del cerrojo y antes de soltarlo: «Cancelar» de esta API
    /// siempre lo encuentra.
    /// </summary>
    private async Task<bool> EmpezarAsync(int id, CancellationTokenSource corte, Action<EjecucionAgente> empezar)
    {
        await _cola.Cerrojo.WaitAsync(CancellationToken.None);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(CancellationToken.None);
            var ejecucion = await db.EjecucionesAgente.FirstOrDefaultAsync(e => e.Id == id, CancellationToken.None);
            if (ejecucion is null || ejecucion.Estado != EstadoEjecucion.EnCola || ejecucion.CancelacionPedida) return false;
            empezar(ejecucion);

            await using var transaccion = await db.Database.BeginTransactionAsync(CancellationToken.None);
            var reclamadas = await db.EjecucionesAgente
                .Where(e => e.Id == id && e.Estado == EstadoEjecucion.EnCola && !e.CancelacionPedida)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, EstadoEjecucion.EnCurso), CancellationToken.None);
            if (reclamadas == 0) return false;
            await db.SaveChangesAsync(CancellationToken.None);
            await transaccion.CommitAsync(CancellationToken.None);
            _cola.Registrar(id, corte);
            return true;
        }
        finally
        {
            _cola.Cerrojo.Release();
        }
    }

    /// <summary>Tras un fallo inesperado: si la ejecución no ha terminado, la cierra. Nunca lanza.</summary>
    private async Task CerrarAsync(int id, Action<EjecucionAgente> cierre)
    {
        try
        {
            await CambiarAsync(id, e =>
            {
                if (e.EsTerminal) return false;
                cierre(e);
                return true;
            });
        }
        catch (Exception error)
        {
            _log.LogError(error, "No se ha podido cerrar la ejecución {Id}.", id);
        }
    }

    private void MatarHuerfano(int id, int pid, DateTime inicio)
    {
        try
        {
            using var proceso = Process.GetProcessById(pid);
            var mismo = proceso.ProcessName.Equals("claude", StringComparison.OrdinalIgnoreCase)
                && Math.Abs((proceso.StartTime.ToUniversalTime() - inicio).TotalSeconds) < 2;
            if (!mismo) return;
            _log.LogWarning("Claude de la ejecución {Id} (pid {Pid}) seguía vivo al arrancar: se mata.", id, pid);
            proceso.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Ya no existe, o no se deja: nada que hacer.
        }
    }

    private void LimpiarAntiguos(DateTime ahoraUtc)
    {
        var limite = ahoraUtc - _opciones.Retencion;
        try
        {
            if (Directory.Exists(_opciones.CarpetaLogs))
                foreach (var log in new DirectoryInfo(_opciones.CarpetaLogs).EnumerateFiles($"{ClaveLog}*.log"))
                    if (log.LastWriteTimeUtc < limite) log.Delete();
            if (Directory.Exists(_opciones.CarpetaEjecuciones))
                foreach (var carpeta in new DirectoryInfo(_opciones.CarpetaEjecuciones).EnumerateDirectories())
                    if (carpeta.LastWriteTimeUtc < limite && (carpeta.Attributes & FileAttributes.ReparsePoint) == 0) carpeta.Delete(recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(error, "No se han podido borrar los logs antiguos de los agentes.");
        }
    }
}
