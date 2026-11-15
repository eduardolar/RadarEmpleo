namespace RadarEmpleo.Domain;

/// <summary>Lo gastado en un día de Madrid.</summary>
/// <param name="Dia">Día de Madrid.</param>
/// <param name="Gastado">Lo de las ejecuciones ya terminadas (con <see cref="ReglasGasto.GastoComputable"/>).</param>
/// <param name="Reservado">El presupuesto de las que están en curso.</param>
/// <param name="Ejecuciones">Cuántas han empezado ese día.</param>
public sealed record GastoDelDia(DateOnly Dia, decimal Gastado, decimal Reservado, int Ejecuciones);

/// <summary>
/// Gasto de los agentes, sin BD ni reloj. Una ejecución cuenta en el día (de Madrid) en que empezó; si no se
/// sabe cuánto costó, se cuenta lo peor: su presupuesto entero.
/// </summary>
public static class ReglasGasto
{
    /// <summary>
    /// Lo que cuenta una ejecución: su coste si se sabe; si no, su presupuesto entero si llegó a empezar
    /// (no se sabe cuánto gastó), y 0 si nunca empezó.
    /// </summary>
    public static decimal GastoComputable(EjecucionAgente e) =>
        e.CosteUsd ?? (e.Inicio is not null ? e.PresupuestoUsd : 0m);

    /// <summary>
    /// Gasto del día de Madrid de <paramref name="ahoraUtc"/>: las ejecuciones con <see cref="EjecucionAgente.Inicio"/>
    /// entre su medianoche y la del día siguiente (calculadas cada una, nunca sumando 24 h, para los cambios de hora).
    /// </summary>
    public static GastoDelDia GastadoHoy(IEnumerable<EjecucionAgente> ejecuciones, DateTime ahoraUtc)
    {
        var dia = Fechas.UtcAMadrid(ahoraUtc).Date;
        var desde = Fechas.MedianocheMadridEnUtc(dia);
        var hasta = Fechas.MedianocheMadridEnUtc(dia.AddDays(1));

        var delDia = ejecuciones
            .Where(e => e.Inicio is { } inicio && inicio >= desde && inicio < hasta)
            .ToList();
        var gastado = delDia.Where(e => e.EsTerminal).Sum(GastoComputable);
        var reservado = delDia.Where(e => e.Estado == EstadoEjecucion.EnCurso).Sum(e => e.PresupuestoUsd);
        return new GastoDelDia(DateOnly.FromDateTime(dia), gastado, reservado, delDia.Count);
    }

    /// <summary>¿Cabe una ejecución más de <paramref name="presupuesto"/> sin pasar del tope? El borde exacto cabe.</summary>
    public static bool CabeEnElTope(decimal gastado, decimal reservado, decimal presupuesto, decimal tope) =>
        gastado + reservado + presupuesto <= tope;
}
