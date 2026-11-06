using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Importacion;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary><see cref="AnalizadorRegistro"/> con <c>registro-ejemplo.md</c> y textos inventados.</summary>
public class AnalizadorRegistroTests
{
    private const string Cabecera = """
        | Fecha | Empresa | Puesto | Modalidad | Estado | Enlace |
        |---|---|---|---|---|---|
        """;

    private static string Ejemplo() => File.ReadAllText(FabricaApi.RutaRegistroEjemplo);

    /// <summary>Tabla con una sola fila, para probar una columna cada vez.</summary>
    private static AnalisisRegistro AnalizarFila(string fecha = "2026-09-01", string empresa = "Acme", string puesto = "Backend",
        string modalidad = "Remoto", string estado = "Enviada", string enlace = "—") =>
        AnalizadorRegistro.Analizar($"{Cabecera}\n| {fecha} | {empresa} | {puesto} | {modalidad} | {estado} | {enlace} |\n");

    private static FilaLote UnicaFila(AnalisisRegistro analisis)
    {
        Assert.Empty(analisis.SinLeer);
        return Assert.Single(analisis.Lote.Filas);
    }

    [Fact]
    public void Analizar_RegistroDeEjemploConCrlf_LeeTodasLasFilasDeLaTablaYNadaMas()
    {
        // No hay .gitattributes: se fuerza CRLF aquí para que no dependa de cómo se descargue el repositorio.
        var analisis = AnalizadorRegistro.Analizar(Ejemplo().ReplaceLineEndings("\r\n"));

        Assert.True(analisis.TablaEncontrada);
        Assert.Equal(LimitesLote.VersionActual, analisis.Lote.Version);
        Assert.Equal(OrigenLote.Tabla, analisis.Lote.Origen);
        Assert.Equal(8, analisis.Lote.Filas.Count);
        Assert.Empty(analisis.SinLeer);
        Assert.Equal(3, analisis.EnviosEnTexto);
        Assert.Equal("Talleres Ficticios S.L.", analisis.Lote.Filas[0].Empresa);
        Assert.Equal("Demo Soluciones", analisis.Lote.Filas[^1].Empresa);
        Assert.DoesNotContain(analisis.Lote.Filas, f => f.Empresa.EndsWith('\r') || f.Enlace?.EndsWith('\r') == true);
    }

    [Fact]
    public void Analizar_ConCrlfYConLf_DaLoMismo()
    {
        var conCrlf = AnalizadorRegistro.Analizar(Ejemplo().ReplaceLineEndings("\r\n"));
        var conLf = AnalizadorRegistro.Analizar(Ejemplo().ReplaceLineEndings("\n"));

        Assert.Equal(conLf.Lote.Filas, conCrlf.Lote.Filas);
        Assert.Equal(conLf.EnviosEnTexto, conCrlf.EnviosEnTexto);
    }

    [Fact]
    public void Analizar_FilaCompleta_DaLaLineaYTodosLosCampos()
    {
        var fila = AnalizadorRegistro.Analizar(Ejemplo()).Lote.Filas[1];

        Assert.Equal(new DateOnly(2026, 8, 5), fila.Fecha);
        Assert.False(fila.FechaAproximada);
        Assert.Equal("Consultoría Inventada, S.A.", fila.Empresa);
        Assert.Equal("Programador Angular", fila.Puesto);
        Assert.Equal(Modalidad.Hibrido, fila.Modalidad);
        Assert.Equal("Híbrido Málaga", fila.ModalidadDetalle);
        Assert.Equal("Málaga", fila.Ubicacion);
        Assert.Equal("InfoJobs", fila.Portal);
        Assert.Equal("www.ejemplo.com/ofertas/1002", fila.Enlace);
        Assert.Equal(EstadoCandidatura.Enviada, fila.Estado);
        Assert.Equal("Enviada (InfoJobs)", fila.Nota);
        Assert.Equal(17, fila.Linea);
    }

    [Fact]
    public void Analizar_FechaAntesDelDiaMes_TomaElAnioDeLaFilaAnteriorYQuedaAproximada()
    {
        var filas = AnalizadorRegistro.Analizar(Ejemplo()).Lote.Filas;

        var aproximada = Assert.Single(filas, f => f.FechaAproximada);
        Assert.Equal(new DateOnly(2026, 8, 12), aproximada.Fecha); // es día-mes, no mes-día
        Assert.Equal("Fecha aproximada: antes del 12-08.", aproximada.Nota);
    }

    [Theory]
    // Tras el 30-12, el 02-01 es del año siguiente: con el de la fila quedaría casi un año antes.
    [InlineData("2026-12-30", "(antes del 02-01)", "2027-01-02")]
    [InlineData("2026-07-15", "(antes del 10-01)", "2027-01-10")]
    // Hasta medio año antes, o cualquier fecha posterior, es del mismo año.
    [InlineData("2026-08-20", "(antes del 12-08)", "2026-08-12")]
    [InlineData("2026-07-15", "(antes del 20-01)", "2026-01-20")]
    [InlineData("2026-03-01", "(antes del 01-09)", "2026-09-01")]
    public void Analizar_FechaAproximadaMasDeMedioAnioAntesDeLaAnterior_EsDelAnioSiguiente(string anterior, string aproximada, string esperada)
    {
        var texto = $"""
            {Cabecera}
            | {anterior} | Acme | Uno | Remoto | Enviada | — |
            | {aproximada} | Acme | Dos | Remoto | Enviada | — |
            """;

        var filas = AnalizadorRegistro.Analizar(texto).Lote.Filas;

        Assert.Equal(DateOnly.Parse(esperada, System.Globalization.CultureInfo.InvariantCulture), filas[1].Fecha);
        Assert.True(filas[1].FechaAproximada);
    }

    [Fact]
    public void Analizar_FechaAproximadaQueNoExisteEnNingunAnioCercano_VaASinLeer()
    {
        var texto = $"""
            {Cabecera}
            | 2026-03-01 | Acme | Uno | Remoto | Enviada | — |
            | (antes del 31-02) | Acme | Dos | Remoto | Enviada | — |
            """;

        Assert.Contains("fecha", Assert.Single(AnalizadorRegistro.Analizar(texto).SinLeer).Motivo);
    }

    [Fact]
    public void Analizar_FechaAproximadaEnLaPrimeraFila_VaASinLeer()
    {
        var analisis = AnalizarFila(fecha: "(antes del 12-08)");

        Assert.Empty(analisis.Lote.Filas);
        Assert.Contains("fecha", Assert.Single(analisis.SinLeer).Motivo);
    }

    [Fact]
    public void Analizar_FechaAproximadaTrasUnaFilaSinFecha_VaASinLeerEnVezDeAdivinarElAnio()
    {
        var texto = $"""
            {Cabecera}
            | 2025-01-10 | Acme | Uno | Remoto | Enviada | — |
            | ayer | Acme | Dos | Remoto | Enviada | — |
            | (antes del 12-08) | Acme | Tres | Remoto | Enviada | — |
            """;

        var analisis = AnalizadorRegistro.Analizar(texto);

        Assert.Equal("Uno", Assert.Single(analisis.Lote.Filas).Puesto);
        Assert.Equal(2, analisis.SinLeer.Count);
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("01/09/2026")]
    [InlineData("septiembre")]
    [InlineData("")]
    public void Analizar_FechaDesconocida_VaASinLeer(string fecha)
    {
        var analisis = AnalizarFila(fecha: fecha);

        Assert.Empty(analisis.Lote.Filas);
        Assert.Contains("fecha", Assert.Single(analisis.SinLeer).Motivo);
    }

    [Theory]
    [InlineData("No indicada", Modalidad.NoIndicada, "No indicada", null)]
    [InlineData("Híbrido Málaga", Modalidad.Hibrido, "Híbrido Málaga", "Málaga")]
    [InlineData("HÍBRIDO, Sevilla", Modalidad.Hibrido, "HÍBRIDO, Sevilla", "Sevilla")]
    [InlineData("Híbrido", Modalidad.Hibrido, "Híbrido", null)]
    [InlineData("Híbrido 3 días", Modalidad.Hibrido, "Híbrido 3 días", null)]
    [InlineData("Híbrido flexible", Modalidad.Hibrido, "Híbrido flexible", null)]
    [InlineData("Presencial, Madrid", Modalidad.Presencial, "Presencial, Madrid", "Madrid")]
    [InlineData("Presencial Madrid", Modalidad.Presencial, "Presencial Madrid", null)]
    [InlineData("Presencial", Modalidad.Presencial, "Presencial", null)]
    [InlineData("Remoto", Modalidad.Remoto, "Remoto", null)]
    [InlineData("Teletrabajo total", Modalidad.Remoto, "Teletrabajo total", null)]
    [InlineData("Remoto o presencial", Modalidad.Presencial, "Remoto o presencial", null)]
    [InlineData("Teletrabajo parcial", Modalidad.Hibrido, "Teletrabajo parcial", null)]
    [InlineData("Remoto parcial, 2 días en Madrid", Modalidad.Hibrido, "Remoto parcial, 2 días en Madrid", null)]
    [InlineData("Mixto", Modalidad.Hibrido, "Mixto", null)]
    [InlineData("Presencial parcial", Modalidad.Hibrido, "Presencial parcial", null)]
    [InlineData("Remoto, jornada parcial", Modalidad.Remoto, "Remoto, jornada parcial", null)]
    [InlineData("Teletrabajo, jornada parcial", Modalidad.Remoto, "Teletrabajo, jornada parcial", null)]
    [InlineData("Remoto → híbrido Madrid", Modalidad.Hibrido, "Remoto → híbrido Madrid", null)]
    [InlineData("100% teletrabajo", Modalidad.Remoto, "100% teletrabajo", null)]
    [InlineData("A convenir", Modalidad.NoIndicada, "A convenir", null)]
    [InlineData("—", Modalidad.NoIndicada, null, null)]
    public void Analizar_Modalidad_ConservaElTextoYDeduceLaUbicacion(string texto, Modalidad modalidad, string? detalle, string? ubicacion)
    {
        var fila = UnicaFila(AnalizarFila(modalidad: texto));

        Assert.Equal(modalidad, fila.Modalidad);
        Assert.Equal(detalle, fila.ModalidadDetalle);
        Assert.Equal(ubicacion, fila.Ubicacion);
    }

    [Theory]
    [InlineData("Enviada", EstadoCandidatura.Enviada)]
    [InlineData("ENVIADA", EstadoCandidatura.Enviada)]
    [InlineData("Enviado por correo", EstadoCandidatura.Enviada)]
    [InlineData("Caducada", EstadoCandidatura.Cerrada)]
    [InlineData("Cerrada", EstadoCandidatura.Cerrada)]
    [InlineData("Cerrado el plazo", EstadoCandidatura.Cerrada)]
    [InlineData("Entrevista", EstadoCandidatura.Entrevista)]
    [InlineData("Oferta recibida", EstadoCandidatura.Oferta)]
    [InlineData("Preparada", EstadoCandidatura.Preparada)]
    [InlineData("Detectada", EstadoCandidatura.Detectada)]
    [InlineData("**Enviada**", EstadoCandidatura.Enviada)]
    public void Analizar_Estado_SeReconocePorElPrefijo(string texto, EstadoCandidatura estado)
    {
        var fila = UnicaFila(AnalizarFila(estado: texto));

        Assert.Equal(estado, fila.Estado);
        Assert.Null(fila.MotivoDescarte);
        Assert.Null(fila.MotivoDescarteTexto);
    }

    [Theory]
    // Casos reales del registro.
    [InlineData("Llamada para concertar; **entrevista por Teams el 11/11 a las 15:00**; Dosier de Competencias ENVIADO el 06/11", EstadoCandidatura.Entrevista, true)]
    [InlineData("Entrevistas (Teams y presencial) superadas; cursillo el lunes 09/11", EstadoCandidatura.Entrevista, false)]
    [InlineData("Caducada antes de enviarla (CV adaptado hecho)", EstadoCandidatura.Cerrada, false)]
    [InlineData("Enviada (2ª candidatura a Norvalia)", EstadoCandidatura.Enviada, false)]
    // Un estado final escrito después manda sobre el del principio.
    [InlineData("Oferta caducada", EstadoCandidatura.Cerrada, true)]
    [InlineData("Oferta cerrada el 10/11", EstadoCandidatura.Cerrada, true)]
    [InlineData("Enviada; la cerraron el 10/11 (proceso cerrado)", EstadoCandidatura.Cerrada, true)]
    [InlineData("Enviada; descartada el 10/11", EstadoCandidatura.Descartada, true)]
    // …salvo que lo cerrado sea la oferta y no la candidatura.
    [InlineData("Enviada (InfoJobs; inscripciones cerradas)", EstadoCandidatura.Enviada, false)]
    [InlineData("Enviada; plazo ya cerrado", EstadoCandidatura.Enviada, false)]
    // Un estado que no es final no adelanta al del principio.
    [InlineData("Enviada (oferta de Marco Talento)", EstadoCandidatura.Enviada, false)]
    // Sin estado al principio: la palabra con más prioridad.
    [InlineData("Tras la llamada, oferta por escrito", EstadoCandidatura.Oferta, true)]
    [InlineData("Por LinkedIn: enviada", EstadoCandidatura.Enviada, true)]
    // Si el texto duda, niega o espera, solo vale el principio.
    [InlineData("Enviada, sin respuesta", EstadoCandidatura.Enviada, false)]
    [InlineData("Enviada; sin entrevista aún", EstadoCandidatura.Enviada, false)]
    [InlineData("Enviada; a la espera de respuesta", EstadoCandidatura.Enviada, false)]
    [InlineData("Enviada; posible entrevista la semana que viene", EstadoCandidatura.Enviada, false)]
    [InlineData("Entrevista; aún no está cerrado", EstadoCandidatura.Entrevista, false)]
    // Lo tachado ya no vale.
    [InlineData("~~Descartada~~ Enviada", EstadoCandidatura.Enviada, false)]
    public void Analizar_Estado_SiNoVaAlPrincipioSeBuscaEnElTextoYLoDice(string texto, EstadoCandidatura estado, bool deducido)
    {
        var fila = UnicaFila(AnalizarFila(estado: texto));

        Assert.Equal(estado, fila.Estado);
        Assert.Equal(deducido, fila.EstadoDeducido);
    }

    [Fact]
    public void Analizar_EstadoTachado_NoSaleNiEnLaNota()
    {
        var fila = UnicaFila(AnalizarFila(estado: "~~Descartada~~ Enviada (LinkedIn)"));

        Assert.Equal("Enviada (LinkedIn)", fila.Nota);
    }

    [Theory]
    [InlineData("Descartado tras el quiz de código y lógica")]
    [InlineData("Enviada; descartada el 10/11 por el inglés")]
    public void Analizar_EstadoDescartadaEnCualquierParte_LlevaMotivoOtroConElTexto(string texto)
    {
        var fila = UnicaFila(AnalizarFila(estado: texto));

        Assert.Equal(EstadoCandidatura.Descartada, fila.Estado);
        Assert.Equal(MotivoDescarte.Otro, fila.MotivoDescarte);
        Assert.Equal(texto, fila.MotivoDescarteTexto);
    }

    [Fact]
    public void Analizar_EstadoDescartada_LlevaMotivoOtroConElTextoOriginal()
    {
        var fila = UnicaFila(AnalizarFila(estado: "Descartada (piden inglés alto)"));

        Assert.Equal(EstadoCandidatura.Descartada, fila.Estado);
        Assert.Equal(MotivoDescarte.Otro, fila.MotivoDescarte);
        Assert.Equal("Descartada (piden inglés alto)", fila.MotivoDescarteTexto);
    }

    [Fact]
    public void Analizar_Descartada_ElTextoVaSoloEnElMotivoYNoTambienEnLaNota()
    {
        var fila = UnicaFila(AnalizarFila(estado: "Descartado tras el quiz de código y lógica"));

        Assert.Equal("Descartado tras el quiz de código y lógica", fila.MotivoDescarteTexto);
        Assert.Null(fila.Nota);
    }

    [Fact]
    public void Analizar_DescartadaConFechaAproximada_LaNotaSoloLlevaLaFecha()
    {
        var texto = $"""
            {Cabecera}
            | 2026-08-20 | Acme | Uno | Remoto | Enviada | — |
            | (antes del 12-08) | Acme | Dos | Remoto | Descartada (piden inglés) | — |
            """;

        var fila = AnalizadorRegistro.Analizar(texto).Lote.Filas[1];

        Assert.Equal("Fecha aproximada: antes del 12-08.", fila.Nota);
        Assert.Equal("Descartada (piden inglés)", fila.MotivoDescarteTexto);
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Quizá enviada")]
    [InlineData("No enviada todavía")]
    [InlineData("¿Entrevista?")]
    [InlineData("Llamada para concertar")]
    [InlineData("Pendiente de entrevista")]
    [InlineData("Esperando oferta")]
    [InlineData("Posible entrevista")]
    [InlineData("A la espera de la entrevista")]
    [InlineData("Descartada?")]
    [InlineData("Descartada ? (no lo sé)")]
    [InlineData("~~Enviada~~")]
    [InlineData("—")]
    public void Analizar_EstadoDesconocido_VaASinLeerSinRomperLasDemas(string estado)
    {
        var texto = $"""
            {Cabecera}
            | 2026-09-01 | Acme | Uno | Remoto | {estado} | — |
            | 2026-09-02 | Acme | Dos | Remoto | Enviada | — |
            """;

        var analisis = AnalizadorRegistro.Analizar(texto);

        Assert.Equal("Dos", Assert.Single(analisis.Lote.Filas).Puesto);
        var sinLeer = Assert.Single(analisis.SinLeer);
        Assert.Equal(3, sinLeer.Linea);
        Assert.Contains("estado", sinLeer.Motivo);
    }

    [Theory]
    [InlineData("Enviada (LinkedIn, solicitud sencilla)", "LinkedIn")]
    [InlineData("Enviada (infojobs)", "InfoJobs")]
    [InlineData("Enviada (Indeed)", "Indeed")]
    [InlineData("Entrevista (Tecnoempleo)", "Tecnoempleo")]
    [InlineData("Enviada (Workday de la empresa)", "Workday")]
    [InlineData("Enviada (por correo a LinkedIn)", null)]
    [InlineData("Enviada por LinkedIn", null)]
    [InlineData("Enviada", null)]
    public void Analizar_Portal_SoloSiElParentesisEmpiezaPorUnoConocido(string estado, string? portal)
    {
        Assert.Equal(portal, UnicaFila(AnalizarFila(estado: estado)).Portal);
    }

    [Theory]
    [InlineData("Enviada", null)]
    [InlineData("ENVIADA.", null)]
    [InlineData("Enviada (LinkedIn)", "Enviada (LinkedIn)")]
    [InlineData("Enviada por correo", "Enviada por correo")]
    public void Analizar_Nota_SoloSiElEstadoTraeDetalle(string estado, string? nota)
    {
        Assert.Equal(nota, UnicaFila(AnalizarFila(estado: estado)).Nota);
    }

    [Theory]
    [InlineData("https://ejemplo.com/1", "https://ejemplo.com/1")]
    [InlineData("[la oferta](https://ejemplo.com/1)", "https://ejemplo.com/1")]
    [InlineData("<https://ejemplo.com/1>", "https://ejemplo.com/1")]
    [InlineData("mailto:rrhh@ejemplo.com", "mailto:rrhh@ejemplo.com")]
    [InlineData("—", null)]
    [InlineData("", null)]
    public void Analizar_Enlace_SeQuedaComoSeEscribioParaAvisarAlComparar(string enlace, string? esperado)
    {
        Assert.Equal(esperado, UnicaFila(AnalizarFila(enlace: enlace)).Enlace);
    }

    [Theory]
    [InlineData("**Dev** Angular", "Dev Angular")]
    [InlineData("Dev **Angular**", "Dev Angular")]
    [InlineData("**Dev Angular**", "Dev Angular")]
    [InlineData("__Dev__ Angular", "Dev Angular")]
    [InlineData("_Dev_ Angular", "Dev Angular")]
    [InlineData("Dev *Angular*", "Dev Angular")]
    [InlineData("~~Dev~~ Angular", "Dev Angular")]
    [InlineData("`Dev` Angular", "Dev Angular")]
    [InlineData("Dev_Angular senior_", "Dev_Angular senior")]
    [InlineData("Dev snake_case_y C++", "Dev snake_case_y C++")]
    public void Analizar_MarcasDeFormatoEnCualquierParteDeLaCelda_SeQuitanSinTocarLosGuionesBajosDeLasPalabras(string puesto, string esperado)
    {
        Assert.Equal(esperado, UnicaFila(AnalizarFila(puesto: puesto)).Puesto);
    }

    [Fact]
    public void Analizar_EmpresaConNegritaAMedias_EsLaMismaEmpresaQueSinNegrita()
    {
        var fila = UnicaFila(AnalizarFila(empresa: "**Acme** S.L."));

        Assert.Equal("Acme S.L.", fila.Empresa);
        Assert.Equal(ReglasImportacion.Clave("Acme S.L.", "Backend", fila.Fecha), ReglasImportacion.Clave(fila.Empresa, fila.Puesto, fila.Fecha));
    }

    [Theory]
    [InlineData("_Iota_", "Iota")]
    [InlineData("~~Iota~~", "Iota")]
    [InlineData("*Iota* Labs", "Iota Labs")]
    public void Analizar_EmpresaConCursivaOTachado_SinLasMarcas(string empresa, string esperada)
    {
        Assert.Equal(esperada, UnicaFila(AnalizarFila(empresa: empresa)).Empresa);
    }

    [Theory]
    [InlineData("https://ejemplo.com/mis_ofertas/_layouts/oferta_1")]
    [InlineData("https://x.com/_ab_/1")]
    [InlineData("https://x.com/buscar?q=_x_")]
    [InlineData("https://x.com/a**b~~c`d*e*")]
    public void Analizar_Enlace_NoPasaPorLaLimpiezaDeFormato(string enlace)
    {
        Assert.Equal(enlace, UnicaFila(AnalizarFila(enlace: enlace)).Enlace);
    }

    [Theory]
    [InlineData("[**la oferta**](https://x.com/_ab_/1)", "https://x.com/_ab_/1")]
    [InlineData("<https://x.com/buscar?q=_x_>", "https://x.com/buscar?q=_x_")]
    public void Analizar_EnlaceConTextoOEntreAngulos_SacaLaDireccionSinTocarla(string celda, string esperado)
    {
        Assert.Equal(esperado, UnicaFila(AnalizarFila(enlace: celda)).Enlace);
    }

    [Fact]
    public void Analizar_CabeceraConNegrita_SeSigueReconociendo()
    {
        var texto = """
            | **Fecha** | **Empresa** | **Puesto** | **Estado** | **Enlace** |
            |---|---|---|---|---|
            | 2026-09-01 | Acme | Backend | Enviada | https://x.com/_ab_/1 |
            """;

        var fila = UnicaFila(AnalizadorRegistro.Analizar(texto));

        Assert.Equal("https://x.com/_ab_/1", fila.Enlace);
    }

    [Fact]
    public void Analizar_BarraEscapadaDentroDeUnaCelda_EsParteDelTexto()
    {
        var fila = UnicaFila(AnalizarFila(puesto: @"Backend \| Frontend"));

        Assert.Equal("Backend | Frontend", fila.Puesto);
    }

    [Fact]
    public void Analizar_BarraSinEscaparDentroDeUnaCelda_VaASinLeerPorElNumeroDeCeldas()
    {
        var analisis = AnalizarFila(puesto: "Backend | Frontend");

        Assert.Empty(analisis.Lote.Filas);
        Assert.Contains("7 celdas", Assert.Single(analisis.SinLeer).Motivo);
    }

    [Fact]
    public void Analizar_FilaIncompleta_VaASinLeerConSuLinea()
    {
        var texto = $"""
            {Cabecera}
            | 2026-09-01 | Acme | Uno |
            | 2026-09-02 | Acme | Dos | Remoto | Enviada | — |
            """;

        var analisis = AnalizadorRegistro.Analizar(texto);

        Assert.Equal("Dos", Assert.Single(analisis.Lote.Filas).Puesto);
        Assert.Equal(3, Assert.Single(analisis.SinLeer).Linea);
    }

    [Theory]
    [InlineData("")]
    [InlineData("—")]
    [InlineData("-")]
    public void Analizar_SinEmpresaOSinPuesto_VaASinLeer(string vacio)
    {
        Assert.Single(AnalizarFila(empresa: vacio).SinLeer);
        Assert.Single(AnalizarFila(puesto: vacio).SinLeer);
    }

    [Theory]
    [InlineData(", S.L.")]
    [InlineData("...")]
    [InlineData("(S.A.)")]
    public void Analizar_EmpresaQueSeQuedaEnNadaAlNormalizar_VaASinLeer(string empresa)
    {
        var analisis = AnalizarFila(empresa: empresa);

        Assert.Empty(analisis.Lote.Filas);
        Assert.Contains("empresa", Assert.Single(analisis.SinLeer).Motivo);
    }

    [Theory]
    [InlineData(201, 1)]
    [InlineData(1, 301)]
    public void Analizar_EmpresaOPuestoPorEncimaDelLimite_VaASinLeerParaQueAplicarNoDe400(int largoEmpresa, int largoPuesto)
    {
        var analisis = AnalizarFila(empresa: new string('a', largoEmpresa), puesto: new string('b', largoPuesto));

        Assert.Empty(analisis.Lote.Filas);
        Assert.Contains("demasiado largo", Assert.Single(analisis.SinLeer).Motivo);
    }

    [Fact]
    public void Analizar_EstadoLarguisimo_RecortaMotivoYNotaHastaLosLimitesDelLote()
    {
        var descartada = UnicaFila(AnalizarFila(estado: $"Descartada ({new string('x', 6000)})"));
        var enviada = UnicaFila(AnalizarFila(estado: $"Enviada ({new string('x', 6000)})"));

        Assert.Equal(LimitesLote.MotivoDescarteTexto, descartada.MotivoDescarteTexto!.Length);
        Assert.Equal(LimitesLote.Nota, enviada.Nota!.Length);
        Assert.EndsWith("…", enviada.Nota);
    }

    [Fact]
    public void Analizar_VinetasSeccionesYUrlsFueraDeLaTabla_SeIgnoran()
    {
        var texto = $"""
            ## Primera sección
            - Empresa Falsa: 2026-01-01 Backend
            https://ejemplo.com/oferta

            {Cabecera}
            | 2026-09-01 | Acme | Uno | Remoto | Enviada | — |

            ## Otra sección
            | 2026-09-02 | Acme | Fuera de la tabla | Remoto | Enviada | — |
            """;

        Assert.Equal("Uno", UnicaFila(AnalizadorRegistro.Analizar(texto)).Puesto);
    }

    [Fact]
    public void Analizar_EnviosEnTexto_CuentaSoloLasLineasConEnviadaEnMayusculasFueraDeLaTabla()
    {
        var texto = $"""
            - Acme: ENVIADA el lunes
            - Otra: enviada el martes
            {Cabecera}
            | 2026-09-01 | Acme | ENVIADA en el puesto | Remoto | ENVIADA | — |

            - Más: ENVIADA y ENVIADA en la misma línea
            """;

        Assert.Equal(2, AnalizadorRegistro.Analizar(texto).EnviosEnTexto);
    }

    [Fact]
    public void Analizar_ColumnasEnOtroOrdenYSinEnlaceNiModalidad_LasBuscaPorNombre()
    {
        var texto = """
            | ESTADO | Puesto | Notas | Empresa | Fecha |
            |:---|---:|---|---|---|
            | Enviada | Backend | Lo que sea | Acme | 2026-09-01 |
            """;

        var fila = UnicaFila(AnalizadorRegistro.Analizar(texto));

        Assert.Equal("Acme", fila.Empresa);
        Assert.Equal("Backend", fila.Puesto);
        Assert.Equal(EstadoCandidatura.Enviada, fila.Estado);
        Assert.Null(fila.Enlace);
        Assert.Equal(Modalidad.NoIndicada, fila.Modalidad);
    }

    [Fact]
    public void Analizar_DosTablas_UsaLaPrimeraConLasColumnasNecesarias()
    {
        var texto = $"""
            | Tema | Pendiente |
            |---|---|
            | Portfolio | Capturas |

            {Cabecera}
            | 2026-09-01 | Acme | Uno | Remoto | Enviada | — |

            {Cabecera}
            | 2026-09-02 | Acme | Dos | Remoto | Enviada | — |
            """;

        Assert.Equal("Uno", UnicaFila(AnalizadorRegistro.Analizar(texto)).Puesto);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# Solo texto\n\n- Sin tablas\n")]
    [InlineData("| Fecha | Empresa | Puesto |\n|---|---|---|\n| 2026-09-01 | Acme | Uno |\n")]
    public void Analizar_SinTablaValida_LoDice(string texto)
    {
        var analisis = AnalizadorRegistro.Analizar(texto);

        Assert.False(analisis.TablaEncontrada);
        Assert.Empty(analisis.Lote.Filas);
    }

    [Fact]
    public void Analizar_ConMarcaBomAlPrincipio_LaIgnora()
    {
        var analisis = AnalizadorRegistro.Analizar($"﻿{Cabecera}\n| 2026-09-01 | Acme | Uno | Remoto | Enviada | — |\n");

        Assert.Equal("Uno", UnicaFila(analisis).Puesto);
    }

    [Fact]
    public void Analizar_LineasLarguisimasFueraDeLaTabla_NoMolestan()
    {
        var texto = $"{new string('x', 100_000)}\r\n{Cabecera}\r\n| 2026-09-01 | Acme | Uno | Remoto | Enviada | — |\r\n";

        Assert.Equal("Uno", UnicaFila(AnalizadorRegistro.Analizar(texto.ReplaceLineEndings("\r\n"))).Puesto);
    }
}
