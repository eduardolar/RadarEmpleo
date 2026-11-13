using RadarEmpleo.Domain;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Rastreos;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="AnalizadorRastreo"/> con textos inventados que copian los formatos reales del registro (las
/// viñetas desde el 03/11, las listas numeradas de antes, «Tiro largo:», «Plan B», «Bajo:», «ENVIADA»,
/// «Descartada por Eduardo»…) y con los datos de ejemplo.
/// </summary>
public class AnalizadorRastreoTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 8);

    private static OfertaLeida Oferta(string linea)
    {
        var leida = AnalizadorRastreo.LeerLinea(linea, 7);
        Assert.Equal(TipoLineaRastreo.Oferta, leida.Tipo);
        return leida.Oferta!;
    }

    private static TipoLineaRastreo Tipo(string linea) => AnalizadorRastreo.LeerLinea(linea, 1).Tipo;

    // ---------------------------------------------------------------- líneas de oferta

    [Fact]
    public void LeerLinea_VinetaConEncajeDelante_LeeTodosLosCampos()
    {
        const string linea = "- **Alto · Empresa Inventada — Operario/a de montaje, Ciudad Ejemplo** (temporal, 8 h). " +
                             "https://ejemplo.com/ofertas/1 — CV de operario. Pega: sin salario y temporal.";

        var oferta = Oferta(linea);

        Assert.Equal(7, oferta.Linea);
        Assert.Equal(linea, oferta.TextoOriginal);
        Assert.Equal("Empresa Inventada", oferta.Empresa);
        // El puesto va entero: cortarlo en la coma rompería «Python, .NET, Typescript developer».
        Assert.Equal("Operario/a de montaje, Ciudad Ejemplo", oferta.Puesto);
        Assert.Equal("https://ejemplo.com/ofertas/1", oferta.Enlace);
        Assert.Equal(Encaje.Alto, oferta.Encaje);
        Assert.Equal("sin salario y temporal.", oferta.Pega);
        Assert.False(oferta.Ignorada);
    }

    [Theory]
    [InlineData("1. **Acme — Programador/a Angular**, remoto. Pegas: mudanza. https://ejemplo.com/o/2")]
    [InlineData("12) **Acme — Programador/a Angular**, remoto. Pegas: mudanza. https://ejemplo.com/o/2")]
    [InlineData("* **Acme — Programador/a Angular**, remoto. Pegas: mudanza. https://ejemplo.com/o/2")]
    [InlineData("   - **Acme — Programador/a Angular**, remoto. Pegas: mudanza. https://ejemplo.com/o/2")]
    public void LeerLinea_ListaNumeradaUOtraVineta_EsUnaOferta(string linea)
    {
        var oferta = Oferta(linea);

        Assert.Equal("Acme", oferta.Empresa);
        Assert.Equal("Programador/a Angular", oferta.Puesto);
        Assert.Equal("mudanza.", oferta.Pega);
        Assert.Equal(Encaje.SinValorar, oferta.Encaje);
    }

    [Fact]
    public void LeerLinea_TiroLargoDelanteDeLaNegrita_EsTiroLargo()
    {
        var oferta = Oferta("6. Tiro largo: **Consultora Ejemplo — Consultor Tecnológico .NET**, híbrido. https://ejemplo.com/o/3");

        Assert.Equal(Encaje.TiroLargo, oferta.Encaje);
        Assert.Equal("Consultora Ejemplo", oferta.Empresa);
    }

    [Theory]
    [InlineData("Alto", Encaje.Alto)]
    [InlineData("Alto (sin verificar en origen)", Encaje.Alto)]
    [InlineData("Medio", Encaje.Medio)]
    [InlineData("Medio-Alto", Encaje.Medio)]
    [InlineData("Medio-Bajo", Encaje.TiroLargo)]
    [InlineData("Bajo", Encaje.TiroLargo)]
    [InlineData("Tiro largo", Encaje.TiroLargo)]
    [InlineData("Recomendada (no repetir)", Encaje.SinValorar)]
    public void LeerLinea_EtiquetaDeEncaje_SeTraduce(string etiqueta, Encaje esperado)
    {
        var oferta = Oferta($"- **{etiqueta} · Acme — Backend** https://ejemplo.com/o/4");

        Assert.Equal(esperado, oferta.Encaje);
        Assert.Equal("Acme", oferta.Empresa);
    }

    [Theory]
    [InlineData("- **Acme — Backend .NET** (remoto). https://ejemplo.com/o/5 · Encaje Medio-Alto. **CV_Backend.pdf**", Encaje.Medio)]
    [InlineData("- **Acme — Backend .NET** (remoto). https://ejemplo.com/o/5 · Tiro largo. **CV_Backend.pdf**", Encaje.TiroLargo)]
    public void LeerLinea_EncajeDetrasDelEnlace_TambienSeLee(string linea, Encaje esperado)
    {
        Assert.Equal(esperado, Oferta(linea).Encaje);
    }

    [Fact]
    public void LeerLinea_GuionCortoDentroDelPuesto_NoParteLaNegrita()
    {
        var oferta = Oferta("- **Medio · Empresa Ejemplo — Desarrollador/a .NET – C#** https://ejemplo.com/o/6");

        Assert.Equal("Empresa Ejemplo", oferta.Empresa);
        Assert.Equal("Desarrollador/a .NET – C#", oferta.Puesto);
    }

    [Fact]
    public void LeerLinea_DosEnlaces_ValeElPrimero()
    {
        var oferta = Oferta("- **Acme — Bolsas de talento** https://ejemplo.com/o/7 · https://ejemplo.com/o/8");

        Assert.Equal("https://ejemplo.com/o/7", oferta.Enlace);
    }

    [Theory]
    [InlineData("- **Acme — Backend** https://ejemplo.com/o/9.", "https://ejemplo.com/o/9")]
    [InlineData("- **Acme — Backend** (https://ejemplo.com/o/9)", "https://ejemplo.com/o/9")]
    [InlineData("- **Acme — Backend** [la oferta](https://ejemplo.com/o/9)", "https://ejemplo.com/o/9")]
    [InlineData("- **Acme — Backend** https://ejemplo.com/job-listing/x_KO0,71_KE72,109.htm?jl=1, publicada ayer",
        "https://ejemplo.com/job-listing/x_KO0,71_KE72,109.htm?jl=1")]
    public void LeerLinea_EnlaceConPuntuacionAlrededor_LaQuita(string linea, string esperado)
    {
        Assert.Equal(esperado, Oferta(linea).Enlace);
    }

    [Fact]
    public void LeerLinea_PegaAntesDelEnlace_LlegaHastaElEnlace()
    {
        var oferta = Oferta("- **Acme — Backend**, remoto. Pegas: pide 2 años y no tiene Spark. https://ejemplo.com/o/10 resto");

        Assert.Equal("pide 2 años y no tiene Spark.", oferta.Pega);
    }

    [Fact]
    public void LeerLinea_SinPegaEtiquetada_LaPegaEsNull()
    {
        Assert.Null(Oferta("- **Acme — Backend**, remoto, mucha competencia. https://ejemplo.com/o/11").Pega);
    }

    [Fact]
    public void LeerLinea_FechaDelanteDeLaEmpresa_LaQuita()
    {
        Assert.Equal("Empresa Ejemplo", Oferta("- **21/10 Empresa Ejemplo — Soporte TI** https://ejemplo.com/o/12").Empresa);
    }

    [Fact]
    public void LeerLinea_DescartadaPorEduardo_SaleIgnorada()
    {
        var oferta = Oferta("- **Descartada por Eduardo (03/11) · Grupo Ejemplo — Oficial de sala**, indefinido. https://ejemplo.com/o/13");

        Assert.True(oferta.Ignorada);
        Assert.Equal("Grupo Ejemplo", oferta.Empresa);
        Assert.Equal(Encaje.SinValorar, oferta.Encaje);
    }

    // ---------------------------------------------------------------- líneas que no son ofertas

    [Theory]
    [InlineData("- **Plan B si falla lo otro:** Empresa Uno https://ejemplo.com/o/14 · Empresa Dos https://ejemplo.com/o/15")]
    [InlineData("- **Bajo:** Empresa Uno (telefonía); Empresa Dos (almacén).")]
    [InlineData("- **Medio · Empresa Ejemplo — Dependiente/a**, temporal; no verificable.")]
    [InlineData("- Empresa Antigua — Junior SW Engineer, Madrid híbrido, sin negrita. Pega: grado.")]
    [InlineData("- NO VERIFICADA: un portal — «Full Stack Junior». https://ejemplo.com/o/16")]
    public void LeerLinea_PareceUnaOfertaPeroNoSePuedeLeer_SinLeer(string linea)
    {
        Assert.Equal(TipoLineaRastreo.SinLeer, Tipo(linea));
    }

    [Theory]
    [InlineData("- **ENVIADA el 03/11: Empresa Ejemplo — Operario/a de montaje**, por un portal, of-ia1b2c3.")]
    [InlineData("- **ENVIADA el 03/11: Empresa Ejemplo — Operario/a de montaje** https://ejemplo.com/o/17")]
    [InlineData("- **30/10 ENVIADA Empresa Ejemplo — Desarrollador/a Junior** (solicitud sencilla) con el CV.")]
    [InlineData("   - **CV preparado el 05/10: CV_Ejemplo.pdf** (con cambios). https://ejemplo.com/o/18 **ENVIADA el 05/10.**")]
    [InlineData("- **Empresa Ejemplo — Desarrollador .NET**, sin enlace. **ENVIADA el 06/11** por la web.")]
    public void LeerLinea_EnvioSinOferta_SeSaltaSinContarla(string linea)
    {
        Assert.Equal(TipoLineaRastreo.Otra, Tipo(linea));
    }

    [Theory]
    [InlineData("Recomendadas, de mejor a peor encaje:")]
    [InlineData("Muy poco nuevo. Única recomendable: **Acme — Soporte** https://ejemplo.com/o/19")]
    [InlineData("- Descartadas el 15/10: Acme (3 años) · Otra (inglés).")]
    [InlineData("- **03/11: Una empresa llama a Eduardo.** Entrevista el mismo día.")]
    [InlineData("")]
    public void LeerLinea_TextoNormal_EsOtra(string linea)
    {
        Assert.Equal(TipoLineaRastreo.Otra, Tipo(linea));
    }

    [Fact]
    public void LeerLinea_EmpresaVacia_SinLeer()
    {
        Assert.Equal(TipoLineaRastreo.SinLeer, Tipo("- **Alto · — Backend** https://ejemplo.com/o/20"));
    }

    [Fact]
    public void TextoPlano_QuitaLaVinetaYElMarkdown()
    {
        Assert.Equal("Alto · Acme — Backend (remoto). Ver la oferta (https://ejemplo.com/o/21)",
            AnalizadorRastreo.TextoPlano("- **Alto · Acme — Backend** (remoto). [Ver la oferta](https://ejemplo.com/o/21)"));
    }

    // ---------------------------------------------------------------- fechas

    [Theory]
    [InlineData("Rastreo 2026-09-04 — candidatas con pega", 2026, 9, 4)]
    [InlineData("Rastreo semanal 10/09 (en la nube)", 2026, 9, 10)]
    [InlineData("Rastreo 30/10 (viernes, lo publicado desde el 26/10)", 2026, 10, 30)]
    [InlineData("Rastreo semanal 24/09 (en la nube) — volcado aquí el 01/10", 2026, 9, 24)]
    [InlineData("Rastreo 5/1/2025 (ejemplo)", 2025, 1, 5)]
    [InlineData("Rastreo 05/01/25", 2025, 1, 5)]
    public void FechaDeTitulo_FormatosReales_LaLee(string titulo, int anio, int mes, int dia)
    {
        Assert.Equal(new DateOnly(anio, mes, dia), AnalizadorRastreo.FechaDeTitulo(titulo, Hoy));
    }

    [Theory]
    [InlineData("Rastreo de la semana")]
    [InlineData("Rastreo 31/02 (no existe)")]
    [InlineData("Rastreo 2026-13-01")]
    public void FechaDeTitulo_SinFechaValida_Null(string titulo)
    {
        Assert.Null(AnalizadorRastreo.FechaDeTitulo(titulo, Hoy));
    }

    [Fact]
    public void FechaConAnio_FechaPasadaDeEsteAnio_EsDeEsteAnio()
    {
        Assert.Equal(new DateOnly(2026, 11, 3), AnalizadorRastreo.FechaConAnio(3, 11, Hoy));
    }

    [Fact]
    public void FechaConAnio_HoyYManiana_SonDeEsteAnio()
    {
        Assert.Equal(new DateOnly(2026, 11, 8), AnalizadorRastreo.FechaConAnio(8, 11, Hoy));
        Assert.Equal(new DateOnly(2026, 11, 9), AnalizadorRastreo.FechaConAnio(9, 11, Hoy));
    }

    [Fact]
    public void FechaConAnio_MasDeUnDiaEnElFuturo_EsDelAnioAnterior()
    {
        Assert.Equal(new DateOnly(2025, 11, 10), AnalizadorRastreo.FechaConAnio(10, 11, Hoy));
    }

    [Fact]
    public void FechaConAnio_CambioDeAnio_ElDiciembreLeidoEnEneroEsDelAnioPasado()
    {
        var enero = new DateOnly(2027, 1, 2);

        Assert.Equal(new DateOnly(2026, 12, 28), AnalizadorRastreo.FechaConAnio(28, 12, enero));
        Assert.Equal(new DateOnly(2027, 1, 1), AnalizadorRastreo.FechaConAnio(1, 1, enero));
    }

    [Fact]
    public void FechaConAnio_29DeFebrero_SoloEnUnAnioBisiesto()
    {
        Assert.Equal(new DateOnly(2028, 2, 29), AnalizadorRastreo.FechaConAnio(29, 2, new DateOnly(2028, 3, 1)));
        Assert.Equal(new DateOnly(2028, 2, 29), AnalizadorRastreo.FechaConAnio(29, 2, new DateOnly(2029, 3, 1)));
        Assert.Null(AnalizadorRastreo.FechaConAnio(29, 2, new DateOnly(2027, 3, 1)));
    }

    // ---------------------------------------------------------------- secciones del registro

    private const string Registro = """
        # Registro inventado

        | Fecha | Empresa | Puesto | Estado |
        |---|---|---|---|
        | 2026-10-01 | Acme | Backend | Enviada |

        ## Descartadas en el rastreo del 04/09
        - **Acme — No es un rastreo** https://ejemplo.com/o/30

        ## Rastreo 30/10 (viernes, lo publicado desde el 26/10)
        Semana mala.
        - **Michael Ejemplo — Técnico ERP**, Madrid. Pega: casi presencial. https://ejemplo.com/o/31

        ### Detalle
        - **Plan B:** https://ejemplo.com/o/32

        ## Rastreo 30/10 (tarde, con el Chrome)
        - **Alto · ABM Ejemplo — Desarrollador/a Junior** https://ejemplo.com/o/33
        - **Medio · Prime Ejemplo — Junior .NET Developer** https://ejemplo.com/o/34

        ## Otra sección
        - **Acme — Tampoco** https://ejemplo.com/o/35
        """;

    [Fact]
    public void AnalizarRegistro_SoloLasSeccionesDeRastreoHastaElSiguienteEncabezado()
    {
        var analisis = AnalizadorRastreo.AnalizarRegistro(Registro, Hoy);

        Assert.Empty(analisis.Avisos);
        Assert.Equal(2, analisis.Rastreos.Count);
        var primero = analisis.Rastreos[0];
        Assert.Equal("Rastreo 30/10 (viernes, lo publicado desde el 26/10)", primero.Titulo);
        Assert.Equal(OrigenRastreo.Registro, primero.Origen);
        Assert.Equal(RutasCurriculum.ArchivoRegistro, primero.RutaInforme);
        Assert.Equal(new DateOnly(2026, 10, 30), primero.Fecha);
        // El «###» es parte de la sección: su «Plan B» cuenta como sin leer.
        Assert.Equal("https://ejemplo.com/o/31", Assert.Single(primero.Ofertas).Enlace);
        Assert.Equal(1, primero.LineasSinLeer);
        Assert.Equal(12, primero.Ofertas[0].Linea);
    }

    [Fact]
    public void AnalizarRegistro_DosSeccionesConLaMismaFecha_SonDosRastreosConClavesDistintas()
    {
        var rastreos = AnalizadorRastreo.AnalizarRegistro(Registro, Hoy).Rastreos;

        Assert.All(rastreos, r => Assert.Equal(new DateOnly(2026, 10, 30), r.Fecha));
        Assert.Equal("registro:2026-10-30:rastreo 30/10 (viernes, lo publicado desde el 26/10)", rastreos[0].Clave);
        Assert.Equal("registro:2026-10-30:rastreo 30/10 (tarde, con el chrome)", rastreos[1].Clave);
        Assert.Equal("registro:rastreo 30/10 (tarde, con el chrome)", rastreos[1].ClaveAnterior);
        Assert.Equal(2, rastreos[1].Ofertas.Count);
    }

    [Fact]
    public void AnalizarRegistro_MismoTituloDosVeces_LaSegundaLlevaUnNumero()
    {
        const string texto = "## Rastreo 03/11\n- **A — B** https://ejemplo.com/o/1\n## Rastreo 03/11\n- **C — D** https://ejemplo.com/o/2\n";

        var rastreos = AnalizadorRastreo.AnalizarRegistro(texto, Hoy).Rastreos;

        Assert.Equal(["registro:2026-11-03:rastreo 03/11", "registro:2026-11-03:rastreo 03/11#2"], rastreos.Select(r => r.Clave));
        Assert.Equal(["registro:rastreo 03/11", "registro:rastreo 03/11#2"], rastreos.Select(r => r.ClaveAnterior));
    }

    [Fact]
    public void AnalizarRegistro_ElAnioDeLaClaveSaleDeLaFechaDeReferencia()
    {
        const string texto = "## Rastreo 03/11\n- **A — B** https://ejemplo.com/o/1\n";

        var deEsteAnio = Assert.Single(AnalizadorRastreo.AnalizarRegistro(texto, new DateOnly(2026, 11, 8)).Rastreos);
        var delSiguiente = Assert.Single(AnalizadorRastreo.AnalizarRegistro(texto, new DateOnly(2027, 11, 8)).Rastreos);

        Assert.Equal("registro:2026-11-03:rastreo 03/11", deEsteAnio.Clave);
        Assert.Equal("registro:2027-11-03:rastreo 03/11", delSiguiente.Clave);
        Assert.Equal(deEsteAnio.ClaveAnterior, delSiguiente.ClaveAnterior);
    }

    [Fact]
    public void AnalizarRegistro_TextoSinTituloNiLineasEnBlancoDeLosBordes_YHashEstable()
    {
        const string texto = "## Rastreo 03/11 (martes)\n\nSemana mala.\n- **A — B** https://ejemplo.com/o/1\n\n\n## Otra\n";

        var rastreo = Assert.Single(AnalizadorRastreo.AnalizarRegistro(texto, Hoy).Rastreos);

        Assert.Equal("Semana mala.\n- **A — B** https://ejemplo.com/o/1", rastreo.Texto);
        Assert.Matches("^[0-9a-f]{64}$", rastreo.Hash);
        Assert.Equal(rastreo.Hash, AnalizadorRastreo.AnalizarRegistro(texto.Replace("\n", "\r\n"), Hoy).Rastreos[0].Hash);
    }

    [Fact]
    public void AnalizarRegistro_SeccionQueCrece_CambiaElHashYConservaLaClave()
    {
        const string antes = "## Rastreo 03/11 (martes)\n- **A — B** https://ejemplo.com/o/1\n";
        var despues = antes + "- **C — D** https://ejemplo.com/o/2\n";

        var uno = AnalizadorRastreo.AnalizarRegistro(antes, Hoy).Rastreos[0];
        var dos = AnalizadorRastreo.AnalizarRegistro(despues, Hoy).Rastreos[0];

        Assert.Equal(uno.Clave, dos.Clave);
        Assert.NotEqual(uno.Hash, dos.Hash);
        Assert.Equal(2, dos.Ofertas.Count);
    }

    [Fact]
    public void AnalizarRegistro_SeccionSinFecha_SeAvisaYNoSeLee()
    {
        var analisis = AnalizadorRastreo.AnalizarRegistro("## Rastreo de la semana\n- **A — B** https://ejemplo.com/o/1\n", Hoy);

        Assert.Empty(analisis.Rastreos);
        Assert.Equal("No entiendo la fecha del rastreo «Rastreo de la semana»: no lo he leído.", Assert.Single(analisis.Avisos));
    }

    [Fact]
    public void AnalizarRegistro_RegistroDeEjemplo_LeeSuRastreo()
    {
        var analisis = AnalizadorRastreo.AnalizarRegistro(File.ReadAllText(FabricaApi.RutaRegistroEjemplo).ReplaceLineEndings("\r\n"), Hoy);

        var rastreo = Assert.Single(analisis.Rastreos);
        Assert.Equal(new DateOnly(2026, 11, 3), rastreo.Fecha);
        Assert.Equal(
            ["Empresa Rastreo Uno", "Talleres Ficticios S.L.", "Empresa Rastreo Dos", "Empresa Rastreo Tres",
             "Empresa Rastreo Nueve", "Empresa Rastreo Diez"],
            rastreo.Ofertas.Select(o => o.Empresa));
        Assert.Equal(
            [Encaje.Alto, Encaje.Medio, Encaje.TiroLargo, Encaje.SinValorar, Encaje.SinValorar, Encaje.TiroLargo],
            rastreo.Ofertas.Select(o => o.Encaje));
        Assert.True(rastreo.Ofertas[3].Ignorada);
        Assert.Equal("Programador/a Python – IA", rastreo.Ofertas[2].Puesto);
        Assert.Equal("https://ejemplo.com/ofertas/2002", rastreo.Ofertas[2].Enlace);
        // «Medio · … Dependiente/a» sin enlace, «Bajo:» y «Plan B».
        Assert.Equal(3, rastreo.LineasSinLeer);
        Assert.DoesNotContain(rastreo.Texto, c => c == '\r');
    }

    // ---------------------------------------------------------------- informes de la tarea de los lunes

    [Fact]
    public void AnalizarInforme_InformeDeEjemplo_FechaDelNombreYTituloDelPrimerEncabezado()
    {
        var texto = File.ReadAllText(Path.Combine(FabricaApi.CarpetaDatosEjemplo, "rastreos", "rastreo_2026-11-09.md"));

        var analisis = AnalizadorRastreo.AnalizarInforme("rastreos/rastreo_2026-11-09.md", texto);

        Assert.Empty(analisis.Avisos);
        var rastreo = Assert.Single(analisis.Rastreos);
        Assert.Equal("rastreos/rastreo_2026-11-09.md", rastreo.Clave);
        Assert.Equal(OrigenRastreo.TareaLunes, rastreo.Origen);
        Assert.Equal(new DateOnly(2026, 11, 9), rastreo.Fecha);
        Assert.Equal("Rastreo del lunes 09/11 (ejemplo inventado)", rastreo.Titulo);
        Assert.DoesNotContain("# Rastreo", rastreo.Texto);
        Assert.Equal(3, rastreo.Ofertas.Count);
        Assert.Equal(Encaje.TiroLargo, rastreo.Ofertas[1].Encaje);
        Assert.Equal(6, rastreo.Ofertas[0].Linea);
    }

    [Fact]
    public void AnalizarInforme_SinEncabezado_TituloConLaFecha()
    {
        var rastreo = AnalizadorRastreo.AnalizarInforme("rastreos/rastreo_2026-11-16.md", "- **A — B** https://ejemplo.com/o/1").Rastreos[0];

        Assert.Equal("Rastreo del 16/11", rastreo.Titulo);
        Assert.Equal(1, rastreo.Ofertas[0].Linea);
    }

    [Theory]
    [InlineData("rastreos/rastreo_semana.md")]
    [InlineData("rastreos/rastreo_2026-02-30.md")]
    public void AnalizarInforme_NombreSinFecha_SeAvisaYNoSeLee(string ruta)
    {
        var analisis = AnalizadorRastreo.AnalizarInforme(ruta, "- **A — B** https://ejemplo.com/o/1");

        Assert.Empty(analisis.Rastreos);
        Assert.Contains(ruta[(ruta.LastIndexOf('/') + 1)..], Assert.Single(analisis.Avisos));
    }
}
