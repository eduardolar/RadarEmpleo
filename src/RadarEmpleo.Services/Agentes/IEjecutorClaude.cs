using System.Text.Json;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Agentes;

/// <summary>Lo que necesita el ejecutor para lanzar Claude una vez.</summary>
/// <param name="Tarea">Para el log y para el ejecutor falso, que responde según la tarea.</param>
/// <param name="Argumentos">Ya construidos por <see cref="ConstructorArgumentos"/>, sin el ejecutable.</param>
/// <param name="EntradaEstandar">El prompt completo (se escribe en UTF-8 sin BOM).</param>
/// <param name="RutaLog">Ruta absoluta del log de esta ejecución.</param>
/// <param name="SalidaPrivada">El log no guarda la salida (<see cref="DefinicionTarea.SalidaConDatosPersonales"/>).</param>
public sealed record PeticionClaude(
    TareaAgente Tarea,
    IReadOnlyList<string> Argumentos,
    string CarpetaTrabajo,
    string EntradaEstandar,
    TimeSpan TiempoMaximo,
    string RutaLog,
    bool SalidaPrivada = false);

/// <summary>Cómo ha acabado una llamada a Claude.</summary>
/// <param name="Correcta">Salida 0, JSON legible e <c>is_error</c> a false.</param>
/// <param name="Subtipo">El <c>subtype</c> de Claude: «success», «error_max_budget_usd»…</param>
/// <param name="Resultado">El campo <c>result</c>.</param>
/// <param name="SalidaEstructurada"><c>structured_output</c> o, como plan B, <c>result</c> leído como JSON.</param>
/// <param name="Detalle">Lo técnico, para el log: nunca se enseña.</param>
public sealed record RespuestaClaude(
    bool Correcta,
    string? Subtipo,
    decimal? CosteUsd,
    int? Turnos,
    string? SesionId,
    string? Resultado,
    JsonElement? SalidaEstructurada,
    FalloClaude? Fallo,
    string? Detalle)
{
    /// <summary>Una llamada que no ha llegado a dar una respuesta de Claude.</summary>
    public static RespuestaClaude Fallida(FalloClaude fallo, string? detalle = null) =>
        new(false, null, null, null, null, null, null, fallo, detalle);
}

/// <summary>Por qué no ha salido bien una llamada.</summary>
public enum FalloClaude
{
    NoInstalado,
    TiempoAgotado,
    Cancelado,
    PresupuestoAgotado,
    SalidaNoValida,
    ErrorDeClaude,
    CodigoSalida
}

/// <summary>Lanza Claude Code. La implementación real es <see cref="EjecutorClaudeCli"/>; los tests usan <see cref="EjecutorClaudeFalso"/>.</summary>
public interface IEjecutorClaude
{
    /// <summary>Lanza una ejecución y espera a que acabe. Avisa del pid (y su hora de inicio, UTC) en cuanto existe el proceso.</summary>
    Task<RespuestaClaude> EjecutarAsync(PeticionClaude peticion, Action<int, DateTime> alEmpezar, CancellationToken cancelacion);

    /// <summary>«claude --version» («2.1.185»), con caché de una hora; null si no se puede saber.</summary>
    Task<string?> VersionAsync(CancellationToken cancelacion);
}
