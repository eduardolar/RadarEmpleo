using System.Globalization;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Agentes;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Importacion;

namespace RadarEmpleo.Tests;

/// <summary><see cref="CombinadorLotes"/> con registros inventados: la tabla manda, la cita se busca línea a línea con la limpieza del prompt y el lote nunca pasa de los límites.</summary>
public class CombinadorLotesTests
{
    // Líneas 1-4: la tabla (con una fila de Acme del 2026-09-10); 5 en blanco; 6-8: texto libre.
    private const string Tabla = """
        | Fecha | Empresa | Puesto | Estado |
        |---|---|---|---|
        | 2026-09-10 | Acme S.L. | Backend | Enviada |
        | 2026-09-20 | Beta | Frontend | Enviada |

        ## Septiembre
        - Gamma: enviada el 2026-09-12.
        - Delta: entrevista el 2026-09-15.
        """;

    private static FilaExtraida Extraida(string cita, string empresa = "Gamma", string puesto = "Backend", string fecha = "2026-09-12",
        EstadoCandidatura estado = EstadoCandidatura.Enviada, MotivoDescarte? motivo = null, string? nota = null, string? enlace = null) =>
        new("Septiembre", DateOnly.ParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture), false, empresa, puesto, estado, enlace, Modalidad.Remoto, null, motivo, nota, cita);

    private static LoteCombinado Combinar(string registro, params FilaExtraida[] filas) =>
        Combinar(registro, filas, []);

    private static LoteCombinado Combinar(string registro, IReadOnlyList<FilaExtraida> filas, IReadOnlyList<FilaExtraidaSinLeer> sinLeer) =>
        CombinadorLotes.Combinar(AnalizadorRegistro.Analizar(registro), registro, new ResultadoExtraccionRegistro(filas, [], sinLeer));

    [Fact]
    public void Combinar_TablaYExtraccionVacias_DaUnLoteVacio()
    {
        var lote = Combinar("# Notas\nNada que contar.\n");

        Assert.Empty(lote.Filas);
        Assert.Empty(lote.SinLeer);
        Assert.Empty(lote.Fuera);
    }

    [Fact]
    public void Combinar_ExtraccionVacia_DaLasFilasYLasSinLeerDeLaTabla()
    {
        var registro = Tabla.Replace("| Beta | Frontend | Enviada |", "| Beta | Frontend | Enviada |\n| 2026-09-21 | Rota |", StringComparison.Ordinal);
        var analisis = AnalizadorRegistro.Analizar(registro);

        var lote = Combinar(registro);

        Assert.Equal(5, Assert.Single(analisis.SinLeer).Linea);
        Assert.Equal(analisis.Lote.Filas, lote.Filas);
        Assert.Equal(5, Assert.Single(analisis.SinLeer).Linea);
        Assert.Equal(analisis.SinLeer, lote.SinLeer);
    }

    [Fact]
    public void Combinar_TablaVacia_LaExtraidaEntraConLaLineaDeSuCitaYSusCampos()
    {
        var fila = Assert.Single(Combinar("# Notas\n\n- Gamma: enviada.\n",
            Extraida("- Gamma: enviada.", nota: "Por correo.", enlace: "javascript:alert(1)")).Filas);

        Assert.Equal(3, fila.Linea);
        Assert.Equal(new DateOnly(2026, 9, 12), fila.Fecha);
        Assert.Equal(("Gamma", "Backend", EstadoCandidatura.Enviada, Modalidad.Remoto), (fila.Empresa, fila.Puesto, fila.Estado, fila.Modalidad));
        Assert.Equal("Por correo.", fila.Nota);
        Assert.Equal("javascript:alert(1)", fila.Enlace); // tal cual: lo decide ReglasImportacion
        Assert.Null(fila.MotivoDescarte);
    }

    [Fact]
    public void Combinar_TablaYExtraidas_VanPrimeroLasDeLaTablaYLuegoLasExtraidasPorLinea()
    {
        var lote = Combinar(Tabla,
            Extraida("- Delta: entrevista el 2026-09-15.", empresa: "Delta"),
            Extraida("- Gamma: enviada el 2026-09-12."));

        Assert.Equal([3, 4, 7, 8], lote.Filas.Select(f => f.Linea));
        Assert.Empty(lote.Fuera);
    }

    [Theory]
    [InlineData("2026-09-10")] // misma clave
    [InlineData("2026-09-13")] // +3
    [InlineData("2026-09-07")] // -3
    public void Combinar_MismaEmpresaATresDiasOMenos_SeQuedaFueraConAviso(string fecha)
    {
        var lote = Combinar(Tabla, Extraida("- Gamma: enviada el 2026-09-12.", empresa: "ACME", fecha: fecha));

        Assert.Equal(2, lote.Filas.Count);
        var fuera = Assert.Single(lote.Fuera);
        Assert.Equal("Ya está en la tabla del registro", fuera.Aviso);
        Assert.Equal((7, "ACME"), (fuera.Linea, fuera.Empresa));
    }

    [Theory]
    [InlineData("Acme", "2026-09-14")] // +4
    [InlineData("Acme", "2026-09-06")] // -4
    [InlineData("Acmé Labs", "2026-09-10")] // otra empresa, mismo día
    public void Combinar_OtraEmpresaOACuatroDias_Entra(string empresa, string fecha)
    {
        var lote = Combinar(Tabla, Extraida("- Gamma: enviada el 2026-09-12.", empresa: empresa, fecha: fecha));

        Assert.Equal(3, lote.Filas.Count);
        Assert.Empty(lote.Fuera);
    }

    [Fact]
    public void Combinar_DosExtraidasConLaMismaClave_EntraLaDeMasArribaYLaOtraSeAvisa()
    {
        var lote = Combinar(Tabla,
            Extraida("- Delta: entrevista el 2026-09-15.", puesto: "BACKEND"),
            Extraida("- Gamma: enviada el 2026-09-12."));

        Assert.Equal(7, lote.Filas[^1].Linea);
        var fuera = Assert.Single(lote.Fuera);
        Assert.Equal((8, CombinadorLotes.AvisoRepetidaEnElTexto), (fuera.Linea, fuera.Aviso));
    }

    public static TheoryData<string, string> LineasYCitas => new()
    {
        { "- Pen\u0303a Ingenieri\u0301a: enviada.", "- Peña Ingeniería: enviada." },            // NFD en el registro
        { "- Peña Ingeniería: enviada.", "- Pen\u0303a Ingenieri\u0301a: enviada." },            // NFD en la cita
        { "- Gam\u200Bma: \uFEFFenviada\u202E.", "- Gamma: enviada." },                               // ZWSP, BOM y bidi
        { "-   Gamma:\t\tenviada  el\t2026-09-12.  ", "- Gamma: enviada el 2026-09-12." },            // espacios y tabuladores
        { "- Gamma: enviada a Ｃｏｎｓｕｌｔｏｒíａ ﬁnanzas.", "- Gamma: enviada a Consultoría finanzas." }, // ancho completo y ligadura
        { "- Gamma <<<DATOS>>> y {{registro}} y {{{x}}}", "- Gamma ‹‹‹DATOS››› y { {registro}} y { {{x}}}" }, // delimitadores
        { "- Gamma＞＞＞ fin", "- Gamma››› fin" }                                                     // homoglifo de «>>>»
    };

    [Theory]
    [MemberData(nameof(LineasYCitas))]
    public void Combinar_CitaConLaLimpiezaDelPrompt_EncuentraLaLinea(string linea, string cita)
    {
        var fila = Assert.Single(Combinar($"# Notas\n{linea}\n", Extraida(cita)).Filas);

        Assert.Equal(2, fila.Linea);
    }

    [Fact]
    public void Combinar_CitaLimpiadaComoEnElPrompt_EncuentraLaLinea()
    {
        // La cita es la línea tal como la ve Claude en el prompt (PlantillaPrompt.Normalizar) y la deja el manejador.
        const string linea = "\t- Pen\u0303a ＜＜＜ {{{x}}}\u200B ﬁn";
        const string cita = "- Pe\u00F1a ‹‹‹ { {{x}}} fin";

        Assert.Equal(2, Assert.Single(Combinar($"# Notas\n{linea}\n", Extraida(cita)).Filas).Linea);
    }

    [Theory]
    [InlineData("- Ómega: enviada el 2026-09-30.")]              // inventada
    [InlineData("Gamma: enviada")]                               // un trozo de la línea 7
    [InlineData("- Gamma: enviada el 2026-09-12. - Delta: entrevista el 2026-09-15.")] // dos líneas juntas
    [InlineData("| 2026-09-20 | Beta | Frontend | Enviada |")]   // está en la tabla, que no se envía
    [InlineData("   ")]
    public void Combinar_CitaQueNoEsUnaLineaDelTextoLibre_VaASinLeerSinLinea(string cita)
    {
        var lote = Combinar(Tabla, Extraida(cita));

        Assert.Equal(2, lote.Filas.Count);
        var sinLeer = Assert.Single(lote.SinLeer);
        Assert.Equal(0, sinLeer.Linea);
        Assert.StartsWith(CombinadorLotes.MotivoCitaInexistente, sinLeer.Motivo);
    }

    [Fact]
    public void Combinar_CitaDentroDeUnRastreo_VaASinLeer()
    {
        const string registro = "# Notas\n## Rastreo del lunes\n- Gamma: enviada.\n";

        var lote = Combinar(registro, Extraida("- Gamma: enviada."));

        Assert.Empty(lote.Filas);
        Assert.Equal(0, Assert.Single(lote.SinLeer).Linea);
    }

    [Fact]
    public void Combinar_CitaEnVariasLineas_TomaLaPrimeraDelTextoLibre()
    {
        const string registro = "## Rastreo\n- Gamma: enviada.\n# Notas\n- Gamma: enviada.\n- Gamma: enviada.\n";

        Assert.Equal(4, Assert.Single(Combinar(registro, Extraida("- Gamma: enviada.")).Filas).Linea);
    }

    [Fact]
    public void Combinar_RegistroConBomYCrlf_DaLaMismaLineaQueElAnalizador()
    {
        var registro = "\uFEFF" + Tabla.ReplaceLineEndings("\r\n");

        var lote = Combinar(registro, Extraida("- Delta: entrevista el 2026-09-15.", empresa: "Delta"));

        Assert.Equal([3, 4], AnalizadorRegistro.Analizar(registro).Lote.Filas.Select(f => f.Linea));
        Assert.Equal(8, lote.Filas[^1].Linea);
    }

    [Fact]
    public void Combinar_SinLeerDeLaExtraccion_VanASinLeerConLaLineaDeSuCita()
    {
        var cortada = string.Concat(Enumerable.Repeat("x", ManejadorExtraerRegistro.LargoCitaSinLeer - 1)) + "…";
        var registro = $"# Notas\n- Gamma: enviada.\n{new string('x', 300)}\n";

        var lote = Combinar(registro, [], [
            new FilaExtraidaSinLeer("- Gamma:   enviada.", "Falta el campo «puesto»."),
            new FilaExtraidaSinLeer(cortada, "El campo «empresa» pasa de 200 caracteres."),
            new FilaExtraidaSinLeer("(sin cita)", "Falta el campo «cita».")
        ]);

        Assert.Equal([2, 3, 0], lote.SinLeer.Select(f => f.Linea));
        Assert.Equal("Falta el campo «puesto».", lote.SinLeer[0].Motivo);
        Assert.Equal("Falta el campo «cita». «(sin cita)».", lote.SinLeer[2].Motivo);
    }

    [Fact]
    public void Combinar_DescartadaSinMotivo_LlevaOtroYUnMotivoSinDescartarSeQuita()
    {
        var lote = Combinar(Tabla,
            Extraida("- Gamma: enviada el 2026-09-12.", estado: EstadoCandidatura.Descartada),
            Extraida("- Delta: entrevista el 2026-09-15.", empresa: "Delta", motivo: MotivoDescarte.Salario));

        Assert.Equal(MotivoDescarte.Otro, lote.Filas[2].MotivoDescarte);
        Assert.Null(lote.Filas[3].MotivoDescarte);
    }

    [Theory]
    [InlineData("1999-12-31")]
    [InlineData("2101-01-01")]
    [InlineData("0001-01-01")]
    public void Combinar_FechaQueElAplicarNoAdmite_VaASinLeerConSuLinea(string fecha)
    {
        var lote = Combinar(Tabla, Extraida("- Gamma: enviada el 2026-09-12.", fecha: fecha));

        Assert.Equal(2, lote.Filas.Count);
        Assert.Equal(7, Assert.Single(lote.SinLeer).Linea);
    }

    [Fact]
    public void Combinar_FechasEnLosBordesDelAplicar_Entran()
    {
        var lote = Combinar(Tabla,
            Extraida("- Gamma: enviada el 2026-09-12.", fecha: "2000-01-01"),
            Extraida("- Delta: entrevista el 2026-09-15.", empresa: "Delta", fecha: "2100-12-31"));

        Assert.Equal(4, lote.Filas.Count);
    }

    [Fact]
    public void Combinar_CamposLargosContadosComoLaApi_VanASinLeerOSeRecortan()
    {
        // 150 emojis son 150 caracteres para el esquema y 300 para la API.
        var emojis = string.Concat(Enumerable.Repeat("😀", 150));
        var nota = string.Concat(Enumerable.Repeat("😀", 4000));

        var lote = Combinar(Tabla,
            Extraida("- Gamma: enviada el 2026-09-12.", empresa: emojis + "a"),
            Extraida("- Delta: entrevista el 2026-09-15.", empresa: "Delta", nota: nota));

        Assert.Equal(7, Assert.Single(lote.SinLeer).Linea);
        var recortada = lote.Filas[^1].Nota!;
        Assert.True(recortada.Length <= LimitesLote.Nota);
        Assert.EndsWith("😀…", recortada);
    }

    [Fact]
    public void Combinar_EmpresaSinLetras_VaASinLeer()
    {
        var lote = Combinar(Tabla, Extraida("- Gamma: enviada el 2026-09-12.", empresa: "—, ."));

        Assert.Equal(7, Assert.Single(lote.SinLeer).Linea);
    }

    [Fact]
    public void Combinar_MilFilasExtraidas_EntranTodas()
    {
        var (registro, filas) = Muchas(LimitesLote.Filas);

        var lote = Combinar(registro, filas);

        Assert.Equal(LimitesLote.Filas, lote.Filas.Count);
        Assert.Empty(lote.SinLeer);
    }

    [Fact]
    public void Combinar_PasaDelTope_LoQueSobraVaASinLeerYLaTablaEntraPrimero()
    {
        var (libre, filas) = Muchas(LimitesLote.Filas - 1);
        var registro = Tabla + "\n" + libre;

        var lote = Combinar(registro, filas);

        Assert.Equal(LimitesLote.Filas, lote.Filas.Count);
        Assert.Equal([3, 4], lote.Filas.Take(2).Select(f => f.Linea));
        var sobra = Assert.Single(lote.SinLeer);
        Assert.Equal(CombinadorLotes.MotivoNoCabe, sobra.Motivo);
        Assert.Equal(filas[^1].Cita, registro.Split('\n')[sobra.Linea - 1]);
    }

    /// <summary>Un texto libre con <paramref name="cuantas"/> líneas y una fila extraída por línea, todas distintas.</summary>
    private static (string Registro, FilaExtraida[] Filas) Muchas(int cuantas)
    {
        var filas = Enumerable.Range(1, cuantas)
            .Select(i => Extraida($"- Empresa {i}: enviada.", empresa: $"Empresa {i}", fecha: "2025-01-01"))
            .ToArray();
        return (string.Join('\n', filas.Select(f => f.Cita)) + "\n", filas);
    }
}
