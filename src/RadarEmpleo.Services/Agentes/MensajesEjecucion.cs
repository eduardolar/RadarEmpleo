using System.Globalization;

namespace RadarEmpleo.Services.Agentes;

/// <summary>Los mensajes para Eduardo cuando una ejecución no sale. Sin nombres de tipos ni detalles técnicos (esos van al log).</summary>
public static class MensajesEjecucion
{
    private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-ES");

    public const string NoInstalado = "No encuentro Claude Code en este PC.";
    public const string SalidaNoValida = "Claude ha respondido en un formato que no entiendo.";
    public const string ErrorDeClaude = "Claude ha devuelto un error.";
    public const string Inesperado = "Algo ha fallado al lanzar Claude.";
    public const string SinManejador = "Esta tarea todavía no se puede lanzar desde la app.";
    public const string Cancelada = "Cancelada.";

    /// <summary>Otra copia de la API (otra ventana, otro arranque) tiene la cola de esta base de datos.</summary>
    public const string OtraCopiaLlevaLaCola =
        "Otra copia de RadarEmpleo lleva los agentes de esta base de datos. Lánzalos desde esa o ciérrala y vuelve a intentarlo en un momento.";

    /// <summary>La cola todavía no ha arrancado (o se está cerrando).</summary>
    public const string ColaSinArrancar = "Los agentes todavía no están listos. Vuelve a intentarlo en un momento.";

    /// <summary>No se puede abrir el archivo de exclusión junto a la base de datos.</summary>
    public static string SinExclusion(string detalle) =>
        $"No puedo usar los agentes: el archivo de control junto a la base de datos {detalle}.";

    /// <summary>El mensaje de un fallo de Claude en una tarea con ese tiempo máximo y presupuesto.</summary>
    public static string De(FalloClaude fallo, TimeSpan tiempoMaximo, decimal presupuestoUsd) => fallo switch
    {
        FalloClaude.NoInstalado => NoInstalado,
        FalloClaude.TiempoAgotado => $"Ha tardado más de {Duracion(tiempoMaximo)} y la he parado.",
        FalloClaude.PresupuestoAgotado => $"Ha llegado al tope de {Usd(presupuestoUsd)} de esta tarea y la he parado.",
        FalloClaude.SalidaNoValida => SalidaNoValida,
        FalloClaude.Cancelado => Cancelada,
        _ => ErrorDeClaude
    };

    /// <summary>Error al encolar o al empezar cuando ya no cabe en el tope de hoy.</summary>
    public static string TopeDiario(decimal topeUsd) =>
        $"Has llegado al tope de gasto de hoy ({Usd(topeUsd)}). Mañana se puede volver a lanzar.";

    /// <summary>Error cuando el bloqueo compartido sigue ocupado después de la espera máxima.</summary>
    public static string BloqueoDemasiadoTiempo(string quien) =>
        $"La otra ejecución ({quien}) sigue en marcha después de una hora.";

    /// <summary>«0,50 USD».</summary>
    public static string Usd(decimal importe) => $"{importe.ToString("0.00", Espanol)} USD";

    /// <summary>«5 min», «90 s», «1 h 2 min».</summary>
    public static string Duracion(TimeSpan tiempo)
    {
        if (tiempo.TotalSeconds < 60) return $"{(int)tiempo.TotalSeconds} s";
        if (tiempo.TotalHours < 1)
            return tiempo.Seconds == 0 ? $"{(int)tiempo.TotalMinutes} min" : $"{(int)tiempo.TotalMinutes} min {tiempo.Seconds} s";
        return tiempo.Minutes == 0 ? $"{(int)tiempo.TotalHours} h" : $"{(int)tiempo.TotalHours} h {tiempo.Minutes} min";
    }
}
