using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary><see cref="ReglasRecordatorio"/> y los métodos de <see cref="Recordatorio"/> (sin BD).</summary>
public class ReglasRecordatorioTests
{
    private static DateTime Utc(int anio, int mes, int dia, int hora, int minuto = 0) =>
        new(anio, mes, dia, hora, minuto, 0, DateTimeKind.Utc);

    [Fact]
    public void FechaHoraUtc_EnInvierno_RestaUnaHora()
    {
        Assert.Equal(Utc(2026, 11, 11, 14), ReglasRecordatorio.FechaHoraUtc(new DateOnly(2026, 11, 11), new TimeOnly(15, 0)));
    }

    [Fact]
    public void FechaHoraUtc_SinHora_SonLasNueveDeMadrid()
    {
        Assert.Equal(Utc(2026, 11, 17, 8), ReglasRecordatorio.FechaHoraUtc(new DateOnly(2026, 11, 17)));
        Assert.Equal(new TimeOnly(9, 0), ReglasRecordatorio.HoraPorDefecto);
    }

    [Theory]
    [InlineData(3, 29, 7)]  // el día que empieza el horario de verano
    [InlineData(10, 25, 8)] // el día que vuelve el de invierno
    public void FechaHoraUtc_ElDiaDelCambioDeHora_UsaElHorarioQueYaRige(int mes, int dia, int horaUtc)
    {
        Assert.Equal(Utc(2026, mes, dia, horaUtc), ReglasRecordatorio.FechaHoraUtc(new DateOnly(2026, mes, dia), new TimeOnly(9, 0)));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 30)]
    [InlineData(2, 59)]
    public void FechaHoraUtc_EnElSaltoDeMarzo_LanzaValidacionConElCampoHora(int hora, int minuto)
    {
        // Domingo 28/03/2027: el reloj de Madrid pasa de las 02:00 a las 03:00.
        var error = Assert.Throws<ValidacionException>(() =>
            ReglasRecordatorio.FechaHoraUtc(new DateOnly(2027, 3, 28), new TimeOnly(hora, minuto)));

        Assert.Equal("hora", error.Campo);
        Assert.Equal("Esa hora no existe ese día: el reloj se adelanta de 02:00 a 03:00.", error.Message);
    }

    [Fact]
    public void FechaHoraUtc_JustoDespuesDelSalto_SonLasTresDeVerano()
    {
        Assert.Equal(Utc(2027, 3, 28, 1), ReglasRecordatorio.FechaHoraUtc(new DateOnly(2027, 3, 28), new TimeOnly(3, 0)));
        Assert.Equal(Utc(2027, 3, 28, 0, 59), ReglasRecordatorio.FechaHoraUtc(new DateOnly(2027, 3, 28), new TimeOnly(1, 59)));
    }

    [Fact]
    public void FechaHoraUtc_LaHoraRepetidaDeOctubre_NoFalla()
    {
        // 31/10/2027: de 02:00 a 02:59 pasa dos veces; vale la de invierno, como hasta ahora.
        Assert.Equal(Utc(2027, 10, 31, 1, 30), ReglasRecordatorio.FechaHoraUtc(new DateOnly(2027, 10, 31), new TimeOnly(2, 30)));
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("9h")]
    [InlineData("15:00:00")]
    [InlineData("abc")]
    [InlineData("12:60")]
    public void LeerHora_QueNoEsHoraYMinutos_LanzaValidacionConElCampoHora(string texto)
    {
        var error = Assert.Throws<ValidacionException>(() => ReglasRecordatorio.LeerHora(texto));

        Assert.Equal("hora", error.Campo);
    }

    [Theory]
    [InlineData("09:05", 9, 5)]
    [InlineData("00:00", 0, 0)]
    [InlineData("23:59", 23, 59)]
    [InlineData(" 15:00 ", 15, 0)]
    public void LeerHora_Valida_LaDevuelve(string texto, int hora, int minuto)
    {
        Assert.Equal(new TimeOnly(hora, minuto), ReglasRecordatorio.LeerHora(texto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void LeerHora_Vacia_SonLasNueve(string? texto)
    {
        Assert.Equal(new TimeOnly(9, 0), ReglasRecordatorio.LeerHora(texto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void LeerHora_DeUnaEntrevistaVacia_LanzaValidacionConElCampoHora(string? texto)
    {
        var error = Assert.Throws<ValidacionException>(() => ReglasRecordatorio.LeerHora(TipoRecordatorio.Entrevista, texto));

        Assert.Equal("hora", error.Campo);
        Assert.Equal("Pon la hora de la entrevista.", error.Message);
    }

    [Fact]
    public void LeerHora_DeUnaEntrevistaConHora_LaDevuelve_TambienLasNueve()
    {
        Assert.Equal(new TimeOnly(15, 30), ReglasRecordatorio.LeerHora(TipoRecordatorio.Entrevista, "15:30"));
        Assert.Equal(new TimeOnly(9, 0), ReglasRecordatorio.LeerHora(TipoRecordatorio.Entrevista, "09:00"));
    }

    [Theory]
    [InlineData(TipoRecordatorio.Seguimiento)]
    [InlineData(TipoRecordatorio.PublicacionLinkedIn)]
    [InlineData(TipoRecordatorio.Plazo)]
    [InlineData(TipoRecordatorio.Otro)]
    public void LeerHora_DeLosDemasTiposVacia_SonLasNueve(TipoRecordatorio tipo)
    {
        Assert.Equal(new TimeOnly(9, 0), ReglasRecordatorio.LeerHora(tipo, null));
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public void ValidarDia_FueraDeRango_LanzaValidacionConElCampoDia(int anio)
    {
        var error = Assert.Throws<ValidacionException>(() => ReglasRecordatorio.ValidarDia(new DateOnly(anio, 6, 1)));

        Assert.Equal("dia", error.Campo);
    }

    [Fact]
    public void PosponerA_AlCruzarElCambioDeHora_ConservaLaHoraDeMadrid()
    {
        // Viernes 23/10/2026 a las 15:00 de Madrid (verano: 13:00Z).
        var recordatorio = new Recordatorio { FechaHora = Utc(2026, 10, 23, 13) };

        recordatorio.PosponerA(new DateOnly(2026, 10, 27));

        Assert.Equal(Utc(2026, 10, 27, 14), recordatorio.FechaHora);
    }

    [Fact]
    public void PosponerA_AlDiaDelSaltoDeMarzo_UnaHoraQueNoExiste_PasaALasTres()
    {
        // Sábado 27/03/2027 a las 02:30 de Madrid (invierno: 01:30Z); el domingo esa hora no existe.
        var recordatorio = new Recordatorio { FechaHora = Utc(2027, 3, 27, 1, 30) };

        recordatorio.PosponerA(new DateOnly(2027, 3, 28));

        Assert.Equal(Utc(2027, 3, 28, 1), recordatorio.FechaHora); // las 03:00 de verano
    }

    [Fact]
    public void PosponerA_AUnDiaPasado_LoAdmite()
    {
        var recordatorio = new Recordatorio { FechaHora = Utc(2026, 11, 14, 8) };

        recordatorio.PosponerA(new DateOnly(2026, 11, 3));

        Assert.Equal(Utc(2026, 11, 3, 8), recordatorio.FechaHora);
    }

    [Fact]
    public void PosponerA_YaHecho_LanzaReglaNegocioYNoLoMueve()
    {
        var fecha = Utc(2026, 11, 13, 8);
        var recordatorio = new Recordatorio { FechaHora = fecha };
        recordatorio.MarcarHecho(Utc(2026, 11, 13, 9));

        var error = Assert.Throws<ReglaNegocioException>(() => recordatorio.PosponerA(new DateOnly(2026, 11, 14)));

        Assert.Equal("Ya está hecho. Márcalo como pendiente para moverlo.", error.Message);
        Assert.Equal(fecha, recordatorio.FechaHora);
    }

    [Fact]
    public void MarcarHecho_DosVeces_LaSegundaNoCambiaLaFecha()
    {
        var recordatorio = new Recordatorio();
        var primera = Utc(2026, 11, 13, 9);

        recordatorio.MarcarHecho(primera);
        recordatorio.MarcarHecho(primera.AddMinutes(5));

        Assert.True(recordatorio.Hecho);
        Assert.Equal(primera, recordatorio.FechaHecho);
    }

    [Fact]
    public void MarcarPendiente_BorraLaFechaDeHecho()
    {
        var recordatorio = new Recordatorio();
        recordatorio.MarcarHecho(Utc(2026, 11, 13, 9));

        recordatorio.MarcarPendiente();

        Assert.False(recordatorio.Hecho);
        Assert.Null(recordatorio.FechaHecho);
    }
}
