namespace RadarEmpleo.Domain;

/// <summary>
/// Tarea fija que la app puede lanzar con Claude. Las cuatro últimas son de la F8-F10 y todavía no tienen
/// plantilla: no se pueden encolar.
/// </summary>
public enum TareaAgente
{
    Rastreo,
    AnalizarEncaje,
    ExtraerRegistro,
    Redactar,
    Revisar,
    RellenarEnvio,
    ConfirmarEnvio
}

/// <summary>Estado de una <see cref="EjecucionAgente"/>. Ok, Error y Cancelada son terminales.</summary>
public enum EstadoEjecucion
{
    EnCola,
    EnCurso,
    Ok,
    Error,
    Cancelada
}

/// <summary>Por qué sigue en cola una ejecución: espera su turno o el bloqueo compartido está ocupado.</summary>
public enum MotivoEspera
{
    Turno,
    BloqueoOcupado
}

/// <summary>
/// Un lanzamiento de Claude desde la app. El estado SOLO cambia con sus métodos, que siguen esta máquina:
/// EnCola → EnCurso → Ok | Error | Cancelada, EnCola → Error | Cancelada. Desde un estado terminal no se sale
/// nunca (para repetir se crea otra ejecución). Cualquier otra transición lanza <see cref="ReglaNegocioException"/>.
/// Los hashes del registro y del perfil antes y después no son un estado: son un aviso que se enseña encima.
/// Todas las fechas en UTC.
/// </summary>
public class EjecucionAgente
{
    public const int LargoResumen = 500, LargoError = 1000, LargoHash = 64, LargoRutaLog = 300, LargoQuien = 100, LargoModelo = 50;
    public const int LargoParametros = 500, LargoSesion = 100;

    /// <summary>Error que queda en lo que estaba en curso cuando se cerró la API.</summary>
    public const string MensajeInterrumpida = "Se cortó porque la app se cerró mientras trabajaba.";

    /// <summary>Resumen de lo que estaba en cola cuando se cerró la API (no se relanza solo).</summary>
    public const string MensajeReiniciadaAntesDeEmpezar = "La API se reinició antes de empezar.";

    public int Id { get; set; }
    public TareaAgente Tarea { get; set; }
    /// <summary>Solo ids ya validados, en JSON: <c>{"candidaturaId":12}</c>.</summary>
    public string ParametrosJson { get; set; } = "{}";
    public EstadoEjecucion Estado { get; private set; }
    public MotivoEspera? Espera { get; private set; }
    /// <summary>
    /// Quién tiene el bloqueo mientras espera, para enseñarlo (<see cref="BloqueoEjecucion.NombreVisible"/>): «la tarea de
    /// los lunes», «tu sesión de /empleo en Claude Code», «otra ventana de RadarEmpleo» u «otro programa».
    /// </summary>
    public string? QuienBloquea { get; private set; }
    /// <summary>Cuándo se encoló.</summary>
    public DateTime FechaAlta { get; set; }
    /// <summary>Cuándo se lanzó Claude.</summary>
    public DateTime? Inicio { get; private set; }
    public DateTime? Fin { get; private set; }
    /// <summary>El <c>--max-budget-usd</c> de la tarea.</summary>
    public decimal PresupuestoUsd { get; set; }
    public int TiempoMaximoSegundos { get; set; }
    public string? Modelo { get; set; }
    /// <summary>El <c>total_cost_usd</c> de Claude; null si no se sabe (por ejemplo, si se cortó).</summary>
    public decimal? CosteUsd { get; private set; }
    public int? Turnos { get; private set; }
    /// <summary>Frase para enseñar.</summary>
    public string? Resumen { get; private set; }
    /// <summary>Mensaje en español para Eduardo (el detalle técnico va al log).</summary>
    public string? Error { get; private set; }
    /// <summary>Salida estructurada ya validada.</summary>
    public string? ResultadoJson { get; private set; }
    public string? SesionClaude { get; private set; }
    public int? Pid { get; private set; }
    /// <summary>Hora de inicio del proceso, para no matar al arrancar otro proceso que reutilice el pid.</summary>
    public DateTime? InicioProceso { get; private set; }
    /// <summary>Ruta del log, relativa a <c>%LOCALAPPDATA%\RadarEmpleo\logs</c>.</summary>
    public string? RutaLog { get; set; }
    public string? HashRegistroAntes { get; private set; }
    public string? HashRegistroDespues { get; private set; }
    public string? HashPerfilAntes { get; private set; }
    public string? HashPerfilDespues { get; private set; }
    public bool CancelacionPedida { get; private set; }
    /// <summary>Candidatura de la tarea (AnalizarEncaje). Queda en null si se borra.</summary>
    public int? CandidaturaId { get; set; }
    public Candidatura? Candidatura { get; set; }
    /// <summary>Preparación de la F8. Sin clave foránea todavía.</summary>
    public int? PreparacionId { get; set; }
    /// <summary>Envío de la F9-F10. Sin clave foránea todavía.</summary>
    public int? EnvioId { get; set; }
    /// <summary>Paso de la preparación (F8).</summary>
    public int? Paso { get; set; }

    public bool EsTerminal => Estado is EstadoEjecucion.Ok or EstadoEjecucion.Error or EstadoEjecucion.Cancelada;

    /// <summary>
    /// ¿Ha cambiado <c>ofertas-enviadas.md</c> mientras trabajaba? Si uno de los dos hashes no se pudo leer (null), no se
    /// sabe: false. Que el archivo no esté tiene su propia marca (<c>ausente</c>), así que borrarlo sí cuenta como cambio.
    /// </summary>
    public bool RegistroCambiado => Cambiado(HashRegistroAntes, HashRegistroDespues);

    /// <summary>¿Ha cambiado <c>perfil-busqueda.md</c> mientras trabajaba? Como <see cref="RegistroCambiado"/>.</summary>
    public bool PerfilCambiado => Cambiado(HashPerfilAntes, HashPerfilDespues);

    private static bool Cambiado(string? antes, string? despues) => antes is not null && despues is not null && antes != despues;

    /// <summary>
    /// Una ejecución nueva, en cola desde <paramref name="ahoraUtc"/>, sin motivo de espera: la cola le pone
    /// <see cref="MotivoEspera.Turno"/> si de verdad hay otra delante.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Presupuesto o tiempo máximo no positivos (son de código, no del usuario).</exception>
    public static EjecucionAgente Nueva(TareaAgente tarea, string parametrosJson, decimal presupuesto, int tiempoMaximo,
        string? modelo, DateTime ahoraUtc, int? candidaturaId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presupuesto);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tiempoMaximo);
        return new EjecucionAgente
        {
            Tarea = tarea,
            ParametrosJson = parametrosJson,
            Estado = EstadoEjecucion.EnCola,
            FechaAlta = ahoraUtc,
            PresupuestoUsd = presupuesto,
            TiempoMaximoSegundos = tiempoMaximo,
            Modelo = modelo,
            CandidaturaId = candidaturaId
        };
    }

    /// <summary>Sigue en cola, por su turno o porque el bloqueo compartido lo tiene <paramref name="quien"/>.</summary>
    public void Esperar(MotivoEspera motivo, string? quien)
    {
        Exigir(EstadoEjecucion.EnCola);
        Espera = motivo;
        QuienBloquea = motivo == MotivoEspera.BloqueoOcupado ? Recortar(quien, LargoQuien) : null;
    }

    /// <summary>EnCola → EnCurso, con los hashes de antes.</summary>
    public void Empezar(DateTime ahoraUtc, string? hashRegistro, string? hashPerfil)
    {
        Exigir(EstadoEjecucion.EnCola);
        Estado = EstadoEjecucion.EnCurso;
        Espera = null;
        QuienBloquea = null;
        Inicio = ahoraUtc;
        HashRegistroAntes = hashRegistro;
        HashPerfilAntes = hashPerfil;
    }

    /// <summary>Apunta el proceso de Claude en cuanto existe.</summary>
    public void AnotarProceso(int pid, DateTime inicioProceso)
    {
        Exigir(EstadoEjecucion.EnCurso);
        Pid = pid;
        InicioProceso = inicioProceso;
    }

    /// <summary>EnCurso → Ok.</summary>
    public void Terminar(DateTime ahoraUtc, decimal? coste, int? turnos, string? sesion, string resumen, string? resultadoJson,
        string? hashRegistro, string? hashPerfil)
    {
        Exigir(EstadoEjecucion.EnCurso);
        Estado = EstadoEjecucion.Ok;
        Fin = ahoraUtc;
        CosteUsd = coste;
        Turnos = turnos;
        SesionClaude = Recortar(sesion, LargoSesion);
        Resumen = Recortar(resumen, LargoResumen);
        ResultadoJson = resultadoJson;
        AnotarHashesDespues(hashRegistro, hashPerfil);
    }

    /// <summary>EnCola o EnCurso → Error, con el mensaje para Eduardo.</summary>
    public void Fallar(DateTime ahoraUtc, string error, decimal? coste = null, string? hashRegistro = null, string? hashPerfil = null)
    {
        if (Estado is not (EstadoEjecucion.EnCola or EstadoEjecucion.EnCurso)) throw NoSePuede();
        Estado = EstadoEjecucion.Error;
        Espera = null;
        Fin = ahoraUtc;
        CosteUsd = coste;
        Error = Recortar(error, LargoError);
        AnotarHashesDespues(hashRegistro, hashPerfil);
    }

    /// <summary>
    /// En cola se cancela al momento; en curso solo se marca, y queda Cancelada con
    /// <see cref="ConfirmarCancelacion"/> cuando el proceso ya ha muerto. Pedirla dos veces no hace nada.
    /// </summary>
    /// <exception cref="ReglaNegocioException">Ya ha terminado.</exception>
    public void PedirCancelacion(DateTime ahoraUtc)
    {
        switch (Estado)
        {
            case EstadoEjecucion.EnCola:
                Estado = EstadoEjecucion.Cancelada;
                Espera = null;
                QuienBloquea = null;
                Fin = ahoraUtc;
                break;
            case EstadoEjecucion.EnCurso:
                CancelacionPedida = true;
                break;
            default:
                throw new ReglaNegocioException("Ya ha terminado.");
        }
    }

    /// <summary>EnCurso con la cancelación pedida → Cancelada.</summary>
    public void ConfirmarCancelacion(DateTime ahoraUtc, decimal? coste, string? hashRegistro, string? hashPerfil)
    {
        if (Estado != EstadoEjecucion.EnCurso || !CancelacionPedida) throw NoSePuede();
        Estado = EstadoEjecucion.Cancelada;
        Fin = ahoraUtc;
        CosteUsd = coste;
        AnotarHashesDespues(hashRegistro, hashPerfil);
    }

    /// <summary>
    /// Al arrancar la API: lo que estaba en curso queda en Error y lo que estaba en cola, Cancelada (un reinicio
    /// no gasta sin que Eduardo lo vea).
    /// </summary>
    /// <param name="coste">El de la que estaba en curso: 0 si Claude no llegó a lanzarse; null si no se sabe (cuenta el presupuesto).</param>
    public void Interrumpir(DateTime ahoraUtc, decimal? coste = null)
    {
        switch (Estado)
        {
            case EstadoEjecucion.EnCurso:
                Fallar(ahoraUtc, MensajeInterrumpida, coste);
                break;
            case EstadoEjecucion.EnCola:
                PedirCancelacion(ahoraUtc);
                Resumen = MensajeReiniciadaAntesDeEmpezar;
                break;
            default:
                throw NoSePuede();
        }
    }

    private void AnotarHashesDespues(string? hashRegistro, string? hashPerfil)
    {
        HashRegistroDespues = hashRegistro;
        HashPerfilDespues = hashPerfil;
    }

    private void Exigir(EstadoEjecucion estado)
    {
        if (Estado != estado) throw NoSePuede();
    }

    private ReglaNegocioException NoSePuede() => EsTerminal
        ? new ReglaNegocioException("Ya ha terminado.")
        : new ReglaNegocioException("Esta ejecución no está en el paso adecuado para eso.");

    private static string? Recortar(string? texto, int largo) =>
        texto is null || texto.Length <= largo ? texto : texto[..largo];
}
