using RadarEmpleo.Services.Importacion;
using RadarEmpleo.Services.Rastreos;

namespace RadarEmpleo.Tests;

/// <summary><see cref="PartidorTextoLibre"/> con textos inventados: por apartados, por párrafos y por líneas, sin partir nunca una línea.</summary>
public class PartidorTextoLibreTests
{
    /// <summary>Las líneas numeradas desde <paramref name="desde"/>, seguidas.</summary>
    private static TextoLibreRegistro Texto(int desde, params string[] lineas) =>
        new(lineas.Select((l, i) => new LineaRegistro(desde + i, l)).ToList());

    private static TextoLibreRegistro Texto(params string[] lineas) => Texto(1, lineas);

    /// <summary>Lo que miden las líneas con esos números.</summary>
    private static int Medida(TextoLibreRegistro texto, params int[] numeros) =>
        texto.Lineas.Where(l => numeros.Contains(l.Numero)).Sum(PartidorTextoLibre.Medir);

    private static int[] Numeros(TrozoTextoLibre trozo) => trozo.Lineas.Select(l => l.Numero).ToArray();

    [Fact]
    public void Medir_EsElLargoEnElPromptConSuNumeroMasElSalto()
    {
        // «1│ abc» y el salto.
        Assert.Equal(7, PartidorTextoLibre.Medir(new LineaRegistro(1, "abc")));
        Assert.Equal(9, PartidorTextoLibre.Medir(new LineaRegistro(123, "abc")));
        // Sin los invisibles, como va al prompt.
        Assert.Equal(7, PartidorTextoLibre.Medir(new LineaRegistro(1, "a\u200Bbc")));
        // Una en blanco va con su número: «1│» (el espacio del final se recorta) y el salto.
        Assert.Equal(3, PartidorTextoLibre.Medir(new LineaRegistro(1, "")));
    }

    [Fact]
    public void TextoNumerado_CadaLineaConSuNumeroDeVerdad()
    {
        var trozo = new TrozoTextoLibre([new LineaRegistro(3, "## Agosto"), new LineaRegistro(40, "- Acme: enviada."), new LineaRegistro(41, "")],
            "## Agosto");

        Assert.Equal("3│ ## Agosto\n40│ - Acme: enviada.\n41│ ", trozo.TextoNumerado);
    }

    [Fact]
    public void TextoNumerado_UnSaltoDentroDeUnaLinea_NoPareceOtraLineaSinNumero()
    {
        var trozo = new TrozoTextoLibre([new LineaRegistro(7, "- Acme\u2028- Beta: enviada\u0085.")], null);

        Assert.Equal("7│ - Acme - Beta: enviada .", trozo.TextoNumerado);
    }

    [Fact]
    public void ElTituloRepetido_LlevaSuNumeroDeVerdad()
    {
        var texto = Texto(5, "## Agosto", "- " + new string('a', 30), "", "- " + new string('b', 30));

        var trozos = PartidorTextoLibre.Partir(texto, Medida(texto, 5, 6, 7) + 2);

        Assert.Equal(2, trozos.Count);
        Assert.StartsWith("5│ ## Agosto\n8│ - bbb", trozos[1].TextoNumerado);
    }

    [Fact]
    public void TextoVacio_NoDaNingunTrozo()
    {
        Assert.Empty(PartidorTextoLibre.Partir(Texto(), 100));
        Assert.Empty(PartidorTextoLibre.Partir(Texto("", "   ", "\u200B"), 100));
    }

    [Fact]
    public void LargoCeroONegativo_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PartidorTextoLibre.Partir(Texto("a"), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PartidorTextoLibre.Partir(Texto("a"), -1));
    }

    [Fact]
    public void SiCabeTodo_DaUnSoloTrozoConTodasLasLineas()
    {
        var texto = Texto("## Septiembre", "- Gamma: enviada.", "", "## Octubre", "- Delta: entrevista.");

        var trozo = Assert.Single(PartidorTextoLibre.Partir(texto, 16_000));

        Assert.Equal(texto.Lineas, trozo.Lineas);
        Assert.Equal("## Septiembre", trozo.Titulo);
        Assert.Equal(texto.Texto, trozo.Texto);
    }

    [Fact]
    public void ApartadosEnteros_SeJuntanHastaElLargo()
    {
        var texto = Texto(
            "## Uno", "- primera línea del uno", "",
            "## Dos", "- primera línea del dos", "",
            "## Tres", "- primera línea del tres");
        // Caben justo el uno y el dos.
        var largo = Medida(texto, 1, 2, 3, 4, 5, 6);

        var trozos = PartidorTextoLibre.Partir(texto, largo);

        Assert.Equal(2, trozos.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6], Numeros(trozos[0]));
        Assert.Equal("## Uno", trozos[0].Titulo);
        Assert.Equal([7, 8], Numeros(trozos[1]));
        Assert.Equal("## Tres", trozos[1].Titulo);
    }

    [Fact]
    public void LoDeAntesDelPrimerTitulo_EsUnApartadoSinTitulo()
    {
        var texto = Texto("Notas sueltas de antes", "- Acme: enviada.", "# Registro", "- Beta: enviada.");

        var trozos = PartidorTextoLibre.Partir(texto, Medida(texto, 1, 2));

        Assert.Equal(2, trozos.Count);
        Assert.Equal([1, 2], Numeros(trozos[0]));
        Assert.Null(trozos[0].Titulo);
        Assert.Equal([3, 4], Numeros(trozos[1]));
        Assert.Equal("# Registro", trozos[1].Titulo);
    }

    [Fact]
    public void LosTitulosDeTercerNivel_NoCortanElApartado()
    {
        var texto = Texto("## Agosto", "- Acme: enviada.", "### Semana 2", "- Beta: enviada.", "## Septiembre", "- Gamma: enviada.");

        // El de agosto cabe entero con su «###»; septiembre ya no.
        var trozos = PartidorTextoLibre.Partir(texto, Medida(texto, 1, 2, 3, 4));

        Assert.Equal(2, trozos.Count);
        Assert.Equal([1, 2, 3, 4], Numeros(trozos[0]));
        Assert.Equal([5, 6], Numeros(trozos[1]));
        Assert.Equal("## Septiembre", trozos[1].Titulo);
    }

    [Fact]
    public void UnaAlmohadillaSinEspacio_NoEsUnTitulo()
    {
        var texto = Texto("## Agosto", "- Acme: enviada.", "#etiqueta", "- Beta: enviada.");

        var trozo = Assert.Single(PartidorTextoLibre.Partir(texto, Medida(texto, 1, 2, 3, 4)));

        Assert.Equal([1, 2, 3, 4], Numeros(trozo));
    }

    [Fact]
    public void UnApartadoQueNoCabe_SeParteEnParrafosConSuTituloRepetidoDelante()
    {
        var texto = Texto(
            "## Agosto", "- Acme: enviada el 04/08.", "- Beta: enviada el 05/08.", "",
            "- Gamma: enviada el 12/08.", "- Delta: enviada el 13/08.", "",
            "- Epsilon: enviada el 20/08.",
            "## Septiembre", "- Zeta: enviada.");
        // Cabe el título con un párrafo, pero no con dos.
        var largo = Medida(texto, 1, 2, 3, 4) + 2;

        var trozos = PartidorTextoLibre.Partir(texto, largo);

        Assert.Equal(4, trozos.Count);
        Assert.Equal([1, 2, 3, 4], Numeros(trozos[0]));
        // El título repetido es la misma línea del registro, con su número.
        Assert.Equal([1, 5, 6, 7], Numeros(trozos[1]));
        Assert.Equal("## Agosto", trozos[1].Lineas[0].Texto);
        Assert.Equal("## Agosto", trozos[1].Titulo);
        Assert.Equal([1, 8], Numeros(trozos[2]));
        Assert.Equal("## Agosto", trozos[2].Titulo);
        // El siguiente apartado empieza en su propio título, sin el de agosto delante.
        Assert.Equal([9, 10], Numeros(trozos[3]));
        Assert.Equal("## Septiembre", trozos[3].Titulo);
    }

    [Fact]
    public void UnParrafoQueNoCabe_SeParteEnLineas()
    {
        var texto = Texto("## Agosto", "- Acme: enviada el 04/08.", "- Beta: enviada el 05/08.", "- Gamma: enviada el 12/08.");
        // Caben el título y dos líneas, pero no el párrafo entero.
        var largo = Medida(texto, 1, 2, 3);

        var trozos = PartidorTextoLibre.Partir(texto, largo);

        Assert.Equal(2, trozos.Count);
        Assert.Equal([1, 2, 3], Numeros(trozos[0]));
        Assert.Equal([1, 4], Numeros(trozos[1]));
    }

    [Fact]
    public void UnaLineaMasLargaQueElTrozo_VaSolaYEntera()
    {
        var larga = "- " + new string('x', 300);
        var texto = Texto("## Agosto", "- Acme: enviada.", larga, "- Beta: enviada.");

        var trozos = PartidorTextoLibre.Partir(texto, 100);

        Assert.Equal(3, trozos.Count);
        Assert.Equal([1, 2], Numeros(trozos[0]));
        // Sola con su título delante, sin partir.
        Assert.Equal([1, 3], Numeros(trozos[1]));
        Assert.Equal(larga, trozos[1].Lineas[1].Texto);
        Assert.Equal([1, 4], Numeros(trozos[2]));
    }

    [Fact]
    public void UnTitulo_VaConSuPrimeraLineaYNoSueltoAlFinalDelTrozoAnterior()
    {
        var texto = Texto("- Acme: enviada.", "## Agosto", "- " + new string('x', 200));

        var trozos = PartidorTextoLibre.Partir(texto, 50);

        Assert.Equal([[1], [2, 3]], trozos.Select(Numeros).ToArray());
    }

    [Fact]
    public void UnTituloConLineasEnBlancoDetras_TampocoQuedaSuelto()
    {
        var texto = Texto("- Acme: enviada.", "## Agosto", "", "  ", "- " + new string('x', 200), "", "- Beta: enviada.");

        var trozos = PartidorTextoLibre.Partir(texto, 50);

        // El título va con su primera línea con texto; nunca solo al final del trozo de antes.
        Assert.Equal([[1], [2, 3, 4, 5, 6], [2, 7]], trozos.Select(Numeros).ToArray());
    }

    [Fact]
    public void UnApartadoSinTituloQueNoCabe_SeParteSinRepetirNada()
    {
        var texto = Texto("- Acme: enviada.", "", "- Beta: enviada.", "", "- Gamma: enviada.");

        var trozos = PartidorTextoLibre.Partir(texto, Medida(texto, 1, 2) + 1);

        Assert.Equal([[1, 2], [3, 4], [5]], trozos.Select(Numeros).ToArray());
        Assert.All(trozos, t => Assert.Null(t.Titulo));
    }

    [Fact]
    public void LosNumerosDeLinea_SeConservanAunqueNoVayanSeguidos()
    {
        // Como sale de AnalizadorRegistro.TextoLibre: sin la tabla ni los rastreos, con huecos en la numeración.
        var lineas = new List<LineaRegistro>
        {
            new(3, "## Agosto"), new(4, "- Acme: enviada."), new(40, "## Septiembre"), new(41, "- Beta: enviada."), new(90, "- Gamma.")
        };
        var texto = new TextoLibreRegistro(lineas);

        var trozos = PartidorTextoLibre.Partir(texto, Medida(texto, 40, 41, 90));

        Assert.Equal([[3, 4], [40, 41, 90]], trozos.Select(Numeros).ToArray());
        Assert.Same(lineas[2], trozos[1].Lineas[0]);
    }

    [Fact]
    public void UnTrozoSoloDeLineasEnBlanco_NoSeDevuelve()
    {
        var texto = Texto("", "  ", "## Agosto", "- " + new string('x', 200));

        var trozos = PartidorTextoLibre.Partir(texto, 50);

        Assert.Equal([3, 4], Numeros(Assert.Single(trozos)));
    }

    [Fact]
    public void ElMismoCriterioDeTitulosQueLosRastreos()
    {
        // Lo que cierra una sección «## Rastreo …» es lo que abre un apartado.
        foreach (var linea in new[] { "# Uno", "## Dos", "##\tTres", "### Cuatro", "#Cinco", "texto" })
            Assert.Equal(AnalizadorRastreo.SeccionesRastreo(["## Rastreo 03/11", linea]).Single().Fin == 1,
                PartidorTextoLibre.Partir(Texto("- antes", linea), 1)[^1].Titulo is not null);
    }

    [Fact]
    public void UnTextoGrande_NingunTrozoPasaDelLargoYNoSePierdeNiSeParteNingunaLinea()
    {
        // Doce meses con párrafos de largo variable: el caso real, pero inventado.
        var lineas = new List<string>();
        for (var mes = 1; mes <= 12; mes++)
        {
            lineas.Add($"## Mes {mes}");
            for (var i = 0; i < 40; i++)
            {
                lineas.Add($"- Empresa {mes}-{i}: enviada el {i % 28 + 1:00}/{mes:00}. " + new string('n', i * 7 % 90));
                if (i % 5 == 4) lineas.Add("");
            }
        }
        var texto = Texto(lineas.ToArray());

        var trozos = PartidorTextoLibre.Partir(texto, 1_500);

        Assert.True(trozos.Count > 5);
        Assert.All(trozos, t => Assert.True(t.Lineas.Sum(PartidorTextoLibre.Medir) <= 1_500));
        // Quitando los títulos repetidos, salen todas las líneas, en su orden y tal cual.
        var juntas = trozos.SelectMany((t, i) =>
            i > 0 && t.Lineas[0].Numero <= trozos[i - 1].Lineas[^1].Numero ? t.Lineas.Skip(1) : t.Lineas).ToList();
        Assert.Equal(texto.Lineas, juntas);
        // Cada trozo que no empieza en su título lleva el de su apartado delante.
        Assert.All(trozos, t => Assert.StartsWith("## Mes ", t.Lineas[0].Texto));
    }
}
