using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Agentes;

/// <summary>Qué hace el ejecutor falso con una tarea.</summary>
public enum ModoFalso
{
    /// <summary>Responde bien, con <see cref="RespuestaFalsa.Salida"/> como salida estructurada.</summary>
    Ok,
    /// <summary>Claude devuelve un error (<c>is_error</c>).</summary>
    Error,
    /// <summary>Se pasa del presupuesto.</summary>
    PresupuestoAgotado,
    /// <summary>Responde algo que no es JSON.</summary>
    SalidaNoValida,
    /// <summary>No responde: espera hasta que lo cancelen o se acabe el tiempo máximo.</summary>
    NoResponde
}

/// <summary>Una respuesta programada del ejecutor falso.</summary>
/// <param name="EsperaMs">Lo que tarda antes de responder (se corta con la cancelación o el tiempo máximo).</param>
public sealed record RespuestaFalsa(
    ModoFalso Modo = ModoFalso.Ok,
    JsonElement? Salida = null,
    decimal? CosteUsd = 0.01m,
    int? Turnos = 1,
    int EsperaMs = 0,
    string? Resultado = null);

/// <summary>
/// Ejecutor sin Claude, para los tests y para las pruebas en vivo sin gastar (<c>Agentes:Ejecutor=Falso</c>). No lanza
/// ningún proceso ni escribe nada: responde lo programado para cada tarea. Los tests pueden enganchar
/// <see cref="AntesDeResponder"/> (por ejemplo, para tocar el registro como si lo hubiera hecho Claude).
/// </summary>
public sealed class EjecutorClaudeFalso : IEjecutorClaude
{
    /// <summary>El pid que anuncia: no existe ningún proceso con él (los de Windows son múltiplos de 4).</summary>
    public const int PidFalso = 999_999_999;

    public const string VersionFalsa = "0.0.0-falso";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConcurrentDictionary<TareaAgente, RespuestaFalsa> _respuestas = new();
    private readonly ConcurrentDictionary<TareaAgente, ConcurrentQueue<RespuestaFalsa>> _secuencias = new();
    private readonly ConcurrentQueue<PeticionClaude> _peticiones = new();
    private readonly IClock _reloj;

    public EjecutorClaudeFalso(IClock reloj) => _reloj = reloj;

    /// <summary>Lo que le han pedido, en orden.</summary>
    public IReadOnlyCollection<PeticionClaude> Peticiones => _peticiones;

    /// <summary>Se llama con cada petición justo antes de responder (después de la espera).</summary>
    public Func<PeticionClaude, CancellationToken, Task>? AntesDeResponder { get; set; }

    /// <summary>Cuántas ejecuciones han recibido la cancelación.</summary>
    public int Canceladas => _canceladas;
    private int _canceladas;

    /// <summary>Programa la respuesta de una tarea (sin programar: Ok con <c>{}</c>).</summary>
    public void Programar(TareaAgente tarea, RespuestaFalsa respuesta) => _respuestas[tarea] = respuesta;

    /// <summary>
    /// Programa una respuesta para cada una de las próximas llamadas de la tarea, en orden (las partes de una tarea por
    /// partes). Cuando se acaban, vuelve a responder lo de <see cref="Programar"/>. Sustituye la secuencia anterior.
    /// </summary>
    public void ProgramarSecuencia(TareaAgente tarea, params RespuestaFalsa[] respuestas) =>
        _secuencias[tarea] = new ConcurrentQueue<RespuestaFalsa>(respuestas);

    /// <summary>
    /// Lee las respuestas de <c>&lt;carpeta&gt;\&lt;tarea-en-kebab&gt;.json</c> («analizar-encaje.json»…), con la
    /// forma de <see cref="RespuestaFalsa"/>. Las que falten o no se entiendan se quedan sin programar.
    /// </summary>
    public void CargarDe(string carpeta)
    {
        foreach (var definicion in DefinicionesTareas.Todas)
        {
            var ruta = Path.Combine(carpeta, $"{definicion.NombrePrompt}.json");
            if (!File.Exists(ruta)) continue;
            try
            {
                if (JsonSerializer.Deserialize<RespuestaFalsa>(File.ReadAllText(ruta), Json) is { } respuesta)
                    Programar(definicion.Tarea, respuesta);
            }
            catch (JsonException)
            {
                // Un archivo de ejemplo roto no impide arrancar: esa tarea responde lo de por defecto.
            }
        }
    }

    public async Task<RespuestaClaude> EjecutarAsync(PeticionClaude peticion, Action<int, DateTime> alEmpezar, CancellationToken cancelacion)
    {
        _peticiones.Enqueue(peticion);
        var respuesta = _secuencias.TryGetValue(peticion.Tarea, out var secuencia) && secuencia.TryDequeue(out var siguiente)
            ? siguiente
            : _respuestas.GetValueOrDefault(peticion.Tarea, new RespuestaFalsa());
        alEmpezar(PidFalso, _reloj.AhoraUtc);

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        limite.CancelAfter(peticion.TiempoMaximo);
        try
        {
            var espera = respuesta.Modo == ModoFalso.NoResponde ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(respuesta.EsperaMs);
            if (espera != TimeSpan.Zero) await Task.Delay(espera, limite.Token);
            if (AntesDeResponder is { } antes) await antes(peticion, limite.Token);
        }
        catch (OperationCanceledException)
        {
            if (!cancelacion.IsCancellationRequested) return RespuestaClaude.Fallida(FalloClaude.TiempoAgotado);
            Interlocked.Increment(ref _canceladas);
            return RespuestaClaude.Fallida(FalloClaude.Cancelado);
        }

        return respuesta.Modo switch
        {
            ModoFalso.Ok => new RespuestaClaude(true, "success", respuesta.CosteUsd, respuesta.Turnos, "sesion-falsa",
                respuesta.Resultado ?? "Hecho.", respuesta.Salida ?? JsonSerializer.SerializeToElement(new { }), null, null),
            ModoFalso.Error => new RespuestaClaude(false, "error_during_execution", respuesta.CosteUsd, respuesta.Turnos, "sesion-falsa",
                null, null, FalloClaude.ErrorDeClaude, "Error programado del ejecutor falso."),
            ModoFalso.PresupuestoAgotado => new RespuestaClaude(false, "error_max_budget_usd", respuesta.CosteUsd, respuesta.Turnos,
                "sesion-falsa", null, null, FalloClaude.PresupuestoAgotado, "Presupuesto agotado (falso)."),
            _ => RespuestaClaude.Fallida(FalloClaude.SalidaNoValida, "Salida no válida (falso).")
        };
    }

    public Task<string?> VersionAsync(CancellationToken cancelacion) => Task.FromResult<string?>(VersionFalsa);
}
