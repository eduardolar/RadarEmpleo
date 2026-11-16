namespace RadarEmpleo.Services.Agentes;

/// <summary>
/// Los mensajes para Eduardo cuando una ejecución no sale. Sin nombres de tipos ni detalles técnicos (esos van al log)
/// y sin dólares: Claude Code va con su suscripción, así que se habla de «uso estimado» (decisión del 14/11/2026). Por
/// dentro los topes siguen en el equivalente en USD que devuelve Claude.
/// </summary>
public static class MensajesEjecucion
{
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

    /// <summary>Error cuando una ejecución se pasa del tope de uso de su tarea (<c>--max-budget-usd</c>).</summary>
    public const string TopeTarea = "Ha llegado al tope de uso de esta tarea y la he parado.";

    /// <summary>Error al encolar o al empezar cuando ya no cabe en el tope de uso de hoy.</summary>
    public const string TopeDiario = "Has llegado al tope de uso estimado de hoy. Mañana se puede volver a lanzar.";

    /// <summary>El mensaje de un fallo de Claude en una tarea con ese tiempo máximo.</summary>
    public static string De(FalloClaude fallo, TimeSpan tiempoMaximo) => fallo switch
    {
        FalloClaude.NoInstalado => NoInstalado,
        FalloClaude.TiempoAgotado => $"Ha tardado más de {Duracion(tiempoMaximo)} y la he parado.",
        FalloClaude.PresupuestoAgotado => TopeTarea,
        FalloClaude.SalidaNoValida => SalidaNoValida,
        FalloClaude.Cancelado => Cancelada,
        _ => ErrorDeClaude
    };

    /// <summary>Error cuando el bloqueo compartido sigue ocupado después de la espera máxima.</summary>
    public static string BloqueoDemasiadoTiempo(string quien) =>
        $"La otra ejecución ({quien}) sigue en marcha después de una hora.";

    /// <summary>«5 min», «90 s», «1 h 2 min».</summary>
    public static string Duracion(TimeSpan tiempo)
    {
        if (tiempo.TotalSeconds < 60) return $"{(int)tiempo.TotalSeconds} s";
        if (tiempo.TotalHours < 1)
            return tiempo.Seconds == 0 ? $"{(int)tiempo.TotalMinutes} min" : $"{(int)tiempo.TotalMinutes} min {tiempo.Seconds} s";
        return tiempo.Minutes == 0 ? $"{(int)tiempo.TotalHours} h" : $"{(int)tiempo.TotalHours} h {tiempo.Minutes} min";
    }
}
