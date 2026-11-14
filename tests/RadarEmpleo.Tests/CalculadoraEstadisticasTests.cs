using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Estadisticas;

namespace RadarEmpleo.Tests;

public class CalculadoraEstadisticasTests
{
    [Theory]
    [InlineData(EstadoCandidatura.Detectada, true)]
    [InlineData(EstadoCandidatura.Preparada, true)]
    [InlineData(EstadoCandidatura.Enviada, true)]
    [InlineData(EstadoCandidatura.Entrevista, true)]
    [InlineData(EstadoCandidatura.Oferta, true)]
    [InlineData(EstadoCandidatura.Descartada, false)]
    [InlineData(EstadoCandidatura.Cerrada, false)]
    public void EsActiva_TodasMenosDescartadaYCerrada(EstadoCandidatura estado, bool esperado) =>
        Assert.Equal(esperado, CalculadoraEstadisticas.EsActiva(estado));

    [Theory]
    [InlineData(EstadoCandidatura.Entrevista, true)]
    [InlineData(EstadoCandidatura.Oferta, true)]
    [InlineData(EstadoCandidatura.Enviada, false)]
    [InlineData(EstadoCandidatura.Descartada, false)]
    public void EsRespuesta_SoloEntrevistaYOferta(EstadoCandidatura estado, bool esperado) =>
        Assert.Equal(esperado, CalculadoraEstadisticas.EsRespuesta(estado));

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(4, 0, 0)]
    [InlineData(4, 1, 25)]
    [InlineData(3, 1, 33)]
    [InlineData(3, 2, 67)]
    [InlineData(8, 1, 13)] // 12,5 sube a 13: no se redondea «al par»
    [InlineData(5, 5, 100)]
    public void TasaRespuesta_RedondeaAEntero_YSinEnviadasEsNull(int enviadas, int conRespuesta, int? porcentaje) =>
        Assert.Equal(new TasaRespuestaDto(enviadas, conRespuesta, porcentaje), CalculadoraEstadisticas.TasaRespuesta(enviadas, conRespuesta));

    [Theory]
    [InlineData("2026-11-09", "2026-11-09")] // lunes
    [InlineData("2026-11-13", "2026-11-09")] // viernes
    [InlineData("2026-11-15", "2026-11-09")] // domingo: sigue siendo la semana del 9
    [InlineData("2026-12-02", "2026-11-30")] // cambio de mes
    [InlineData("2027-01-01", "2026-12-28")] // cambio de año
    public void LunesDe_LaSemanaVaDeLunesADomingo(string dia, string lunes) =>
        Assert.Equal(DateOnly.Parse(lunes), CalculadoraEstadisticas.LunesDe(DateOnly.Parse(dia)));

    [Fact]
    public void Semana_SieteDiasDesdeElLunes_ConLosInstantesContadosEnSuDiaDeMadrid()
    {
        DateTime[] fechas =
        [
            new(2026, 11, 8, 23, 0, 0, DateTimeKind.Utc), // lunes 9 a las 00:00 de Madrid
            new(2026, 11, 13, 8, 0, 0, DateTimeKind.Utc),
            new(2026, 11, 13, 22, 59, 0, DateTimeKind.Utc), // viernes 13 a las 23:59
            new(2026, 11, 13, 23, 0, 0, DateTimeKind.Utc), // sábado 14 a las 00:00
            new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc) // otra semana: no cuenta
        ];

        var semana = CalculadoraEstadisticas.Semana(new DateOnly(2026, 11, 13), fechas);

        Assert.Equal(Enumerable.Range(9, 7).Select(dia => new DateOnly(2026, 11, dia)), semana.Select(d => d.Dia));
        Assert.Equal([1, 0, 0, 0, 2, 1, 0], semana.Select(d => d.Recordatorios));
    }
}
