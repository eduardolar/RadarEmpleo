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

    // ---------------------------------------------------------------- página de Estadísticas

    [Fact]
    public void PorEstado_LosSieteEnElOrdenDelTablero_ConCeros()
    {
        var porEstado = CalculadoraEstadisticas.PorEstado(
            [EstadoCandidatura.Enviada, EstadoCandidatura.Descartada, EstadoCandidatura.Enviada]);

        Assert.Equal(Enum.GetValues<EstadoCandidatura>(), porEstado.Select(e => e.Estado));
        Assert.Equal([0, 0, 2, 0, 0, 1, 0], porEstado.Select(e => e.Total));
    }

    [Fact]
    public void EnviadasPorSemana_OchoSemanasHastaLaActual_EnHoraDeMadrid()
    {
        DateTime[] envios =
        [
            new(2026, 11, 8, 23, 30, 0, DateTimeKind.Utc), // lunes 9 a las 00:30 de Madrid: semana actual
            new(2026, 11, 8, 22, 30, 0, DateTimeKind.Utc), // domingo 8 a las 23:30: la anterior
            new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc),
            new(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc), // lunes 21/9: la primera de las ocho
            new(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc), // domingo 20/9: fuera
            new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc) // la semana que viene: fuera
        ];

        var semanas = CalculadoraEstadisticas.EnviadasPorSemana(new DateOnly(2026, 11, 13), envios);

        Assert.Equal(8, semanas.Count);
        Assert.Equal(new DateOnly(2026, 9, 21), semanas[0].Lunes);
        Assert.Equal(new DateOnly(2026, 11, 9), semanas[^1].Lunes);
        Assert.Equal([1, 0, 0, 0, 0, 0, 1, 2], semanas.Select(s => s.Total));
    }

    private static DateTime Dia(int mes, int dia, int hora = 9) => new(2026, mes, dia, hora, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DiasHastaRespuesta_DelUltimoEnvioAlPrimerPasoAEntrevistaUOferta()
    {
        (EstadoCandidatura, DateTime)[] historial =
        [
            (EstadoCandidatura.Detectada, Dia(10, 1)),
            (EstadoCandidatura.Enviada, Dia(10, 2)),
            (EstadoCandidatura.Descartada, Dia(10, 5)),
            (EstadoCandidatura.Enviada, Dia(10, 10)), // reabierta y reenviada: cuenta desde aquí
            (EstadoCandidatura.Entrevista, Dia(10, 19)),
            (EstadoCandidatura.Oferta, Dia(10, 30)) // solo cuenta el primer paso
        ];

        Assert.Equal(9, CalculadoraEstadisticas.DiasHastaRespuesta(historial));
    }

    [Fact]
    public void DiasHastaRespuesta_CuentaDiasDeMadridNoHorasDe24()
    {
        (EstadoCandidatura, DateTime)[] historial =
        [
            (EstadoCandidatura.Enviada, Dia(10, 2, 21)), // viernes 2 a las 23:00 de Madrid
            (EstadoCandidatura.Entrevista, Dia(10, 2, 22)) // sábado 3 a las 00:00 de Madrid
        ];

        Assert.Equal(1, CalculadoraEstadisticas.DiasHastaRespuesta(historial));
    }

    [Fact]
    public void DiasHastaRespuesta_SinEnvioAntesOSinRespuesta_EsNull()
    {
        Assert.Null(CalculadoraEstadisticas.DiasHastaRespuesta(
            [(EstadoCandidatura.Detectada, Dia(10, 1)), (EstadoCandidatura.Entrevista, Dia(10, 1))]));
        Assert.Null(CalculadoraEstadisticas.DiasHastaRespuesta(
            [(EstadoCandidatura.Entrevista, Dia(10, 1)), (EstadoCandidatura.Enviada, Dia(10, 2))]));
        Assert.Null(CalculadoraEstadisticas.DiasHastaRespuesta(
            [(EstadoCandidatura.Enviada, Dia(10, 1)), (EstadoCandidatura.Descartada, Dia(10, 9))]));
        Assert.Null(CalculadoraEstadisticas.DiasHastaRespuesta([]));
    }

    [Fact]
    public void Respuesta_MediaConUnDecimal_YSinCasosEsNull()
    {
        Assert.Equal(new RespuestaDto(8, 3, 38, 6.3, 3), CalculadoraEstadisticas.Respuesta(8, 3, [5, 6, 8]));
        Assert.Equal(new RespuestaDto(0, 0, null, null, 0), CalculadoraEstadisticas.Respuesta(0, 0, []));
    }

    [Fact]
    public void MotivosDescarte_DeMasAMenos_YAIgualdadEnElOrdenDelEnum()
    {
        var motivos = CalculadoraEstadisticas.MotivosDescarte(
        [
            MotivoDescarte.Zona, MotivoDescarte.Experiencia, MotivoDescarte.Ingles,
            MotivoDescarte.Zona, MotivoDescarte.Ingles, MotivoDescarte.Zona
        ]);

        Assert.Equal(
            [new(MotivoDescarte.Zona, 3), new(MotivoDescarte.Ingles, 2), new TotalPorMotivoDto(MotivoDescarte.Experiencia, 1)],
            motivos);
    }
}
