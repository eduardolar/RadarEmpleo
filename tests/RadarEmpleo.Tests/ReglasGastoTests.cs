using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary><see cref="ReglasGasto"/>: lo que cuenta cada ejecución y el gasto del día de Madrid.</summary>
public class ReglasGastoTests
{
    /// <summary>Una hora fija (16/11/2026, 13:00 de Madrid): lejos de medianoche y de los cambios de hora.</summary>
    private static readonly DateTime Ahora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static EjecucionAgente Ejecucion(DateTime? inicio, EstadoEjecucion estado, decimal? coste = null, decimal presupuesto = 0.50m)
    {
        var e = EjecucionAgente.Nueva(TareaAgente.AnalizarEncaje, "{}", presupuesto, 300, null, inicio ?? Ahora);
        if (inicio is null)
        {
            if (estado == EstadoEjecucion.Cancelada) e.PedirCancelacion(Ahora);
            return e;
        }
        e.Empezar(inicio.Value, null, null);
        switch (estado)
        {
            case EstadoEjecucion.Ok: e.Terminar(inicio.Value, coste, 1, null, "x", null, null, null); break;
            case EstadoEjecucion.Error: e.Fallar(inicio.Value, "x", coste); break;
        }
        return e;
    }

    [Fact]
    public void GastoComputable_ConCoste_EsEseCoste()
    {
        Assert.Equal(0.18m, ReglasGasto.GastoComputable(Ejecucion(Ahora, EstadoEjecucion.Ok, 0.18m)));
    }

    [Fact]
    public void GastoComputable_EmpezadaSinCoste_EsElPresupuesto()
    {
        Assert.Equal(0.50m, ReglasGasto.GastoComputable(Ejecucion(Ahora, EstadoEjecucion.Error)));
    }

    [Fact]
    public void GastoComputable_SinEmpezar_EsCero()
    {
        Assert.Equal(0m, ReglasGasto.GastoComputable(Ejecucion(null, EstadoEjecucion.Cancelada)));
    }

    [Fact]
    public void GastadoHoy_CuentaLasDeHoyEnMadridYNoLasDeMañana()
    {
        // 16/11/2026, invierno (UTC+1): las 23:30 de Madrid son las 22:30 UTC; las 00:10 del 17, las 23:10 UTC del 16.
        var ahora = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var ejecuciones = new[]
        {
            Ejecucion(new DateTime(2026, 11, 16, 22, 30, 0, DateTimeKind.Utc), EstadoEjecucion.Ok, 0.20m),
            Ejecucion(new DateTime(2026, 11, 16, 23, 10, 0, DateTimeKind.Utc), EstadoEjecucion.Ok, 0.30m),
            Ejecucion(new DateTime(2026, 11, 15, 23, 0, 0, DateTimeKind.Utc), EstadoEjecucion.Ok, 0.40m), // 00:00 del 16 en Madrid
            Ejecucion(new DateTime(2026, 11, 15, 22, 59, 0, DateTimeKind.Utc), EstadoEjecucion.Ok, 1m),   // 23:59 del 15
        };

        var gasto = ReglasGasto.GastadoHoy(ejecuciones, ahora);

        Assert.Equal(new DateOnly(2026, 11, 16), gasto.Dia);
        Assert.Equal(0.60m, gasto.Gastado);
        Assert.Equal(2, gasto.Ejecuciones);
    }

    [Fact]
    public void GastadoHoy_ElDiaDelCambioDeHoraDeOctubre_TieneVeinticincoHoras()
    {
        // 25/10/2026: de 00:00 de Madrid (22:00 UTC del 24, verano) a 00:00 del 26 (23:00 UTC del 25, invierno).
        var ahora = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        var ejecuciones = new[]
        {
            Ejecucion(new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc), EstadoEjecucion.Ok, 0.10m),  // 00:00 del 25
            Ejecucion(new DateTime(2026, 10, 25, 22, 30, 0, DateTimeKind.Utc), EstadoEjecucion.Ok, 0.20m), // 23:30 del 25
            Ejecucion(new DateTime(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc), EstadoEjecucion.Ok, 0.40m),  // 00:00 del 26
            Ejecucion(new DateTime(2026, 10, 24, 21, 59, 0, DateTimeKind.Utc), EstadoEjecucion.Ok, 0.80m), // 23:59 del 24
        };

        var gasto = ReglasGasto.GastadoHoy(ejecuciones, ahora);

        Assert.Equal(0.30m, gasto.Gastado);
        Assert.Equal(2, gasto.Ejecuciones);
    }

    [Fact]
    public void GastadoHoy_ElDiaDelCambioDeHoraDeMarzo_TieneVeintitresHoras()
    {
        // 29/03/2026: de 00:00 de Madrid (23:00 UTC del 28) a 00:00 del 30 (22:00 UTC del 29).
        var ahora = new DateTime(2026, 3, 29, 12, 0, 0, DateTimeKind.Utc);
        var ejecuciones = new[]
        {
            Ejecucion(new DateTime(2026, 3, 29, 21, 59, 0, DateTimeKind.Utc), EstadoEjecucion.Ok, 0.10m), // 23:59 del 29
            Ejecucion(new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc), EstadoEjecucion.Ok, 0.40m),  // 00:00 del 30
        };

        Assert.Equal(0.10m, ReglasGasto.GastadoHoy(ejecuciones, ahora).Gastado);
    }

    [Fact]
    public void GastadoHoy_LasEnCursoReservanSuPresupuestoYNoCuentanComoGastado()
    {
        var ahora = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var ejecuciones = new[]
        {
            Ejecucion(ahora.AddMinutes(-1), EstadoEjecucion.EnCurso, presupuesto: 1m),
            Ejecucion(ahora.AddMinutes(-30), EstadoEjecucion.Error), // sin coste: cuenta el presupuesto
            Ejecucion(null, EstadoEjecucion.EnCola),
        };

        var gasto = ReglasGasto.GastadoHoy(ejecuciones, ahora);

        Assert.Equal(0.50m, gasto.Gastado);
        Assert.Equal(1m, gasto.Reservado);
        Assert.Equal(2, gasto.Ejecuciones);
    }

    [Theory]
    [InlineData("14.50", "0", "0.50", "15", true)]
    [InlineData("14.00", "0.50", "0.50", "15", true)]
    [InlineData("14.51", "0", "0.50", "15", false)]
    [InlineData("0", "14.60", "0.50", "15", false)]
    public void CabeEnElTope_EnElBordeExactoCabe(string gastado, string reservado, string presupuesto, string tope, bool cabe)
    {
        Assert.Equal(cabe, ReglasGasto.CabeEnElTope(decimal.Parse(gastado, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(reservado, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(presupuesto, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(tope, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
