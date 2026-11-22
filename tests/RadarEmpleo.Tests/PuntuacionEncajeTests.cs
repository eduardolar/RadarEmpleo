using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary><see cref="PuntuacionEncaje"/>: aciertos exactos, a un nivel, lejos, sin respuesta y la matriz de confusión.</summary>
public class PuntuacionEncajeTests
{
    [Theory]
    [InlineData(Encaje.Alto, Encaje.Alto, 0)]
    [InlineData(Encaje.Alto, Encaje.Medio, 1)]
    [InlineData(Encaje.TiroLargo, Encaje.Medio, 1)]
    [InlineData(Encaje.Alto, Encaje.TiroLargo, 2)]
    [InlineData(Encaje.No, Encaje.Alto, 3)]
    public void Distancia_CuentaLosNivelesEntreLosDos(Encaje a, Encaje b, int esperada)
    {
        Assert.Equal(esperada, PuntuacionEncaje.Distancia(a, b));
        Assert.Equal(esperada, PuntuacionEncaje.Distancia(b, a));
    }

    [Fact]
    public void Indice_SinValorar_NoEsUnNivel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PuntuacionEncaje.Indice(Encaje.SinValorar));
    }

    [Fact]
    public void Calcular_SinCasos_TodoACero()
    {
        var r = PuntuacionEncaje.Calcular([]);

        Assert.Equal((0, 0, 0, 0, 0), (r.Total, r.Exactos, r.AUnNivel, r.Lejos, r.SinRespuesta));
        Assert.All(r.Matriz.Cast<int>(), n => Assert.Equal(0, n));
    }

    [Fact]
    public void Calcular_SeparaExactosAUnNivelLejosYSinRespuesta()
    {
        var r = PuntuacionEncaje.Calcular(
        [
            new(Encaje.Alto, Encaje.Alto),
            new(Encaje.No, Encaje.No),
            new(Encaje.Medio, Encaje.Alto),
            new(Encaje.TiroLargo, Encaje.No),
            new(Encaje.Alto, Encaje.TiroLargo),
            new(Encaje.No, Encaje.Alto),
            new(Encaje.Medio, null)
        ]);

        Assert.Equal(7, r.Total);
        Assert.Equal(2, r.Exactos);
        Assert.Equal(2, r.AUnNivel);
        Assert.Equal(2, r.Lejos);
        Assert.Equal(1, r.SinRespuesta);
    }

    [Fact]
    public void Calcular_MatrizConElEsperadoEnFilasYElObtenidoEnColumnas()
    {
        var r = PuntuacionEncaje.Calcular(
        [
            new(Encaje.Medio, Encaje.Alto),
            new(Encaje.Medio, Encaje.Alto),
            new(Encaje.No, Encaje.TiroLargo),
            new(Encaje.TiroLargo, Encaje.TiroLargo),
            new(Encaje.Alto, null)
        ]);

        var esperada = new int[4, 4];
        esperada[1, 0] = 2;
        esperada[3, 2] = 1;
        esperada[2, 2] = 1;
        Assert.Equal(esperada, r.Matriz);
        // Los que no tienen respuesta no entran en la matriz.
        Assert.Equal(r.Total - r.SinRespuesta, r.Matriz.Cast<int>().Sum());
    }

    [Fact]
    public void Calcular_EsperadoSinValorar_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PuntuacionEncaje.Calcular([new(Encaje.SinValorar, Encaje.Alto)]));
    }
}
