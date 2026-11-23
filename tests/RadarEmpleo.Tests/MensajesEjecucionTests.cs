using RadarEmpleo.Domain;
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
    public void De_PresupuestoAgotado_DiceElTopeDeUsoDeLaTareaYQueReintentarPuedeGastarLoMismo()
    {
        Assert.Equal("Ha llegado al tope de uso de esta tarea y la he parado. Si la reintentas, puede volver a gastar lo mismo.",
            MensajesEjecucion.De(FalloClaude.PresupuestoAgotado, TimeSpan.FromMinutes(5)));
    }

    /// <summary>El front los pinta tras «No ha salido.»: cada uno dice qué ha pasado y qué hacer.</summary>
    [Theory]
    [InlineData(FalloClaude.ErrorDeClaude, "Claude ha devuelto un error. Prueba a reintentarla en un rato.")]
    [InlineData(FalloClaude.SalidaNoValida, "Claude ha respondido en un formato que no entiendo. Reintentarla suele bastar.")]
    [InlineData(FalloClaude.NoInstalado, "No encuentro Claude Code en este PC. Instálalo o abre la app en el PC donde lo tengas.")]
    public void De_DiceQueHacer(FalloClaude fallo, string esperado)
    {
        Assert.Equal(esperado, MensajesEjecucion.De(fallo, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Inesperado_DiceQueHacer()
    {
        Assert.Equal("Algo ha fallado al lanzar Claude. Vuelve a intentarlo y, si se repite, reinicia la app.", MensajesEjecucion.Inesperado);
    }

    [Fact]
    public void BloqueoDemasiadoTiempo_ConElNombreHumanoYSinHablarDeEjecuciones()
    {
        var mensaje = MensajesEjecucion.BloqueoDemasiadoTiempo(BloqueoEjecucion.NombreVisible(BloqueoEjecucion.QuienEmpleo));

        Assert.Equal("Ha esperado una hora y tu sesión de /empleo en Claude Code sigue usando Claude. Reintenta cuando termine.", mensaje);
        Assert.DoesNotContain("ejecución", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MensajeInterrumpida_HablaDeLaAppYNoDeLaApi()
    {
        Assert.Equal("Se cortó porque la app se cerró mientras trabajaba.", EjecucionAgente.MensajeInterrumpida);
        Assert.DoesNotContain("API", EjecucionAgente.MensajeInterrumpida);
    }

    [Fact]
    public void MensajeReiniciadaAntesDeEmpezar_HablaDeLaAppYDiceQueHacer()
    {
        Assert.Equal("La app se cerró antes de empezar. Vuelve a lanzarla.", EjecucionAgente.MensajeReiniciadaAntesDeEmpezar);
    }

    public static TheoryData<string> Fijos =>
    [
        MensajesEjecucion.NoInstalado, MensajesEjecucion.SalidaNoValida, MensajesEjecucion.ErrorDeClaude, MensajesEjecucion.Inesperado,
        MensajesEjecucion.SinManejador, MensajesEjecucion.Cancelada, MensajesEjecucion.OtraCopiaLlevaLaCola,
        MensajesEjecucion.ColaSinArrancar, MensajesEjecucion.TopeTarea, MensajesEjecucion.TopeDiario,
        MensajesEjecucion.BloqueoDemasiadoTiempo(BloqueoEjecucion.NombreOtroPrograma),
        EjecucionAgente.MensajeInterrumpida, EjecucionAgente.MensajeReiniciadaAntesDeEmpezar
    ];

    [Theory]
    [MemberData(nameof(Fijos))]
    public void NingunMensaje_LlevaRutasNiNombresDeTipos(string mensaje)
    {
        Assert.DoesNotContain("\\", mensaje);
        Assert.DoesNotContain("Exception", mensaje);
        Assert.DoesNotContain("{", mensaje);
        Assert.EndsWith(".", mensaje);
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
