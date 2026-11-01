using RadarEmpleo.Domain;
using static RadarEmpleo.Domain.EstadoCandidatura;

namespace RadarEmpleo.Tests;

/// <summary>Reglas de transición de <see cref="ReglasEstado"/> (lógica pura, sin BD).</summary>
public class ReglasEstadoTests
{
    [Theory]
    [InlineData(Detectada, Preparada)]
    [InlineData(Detectada, Enviada)]      // se pueden saltar pasos hacia delante
    [InlineData(Detectada, Oferta)]
    [InlineData(Preparada, Enviada)]
    [InlineData(Enviada, Entrevista)]
    [InlineData(Entrevista, Oferta)]
    [InlineData(Detectada, Descartada)]   // descartar desde cualquiera
    [InlineData(Oferta, Descartada)]
    [InlineData(Enviada, Cerrada)]        // cerrar desde cualquiera
    [InlineData(Descartada, Cerrada)]
    [InlineData(Descartada, Detectada)]   // reabrir
    [InlineData(Cerrada, Detectada)]
    public void PuedePasar_TransicionPermitida_DevuelveTrue(EstadoCandidatura de, EstadoCandidatura a)
    {
        Assert.True(ReglasEstado.PuedePasar(de, a));
    }

    [Theory]
    [InlineData(Enviada, Preparada)]      // retroceder
    [InlineData(Oferta, Detectada)]
    [InlineData(Entrevista, Enviada)]
    [InlineData(Descartada, Enviada)]     // desde un estado final solo se reabre a Detectada
    [InlineData(Cerrada, Oferta)]
    [InlineData(Enviada, Enviada)]        // mismo estado
    [InlineData(Descartada, Descartada)]
    public void PuedePasar_TransicionProhibida_DevuelveFalse(EstadoCandidatura de, EstadoCandidatura a)
    {
        Assert.False(ReglasEstado.PuedePasar(de, a));
    }

    [Fact]
    public void Validar_DescartadaSinMotivo_LanzaReglaNegocio()
    {
        var ex = Assert.Throws<ReglaNegocioException>(() => ReglasEstado.Validar(Enviada, Descartada, motivo: null));
        Assert.Contains("motivo", ex.Message);
    }

    [Fact]
    public void Validar_DescartadaConMotivo_NoLanza()
    {
        ReglasEstado.Validar(Enviada, Descartada, MotivoDescarte.Ingles);
    }

    [Fact]
    public void Validar_Retroceso_LanzaReglaNegocioConMensajeEnEspanol()
    {
        var ex = Assert.Throws<ReglaNegocioException>(() => ReglasEstado.Validar(Entrevista, Preparada, null));
        Assert.Equal("No se puede volver de «Entrevista» a «Preparada»: los estados solo avanzan.", ex.Message);
    }

    [Fact]
    public void Validar_EstadoOMotivoQueNoExisten_LanzaValidacion()
    {
        Assert.Throws<ValidacionException>(() => ReglasEstado.Validar(Detectada, (EstadoCandidatura)99, null));
        Assert.Throws<ValidacionException>(() => ReglasEstado.Validar(Detectada, Descartada, (MotivoDescarte)42));
    }

    [Theory]
    // Invierno (UTC+1): las 09:00 de Madrid son las 08:00 UTC.
    [InlineData("2026-11-10T15:00:00Z", "2026-11-17T08:00:00Z")]
    // Verano (UTC+2): las 09:00 de Madrid son las 07:00 UTC.
    [InlineData("2026-06-10T10:00:00Z", "2026-06-17T07:00:00Z")]
    // 23:30 UTC ya es el día siguiente en Madrid: cuenta el día de Madrid.
    [InlineData("2026-11-10T23:30:00Z", "2026-11-18T08:00:00Z")]
    [InlineData("2026-06-10T22:30:00Z", "2026-06-18T07:00:00Z")]
    // Envío en verano y seguimiento ya en invierno (cambio de hora el 25/10/2026).
    [InlineData("2026-10-20T10:00:00Z", "2026-10-27T08:00:00Z")]
    public void FechaSeguimiento_CualquierEnvio_DevuelveLas9DeMadridSieteDiasDespuesEnUtc(string envio, string esperado)
    {
        var envioUtc = DateTime.Parse(envio, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        var esperadoUtc = DateTime.Parse(esperado, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

        var seguimiento = ReglasEstado.FechaSeguimiento(envioUtc);

        Assert.Equal(esperadoUtc, seguimiento);
        Assert.Equal(DateTimeKind.Utc, seguimiento.Kind);
        Assert.Equal(new TimeSpan(9, 0, 0), Fechas.UtcAMadrid(seguimiento).TimeOfDay);
    }

    [Fact]
    public void ZonaMadrid_SeResuelveEnEsteSistema_ConHorarioDeInviernoYDeVerano()
    {
        Assert.Equal(TimeSpan.FromHours(1), Fechas.Madrid.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromHours(2), Fechas.Madrid.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void EstadosPosibles_DesdeEnviada_SoloPosterioresYFinales()
    {
        Assert.Equal([Entrevista, Oferta, Descartada, Cerrada], ReglasEstado.EstadosPosibles(Enviada));
    }

    [Fact]
    public void EstadosPosibles_DesdeDescartada_ReabrirOCerrar()
    {
        Assert.Equal([Detectada, Cerrada], ReglasEstado.EstadosPosibles(Descartada));
    }
}
