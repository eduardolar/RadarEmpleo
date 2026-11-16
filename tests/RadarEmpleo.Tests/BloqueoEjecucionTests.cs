using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary><see cref="BloqueoEjecucion"/>: formato del archivo <c>.en-curso</c> y cuándo está caducado.</summary>
public class BloqueoEjecucionTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

    private static BloqueoLeido Bloqueo(DateTime caduca, int? pid = 4321, DateTime? inicio = null) =>
        new(1, BloqueoEjecucion.QuienTareaLunes, "Rastreo", null, pid, inicio ?? Ahora.AddMinutes(-10), caduca, true);

    [Fact]
    public void Serializar_DaLaLineaDelPlan()
    {
        var b = BloqueoEjecucion.DeLaApp(TareaAgente.AnalizarEncaje, 12, 4321, Ahora, TimeSpan.FromMinutes(5));
        Assert.Equal(
            """{"version":1,"quien":"RadarEmpleo","tarea":"AnalizarEncaje","ejecucionId":12,"pid":4321,"inicioUtc":"2026-11-16T08:00:00Z","caducaUtc":"2026-11-16T08:10:00Z","usaChrome":false}""",
            BloqueoEjecucion.Serializar(b));
    }

    [Fact]
    public void Leer_LoQueEscribeLaApp_DaLoMismo()
    {
        var b = BloqueoEjecucion.DeLaApp(TareaAgente.ExtraerRegistro, 3, 99, Ahora, TimeSpan.FromMinutes(10));
        Assert.Equal(b, BloqueoEjecucion.Leer(BloqueoEjecucion.Serializar(b)));
    }

    [Fact]
    public void Leer_LoQueEscribePowerShell_LoEntiende()
    {
        // ConvertTo-Json -Compress de la tarea de los lunes: fechas con «o» y sin ejecucionId.
        var b = BloqueoEjecucion.Leer("""
            {"version":1,"quien":"TareaLunes","tarea":"Rastreo","pid":5555,"inicioUtc":"2026-11-16T07:00:00.1234567Z","caducaUtc":"2026-11-16T07:45:00.1234567Z","usaChrome":true}
            """);

        Assert.NotNull(b);
        Assert.Equal("TareaLunes", b.Quien);
        Assert.Equal(5555, b.Pid);
        Assert.Null(b.EjecucionId);
        Assert.True(b.UsaChrome);
        Assert.Equal(DateTimeKind.Utc, b.CaducaUtc.Kind);
        Assert.Equal(new DateTime(2026, 11, 16, 7, 45, 0, DateTimeKind.Utc), b.CaducaUtc.AddTicks(-1234567));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("[1,2]")]
    [InlineData("""{"quien":"TareaLunes"}""")]
    [InlineData("""{"caducaUtc":"2026-11-16T07:45:00Z"}""")]
    [InlineData("""{"quien":"TareaLunes","caducaUtc":"mañana"}""")]
    public void Leer_Ilegible_DaNull(string texto)
    {
        Assert.Null(BloqueoEjecucion.Leer(texto));
    }

    /// <summary>Recién modificado: así solo cuenta lo que se prueba.</summary>
    private static readonly DateTime Modificado = Ahora.AddMinutes(-1);

    [Fact]
    public void EstaCaducado_PorFecha()
    {
        Assert.True(BloqueoEjecucion.EstaCaducado(Bloqueo(Ahora.AddSeconds(-1)), Modificado, Ahora, _ => Ahora.AddHours(-1)));
    }

    [Fact]
    public void EstaCaducado_ConElProcesoVivoYEnFecha_No()
    {
        Assert.False(BloqueoEjecucion.EstaCaducado(Bloqueo(Ahora.AddMinutes(30)), Modificado, Ahora, _ => Ahora.AddHours(-1)));
    }

    [Fact]
    public void EstaCaducado_PorPidMuerto()
    {
        Assert.True(BloqueoEjecucion.EstaCaducado(Bloqueo(Ahora.AddMinutes(30)), Modificado, Ahora, _ => null));
    }

    [Fact]
    public void EstaCaducado_PorPidConOtraHoraDeInicio()
    {
        // El proceso con ese pid empezó después que el bloqueo: es otro.
        Assert.True(BloqueoEjecucion.EstaCaducado(Bloqueo(Ahora.AddMinutes(30)), Modificado, Ahora, _ => Ahora.AddMinutes(-1)));
    }

    [Fact]
    public void EstaCaducado_SinPidYRecien_No()
    {
        Assert.False(BloqueoEjecucion.EstaCaducado(Bloqueo(Ahora.AddMinutes(30), pid: null), Modificado, Ahora, _ => null));
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(61, true)]
    public void EstaCaducado_UnaCaducidadMuyLejanaTieneTopeDeUnaHoraDesdeLaModificacion(int minutos, bool caducado)
    {
        var lejano = Bloqueo(Ahora.AddDays(30));
        Assert.Equal(caducado, BloqueoEjecucion.EstaCaducado(lejano, Ahora.AddMinutes(-minutos), Ahora, _ => Ahora.AddHours(-2)));
    }

    [Fact]
    public void EstaCaducado_SinPid_NoEsEterno()
    {
        var sinPid = Bloqueo(Ahora.AddDays(30), pid: null);
        Assert.True(BloqueoEjecucion.EstaCaducado(sinPid, Ahora.AddMinutes(-61), Ahora, _ => null));
    }

    [Fact]
    public void EstaCaducado_PidSinHoraLegible_NoEsEterno()
    {
        // InicioDelProceso da MinValue cuando el proceso existe pero no deja leer su hora: vivo, pero solo una hora.
        var b = Bloqueo(Ahora.AddDays(30));
        Assert.False(BloqueoEjecucion.EstaCaducado(b, Ahora.AddMinutes(-30), Ahora, _ => DateTime.MinValue));
        Assert.True(BloqueoEjecucion.EstaCaducado(b, Ahora.AddMinutes(-61), Ahora, _ => DateTime.MinValue));
    }

    [Fact]
    public void EstaCaducado_UnaModificacionFuturaCuentaComoCaducado()
    {
        // Si contara como «ahora», volvería a contar como ahora en cada sondeo y sería eterno.
        var b = Bloqueo(Ahora.AddDays(30));
        Assert.True(BloqueoEjecucion.EstaCaducado(b, Ahora.AddYears(5), Ahora, _ => Ahora.AddHours(-2)));
        Assert.True(BloqueoEjecucion.EstaCaducado(b, Ahora.AddMinutes(10), Ahora, _ => Ahora.AddHours(-2)));
        // Un adelanto pequeño del reloj se admite.
        Assert.False(BloqueoEjecucion.EstaCaducado(b, Ahora.AddSeconds(30), Ahora, _ => Ahora.AddHours(-2)));
    }

    [Fact]
    public void EstaCaducado_UnoPropioDeOtraEjecucion_Si()
    {
        var propio = new BloqueoLeido(1, BloqueoEjecucion.QuienRadar, "AnalizarEncaje", 7, 4321, Ahora.AddMinutes(-1), Ahora.AddMinutes(9), false);

        Assert.True(BloqueoEjecucion.EstaCaducado(propio, Modificado, Ahora, _ => Ahora.AddHours(-1), pidPropio: 4321, ejecucionActual: 8));
        Assert.False(BloqueoEjecucion.EstaCaducado(propio, Modificado, Ahora, _ => Ahora.AddHours(-1), pidPropio: 4321, ejecucionActual: 7));
        // De otra API viva (otro pid): no.
        Assert.False(BloqueoEjecucion.EstaCaducado(propio, Modificado, Ahora, _ => Ahora.AddHours(-1), pidPropio: 9999, ejecucionActual: 8));
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(61, true)]
    public void IlegibleCaducado_SesentaMinutosDespuesDeModificarlo(int minutos, bool caducado)
    {
        Assert.Equal(caducado, BloqueoEjecucion.IlegibleCaducado(Ahora.AddMinutes(-minutos), Ahora));
    }

    [Fact]
    public void IlegibleCaducado_ConFechaFutura_CuentaComoCaducado()
    {
        Assert.True(BloqueoEjecucion.IlegibleCaducado(Ahora.AddYears(1), Ahora));
        Assert.False(BloqueoEjecucion.IlegibleCaducado(Ahora.AddSeconds(30), Ahora));
    }

    [Theory]
    [InlineData("TareaLunes", "la tarea de los lunes")]
    [InlineData("Empleo", "/empleo")]
    [InlineData(null, "otra tarea")]
    [InlineData("Otro", "Otro")]
    [InlineData("<script>alert(1)</script>", "scriptalert1/script")]
    [InlineData("\u0007\u202E", "otra tarea")]
    [InlineData("una tarea con un nombre larguísimo que no cabe en la pantalla", "una tarea con un nombre larguísimo que n")]
    public void NombreVisible(string? quien, string visible)
    {
        Assert.Equal(visible, BloqueoEjecucion.NombreVisible(quien));
    }
}
