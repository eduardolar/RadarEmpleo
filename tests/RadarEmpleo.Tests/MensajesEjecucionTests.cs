using RadarEmpleo.Services.Agentes;

namespace RadarEmpleo.Tests;

/// <summary><see cref="MensajesEjecucion"/>: se habla de «uso estimado», nunca de dólares (decisión del 14/11/2026).</summary>
public class MensajesEjecucionTests
{
    public static TheoryData<FalloClaude> Fallos => [.. Enum.GetValues<FalloClaude>()];

    [Theory]
    [MemberData(nameof(Fallos))]
    public void De_NingunFallo_HablaDeDolares(FalloClaude fallo)
    {
        var mensaje = MensajesEjecucion.De(fallo, TimeSpan.FromMinutes(5));

        Assert.DoesNotContain("USD", mensaje);
        Assert.DoesNotContain("$", mensaje);
        Assert.DoesNotContain("gasto", mensaje);
    }

    [Fact]
    public void De_PresupuestoAgotado_DiceElTopeDeUsoDeLaTarea()
    {
        Assert.Equal("Ha llegado al tope de uso de esta tarea y la he parado.",
            MensajesEjecucion.De(FalloClaude.PresupuestoAgotado, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void TopeDiario_HablaDeUsoEstimado()
    {
        Assert.Equal("Has llegado al tope de uso estimado de hoy. Mañana se puede volver a lanzar.", MensajesEjecucion.TopeDiario);
    }

    [Theory]
    [InlineData(5 * 60, "Ha tardado más de 5 min y la he parado.")]
    [InlineData(90, "Ha tardado más de 1 min 30 s y la he parado.")]
    [InlineData(30 * 60, "Ha tardado más de 30 min y la he parado.")]
    public void De_TiempoAgotado_DiceElTiempoMaximo(int segundos, string esperado)
    {
        Assert.Equal(esperado, MensajesEjecucion.De(FalloClaude.TiempoAgotado, TimeSpan.FromSeconds(segundos)));
    }
}
