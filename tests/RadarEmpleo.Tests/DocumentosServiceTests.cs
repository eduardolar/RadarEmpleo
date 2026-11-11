using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="DocumentosService"/> sobre SQLite en memoria y una carpeta Curriculum temporal con nombres
/// copiados de la real (los archivos son inventados).
/// </summary>
public sealed class DocumentosServiceTests : ArnesSqliteTests, IDisposable
{
    private const string SemanaMarcoTalento = "Candidaturas/Semana 2026-08-31 (31 ago - 6 sep)";
    private const string SemanaSiguiente = "Candidaturas/Semana 2026-09-07 (7 - 13 sep)";
    private const string SemanaLejana = "Candidaturas/Semana 2026-11-02 (2 - 8 nov)";
    private const string Antiguo = "Archivo/_estilo_antiguo";
    private const string CvMarcoTalento = "CV_Eduardo_Lara_Navarro_NET_MarcoTalento";

    private static readonly DateTime Deteccion = new(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"radar-documentos-{Guid.NewGuid():N}");
    private readonly RutasCurriculum _rutas;
    private readonly DocumentosService _documentos;
    private readonly CandidaturasService _candidaturas;

    public DocumentosServiceTests()
    {
        Directory.CreateDirectory(_carpeta);
        _rutas = new RutasCurriculum(_carpeta);
        _documentos = new DocumentosService(Factory, new LectorCurriculum(_rutas), _rutas, Reloj);
        _candidaturas = new CandidaturasService(Factory, Reloj, Exportador, _rutas);
    }

    void IDisposable.Dispose()
    {
        foreach (var enlace in Directory.EnumerateDirectories(_carpeta, "*", SearchOption.AllDirectories)
                     .Where(c => new DirectoryInfo(c).LinkTarget is not null).ToList())
            Directory.Delete(enlace);
        Directory.Delete(_carpeta, recursive: true);
        Dispose();
    }

    private string Crear(string relativa, string contenido = "contenido")
    {
        var ruta = Path.Combine(_carpeta, relativa);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, contenido);
        return ruta;
    }

    private async Task<int> CandidaturaAsync(string empresa = "Marco Talento", DateTime? deteccion = null)
    {
        var creada = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest(empresa,
            "Desarrollador/a .NET (.NET Core, SQL Server, Azure DevOps)", FechaDeteccion: deteccion ?? Deteccion));
        return creada.Id;
    }

    private void CrearCarpetaComoLaReal()
    {
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.docx");
        Crear($"{SemanaMarcoTalento}/CV_Eduardo_Lara_Navarro_IA_Galandor.pdf");
        Crear($"{Antiguo}/{CvMarcoTalento}.pdf");
        Crear($"{Antiguo}/{CvMarcoTalento}.docx");
        Crear($"{SemanaLejana}/CV_Eduardo_Lara_Navarro_Python_IA_DATALUX.pdf");
        Crear("CV_Eduardo_Lara_Navarro.pdf");
        Crear("Documentos personales/DNI_MarcoTalento.pdf");
    }

    // ---------------------------------------------------------------- sugeridos

    [Fact]
    public async Task Sugeridos_MarcoTalento_PrimeroElDeSuSemanaYLuegoElAntiguo()
    {
        CrearCarpetaComoLaReal();
        var id = await CandidaturaAsync();

        var sugeridos = await _documentos.SugeridosAsync(id);

        Assert.NotNull(sugeridos);
        Assert.Equal(2, sugeridos.Count);
        var primero = sugeridos[0];
        Assert.Equal($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf", primero.RutaRelativa);
        Assert.Equal($"{CvMarcoTalento}.pdf", primero.NombreArchivo);
        Assert.Equal(SemanaMarcoTalento, primero.Carpeta);
        Assert.Equal(TipoDocumento.CV, primero.Tipo);
        Assert.Equal("pdf", primero.Extension);
        Assert.Equal(CoincidenciaSugerencia.Empresa, primero.Coincidencia);
        Assert.False(primero.EsAntiguo);
        Assert.False(primero.YaVinculado);
        Assert.Equal($"{Antiguo}/{CvMarcoTalento}.pdf", sugeridos[1].RutaRelativa);
        Assert.True(sugeridos[1].EsAntiguo);
    }

    [Fact]
    public async Task Sugeridos_CompletaAntesQueParcialSemanasCercanasAntesYCvAntesQueCarta()
    {
        Crear($"{SemanaLejana}/CV_Eduardo_Lara_Navarro_Python_IA_DATALUX.pdf");
        Crear($"{SemanaSiguiente}/Carta_Presentacion_Eduardo_Lara_Navarro_GrupoDATALUX.pdf");
        Crear($"{SemanaSiguiente}/CV_Eduardo_Lara_Navarro_GrupoDATALUX.pdf");
        Crear($"{SemanaMarcoTalento}/CV_Eduardo_Lara_Navarro_DATALUX.pdf");
        Crear($"{Antiguo}/CV_Eduardo_Lara_Navarro_GrupoDATALUX.pdf");
        Crear("CV_Eduardo_Lara_Navarro_DATALUX.pdf");
        var id = await CandidaturaAsync("Grupo DATALUX");

        var sugeridos = (await _documentos.SugeridosAsync(id))!;

        Assert.Equal(new[]
        {
            $"{SemanaSiguiente}/CV_Eduardo_Lara_Navarro_GrupoDATALUX.pdf",
            $"{SemanaSiguiente}/Carta_Presentacion_Eduardo_Lara_Navarro_GrupoDATALUX.pdf",
            $"{Antiguo}/CV_Eduardo_Lara_Navarro_GrupoDATALUX.pdf",
            $"{SemanaMarcoTalento}/CV_Eduardo_Lara_Navarro_DATALUX.pdf",
            $"{SemanaLejana}/CV_Eduardo_Lara_Navarro_Python_IA_DATALUX.pdf",
            "CV_Eduardo_Lara_Navarro_DATALUX.pdf"
        }, sugeridos.Select(s => s.RutaRelativa));
        Assert.Equal(CoincidenciaSugerencia.Empresa, sugeridos[0].Coincidencia);
        Assert.Equal(CoincidenciaSugerencia.Parcial, sugeridos[3].Coincidencia);
    }

    [Fact]
    public async Task Sugeridos_SoloHayDocx_DevuelveElDocx()
    {
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.docx");

        var sugeridos = await _documentos.SugeridosAsync(await CandidaturaAsync());

        Assert.Equal("docx", Assert.Single(sugeridos!).Extension);
    }

    [Fact]
    public async Task Sugeridos_NingunoConLaEmpresa_DevuelveLosDeLaSemanaDeLaCandidatura()
    {
        CrearCarpetaComoLaReal();
        Crear($"{SemanaMarcoTalento}/Carta_Presentacion_Eduardo_Lara_Navarro_Galandor.pdf");

        var sugeridos = (await _documentos.SugeridosAsync(await CandidaturaAsync("Operarios Inventados")))!;

        Assert.Equal(new[]
        {
            $"{SemanaMarcoTalento}/CV_Eduardo_Lara_Navarro_IA_Galandor.pdf",
            $"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf",
            $"{SemanaMarcoTalento}/Carta_Presentacion_Eduardo_Lara_Navarro_Galandor.pdf"
        }, sugeridos.Select(s => s.RutaRelativa));
        Assert.All(sugeridos, s => Assert.Equal(CoincidenciaSugerencia.MismaSemana, s.Coincidencia));
    }

    [Fact]
    public async Task Sugeridos_ConFechaDeEnvio_UsaLaSemanaDelEnvio()
    {
        Crear($"{SemanaMarcoTalento}/CV_Eduardo_Lara_Navarro_Operario.pdf");
        Crear("Candidaturas/Semana 2026-10-26 (26 oct - 1 nov)/CV_Eduardo_Lara_Navarro_Operario.pdf");
        var id = await CandidaturaAsync("Operarios Inventados");
        await _candidaturas.CambiarEstadoAsync(id, new CambiarEstadoRequest(EstadoCandidatura.Enviada)); // el 27/10

        var sugeridos = await _documentos.SugeridosAsync(id);

        Assert.Equal("Candidaturas/Semana 2026-10-26 (26 oct - 1 nov)/CV_Eduardo_Lara_Navarro_Operario.pdf",
            Assert.Single(sugeridos!).RutaRelativa);
    }

    [Fact]
    public async Task Sugeridos_MasDeDiez_DevuelveDiez()
    {
        for (var i = 0; i < 14; i++)
            Crear($"{SemanaMarcoTalento}/CV_Eduardo_Lara_Navarro_{i:00}_MarcoTalento.pdf");

        var sugeridos = await _documentos.SugeridosAsync(await CandidaturaAsync());

        Assert.Equal(DocumentosService.MaximoSugerencias, sugeridos!.Count);
    }

    [Fact]
    public async Task Sugeridos_YaVinculado_LoMarca()
    {
        CrearCarpetaComoLaReal();
        var id = await CandidaturaAsync();
        await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));

        var sugeridos = await _documentos.SugeridosAsync(id);

        Assert.True(sugeridos![0].YaVinculado);
        Assert.False(sugeridos[1].YaVinculado);
    }

    [Fact]
    public async Task Sugeridos_NuncaDocumentosPersonales()
    {
        Crear("Documentos personales/CV_Eduardo_Lara_Navarro_MarcoTalento.pdf");
        Crear("Marca personal/CV_Eduardo_Lara_Navarro_MarcoTalento.pdf");

        Assert.Empty((await _documentos.SugeridosAsync(await CandidaturaAsync()))!);
    }

    [Fact]
    public async Task Sugeridos_CandidaturaQueNoExiste_Null()
    {
        Assert.Null(await _documentos.SugeridosAsync(999));
    }

    [Fact]
    public async Task Sugeridos_EnLaRaizSoloCvYCartas_NuncaUnDniDescargado()
    {
        Crear("CV_Eduardo_Lara_Navarro_MarcoTalento.pdf");
        Crear("DNI_MarcoTalento.pdf");
        Crear("Nomina_MarcoTalento.pdf");

        var sugeridos = (await _documentos.SugeridosAsync(await CandidaturaAsync()))!;

        Assert.Equal(["CV_Eduardo_Lara_Navarro_MarcoTalento.pdf"], sugeridos.Select(s => s.RutaRelativa));
    }

    private const string SemanaEnvioNovaIt = "Candidaturas/Semana 2026-08-03 (3 - 9 ago)";
    private const string SemanaPosterior = "Candidaturas/Semana 2026-10-26 (26 oct - 1 nov)";

    private async Task<int> NovaItEnviadaElOchoDeAgostoAsync()
    {
        var envio = new DateTime(2026, 8, 8, 9, 0, 0, DateTimeKind.Utc);
        var id = await CandidaturaAsync("NovaIT", envio);
        Reloj.AhoraUtc = envio;
        await _candidaturas.CambiarEstadoAsync(id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));
        Reloj.AhoraUtc = AhoraInicial;
        return id;
    }

    [Fact]
    public async Task Sugeridos_Enviada_LosDeSemanasPosterioresAlEnvioVanDetrasYMarcados()
    {
        // Caso real: NovaIT enviada el 08/08 sugería el CV de NovaIT de la semana del 26 oct.
        Crear($"{SemanaPosterior}/CV_Eduardo_Lara_Navarro_NET_NovaIT.pdf");
        Crear($"{Antiguo}/CV_Eduardo_Lara_Navarro_NovaIT.pdf");
        Crear($"{SemanaEnvioNovaIt}/CV_Eduardo_Lara_Navarro_NET_NovaIT.pdf");
        Crear("Candidaturas/Semana 2026-07-27 (27 jul - 2 ago)/CV_Eduardo_Lara_Navarro_NovaIT.pdf");
        var id = await NovaItEnviadaElOchoDeAgostoAsync();

        var sugeridos = (await _documentos.SugeridosAsync(id))!;

        Assert.Equal(new[]
        {
            $"{SemanaEnvioNovaIt}/CV_Eduardo_Lara_Navarro_NET_NovaIT.pdf",
            "Candidaturas/Semana 2026-07-27 (27 jul - 2 ago)/CV_Eduardo_Lara_Navarro_NovaIT.pdf",
            $"{Antiguo}/CV_Eduardo_Lara_Navarro_NovaIT.pdf",
            $"{SemanaPosterior}/CV_Eduardo_Lara_Navarro_NET_NovaIT.pdf"
        }, sugeridos.Select(s => s.RutaRelativa));
        Assert.Equal([false, false, false, true], sugeridos.Select(s => s.PosteriorAlEnvio));
    }

    [Fact]
    public async Task Sugeridos_SinEnviar_NingunoEsPosteriorAlEnvio()
    {
        Crear($"{SemanaPosterior}/CV_Eduardo_Lara_Navarro_NET_NovaIT.pdf");
        var id = await CandidaturaAsync("NovaIT", new DateTime(2026, 8, 8, 9, 0, 0, DateTimeKind.Utc));

        var sugerido = Assert.Single((await _documentos.SugeridosAsync(id))!);

        Assert.False(sugerido.PosteriorAlEnvio);
    }

    [Fact]
    public async Task Sugeridos_Enviada_ElDeLaMismaSemanaDelEnvioNoEsPosterior()
    {
        // Enviada el sábado: un CV hecho el lunes de esa semana sigue siendo de la semana del envío.
        Crear($"{SemanaEnvioNovaIt}/CV_Eduardo_Lara_Navarro_NET_NovaIT.pdf");
        var id = await NovaItEnviadaElOchoDeAgostoAsync();

        Assert.False(Assert.Single((await _documentos.SugeridosAsync(id))!).PosteriorAlEnvio);
    }

    [Fact]
    public async Task Sugeridos_SinCarpeta_LanzaReglaNegocio()
    {
        var id = await CandidaturaAsync();
        var sinCarpeta = new RutasCurriculum(null);
        var servicio = new DocumentosService(Factory, new LectorCurriculum(sinCarpeta), sinCarpeta, Reloj);

        await Assert.ThrowsAsync<ReglaNegocioException>(() => servicio.SugeridosAsync(id));
    }

    // ---------------------------------------------------------------- vincular

    [Fact]
    public async Task Vincular_UnCv_LoGuardaVigenteConSuHashYSaleEnLaFicha()
    {
        var ruta = Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf", "%PDF-1.4 inventado");
        var id = await CandidaturaAsync();

        var documento = await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));

        Assert.NotNull(documento);
        Assert.Equal(TipoDocumento.CV, documento.Tipo);
        Assert.Equal(OrigenDocumento.Manual, documento.Origen);
        Assert.True(documento.Vigente);
        Assert.True(documento.Encontrado);
        Assert.Equal(SemanaMarcoTalento, documento.Carpeta);
        Assert.Equal("pdf", documento.Extension);
        Assert.Equal(AhoraInicial, documento.FechaAlta);
        await using (var db = CreateContext())
        {
            var guardado = await db.Documentos.SingleAsync();
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(ruta))), guardado.HashSha256);
            Assert.Null(guardado.Paginas);
            Assert.Null(guardado.PreparacionId);
        }
        var ficha = await _candidaturas.ObtenerAsync(id);
        Assert.Equal(documento, Assert.Single(ficha!.Documentos));
    }

    [Fact]
    public async Task Vincular_ConBarrasInvertidas_GuardaLaRutaConBarras()
    {
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");

        var documento = await _documentos.VincularAsync(await CandidaturaAsync(),
            new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf".Replace('/', '\\')));

        Assert.Equal($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf", documento!.RutaRelativa);
    }

    [Fact]
    public async Task Vincular_DosCv_SoloElUltimoQuedaVigente()
    {
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        Crear($"{Antiguo}/{CvMarcoTalento}.pdf");
        Crear($"{SemanaMarcoTalento}/Carta_Presentacion_Eduardo_Lara_Navarro_MarcoTalento.pdf");
        var id = await CandidaturaAsync();

        var antiguo = await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{Antiguo}/{CvMarcoTalento}.pdf"));
        var carta = await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/Carta_Presentacion_Eduardo_Lara_Navarro_MarcoTalento.pdf"));
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var nuevo = await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));

        var ficha = (await _candidaturas.ObtenerAsync(id))!.Documentos;
        Assert.Equal([nuevo!.Id, antiguo!.Id, carta!.Id], ficha.Select(d => d.Id));
        Assert.Equal([true, false, true], ficha.Select(d => d.Vigente));
        Assert.Equal(TipoDocumento.Carta, ficha[2].Tipo);
    }

    [Fact]
    public async Task Vincular_ConTipoIndicado_UsaEseTipo()
    {
        Crear($"{SemanaLejana}/Dosier_Competencias_NovaIT_Eduardo_Lara_Navarro.pdf");

        var documento = await _documentos.VincularAsync(await CandidaturaAsync("NovaIT"),
            new VincularDocumentoRequest($"{SemanaLejana}/Dosier_Competencias_NovaIT_Eduardo_Lara_Navarro.pdf", TipoDocumento.Carta));

        Assert.Equal(TipoDocumento.Carta, documento!.Tipo);
    }

    [Theory]
    [InlineData("../perfil-busqueda.md")]
    [InlineData(@"..\fuera.pdf")]
    [InlineData("Documentos personales/DNI_MarcoTalento.pdf")]
    [InlineData("C:/Windows/CV.pdf")]
    [InlineData("perfil-busqueda.md")]
    public async Task Vincular_RutaNoValida_LanzaValidacionEnRutaRelativaYNoGuardaNada(string ruta)
    {
        CrearCarpetaComoLaReal();
        Crear("perfil-busqueda.md");
        var id = await CandidaturaAsync();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _documentos.VincularAsync(id, new VincularDocumentoRequest(ruta)));

        Assert.Equal("rutaRelativa", ex.Campo);
        await using var db = CreateContext();
        Assert.Empty(db.Documentos);
    }

    [Fact]
    public async Task Vincular_TipoQueNoExiste_LanzaValidacion()
    {
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        var id = await CandidaturaAsync();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf", (TipoDocumento)99)));
        Assert.Equal("tipo", ex.Campo);
    }

    [Fact]
    public async Task Vincular_ArchivoQueNoEsta_LanzaReglaNegocioConMensajeClaro()
    {
        var id = await CandidaturaAsync();

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf")));
        Assert.Equal("No encuentro el archivo. Puede que se haya movido.", ex.Message);
    }

    [Fact]
    public async Task Vincular_DosVecesElMismo_LanzaReglaNegocio()
    {
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        var id = await CandidaturaAsync();
        await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));

        await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf")));
        await using var db = CreateContext();
        Assert.Single(db.Documentos);
    }

    [Fact]
    public async Task Vincular_CandidaturaQueNoExiste_Null()
    {
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");

        Assert.Null(await _documentos.VincularAsync(999, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf")));
    }

    [Fact]
    public async Task Vincular_SinCarpeta_LanzaReglaNegocio()
    {
        var id = await CandidaturaAsync();
        var sinCarpeta = new RutasCurriculum(Path.Combine(_carpeta, "no-existe"));
        var servicio = new DocumentosService(Factory, new LectorCurriculum(sinCarpeta), sinCarpeta, Reloj);

        await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            servicio.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf")));
    }

    [Fact]
    public async Task VincularYQuitar_NoCambianNingunArchivoNiExportanElRadar()
    {
        CrearCarpetaComoLaReal();
        var id = await CandidaturaAsync();
        var llamadas = Exportador.Llamadas;
        var antes = Huella();

        var documento = await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));
        await _documentos.SugeridosAsync(id);
        await using (var abierto = (await _documentos.AbrirArchivoAsync(documento!.Id))!.Contenido) { }
        await _documentos.QuitarAsync(documento.Id);

        Assert.Equal(antes, Huella());
        Assert.Equal(llamadas, Exportador.Llamadas);
    }

    private string[] Huella() => Directory.GetFiles(_carpeta, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(r => $"{r}|{File.GetLastWriteTimeUtc(r):O}|{Convert.ToHexString(File.ReadAllBytes(r))}")
        .ToArray();

    [Fact]
    public async Task Vincular_DosCvDistintosALaVez_ElSegundoDa409ClaroYQuedaUnSoloVigente()
    {
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        Crear($"{Antiguo}/{CvMarcoTalento}.pdf");
        var id = await CandidaturaAsync();
        var otraPestana = new DocumentosService(Factory, new LectorCurriculum(_rutas), _rutas, Reloj);
        // Esta lee la ficha sin CV y, justo antes de guardar, la otra pestaña vincula otro CV.
        var lenta = new DocumentosService(
            FactoryAntesDeGuardar(() => otraPestana.VincularAsync(id, new VincularDocumentoRequest($"{Antiguo}/{CvMarcoTalento}.pdf"))),
            new LectorCurriculum(_rutas), _rutas, Reloj);

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            lenta.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf")));

        Assert.StartsWith("Otra pestaña acaba de cambiar los documentos de esta ficha", ex.Message);
        await using var db = CreateContext();
        var queda = await db.Documentos.SingleAsync();
        Assert.Equal($"{Antiguo}/{CvMarcoTalento}.pdf", queda.RutaRelativa);
        Assert.True(queda.Vigente);
    }

    [Fact]
    public async Task Bd_DosVigentesDelMismoTipo_ElIndiceLoImpide()
    {
        var id = await CandidaturaAsync();
        await using var db = CreateContext();
        Documento Vigente(string ruta) => new()
        {
            CandidaturaId = id, Tipo = TipoDocumento.CV, NombreArchivo = "CV.pdf", RutaRelativa = ruta,
            Vigente = true, FechaAlta = AhoraInicial
        };
        db.Documentos.AddRange(Vigente("a.pdf"), Vigente("b.pdf"));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(ErroresBaseDatos.EsViolacionUnica(error, out _));
    }

    [Fact]
    public async Task Bd_UnVigenteYVariosAnterioresDelMismoTipo_SePermite()
    {
        var id = await CandidaturaAsync();
        await using var db = CreateContext();
        Documento Cv(string ruta, bool vigente) => new()
        {
            CandidaturaId = id, Tipo = TipoDocumento.CV, NombreArchivo = "CV.pdf", RutaRelativa = ruta,
            Vigente = vigente, FechaAlta = AhoraInicial
        };
        db.Documentos.AddRange(Cv("a.pdf", true), Cv("b.pdf", false), Cv("c.pdf", false));

        Assert.Equal(3, await db.SaveChangesAsync());
    }

    [Fact]
    public async Task Vincular_ConOtrasMayusculas_GuardaLasDelDiscoYNoSePuedeDuplicar()
    {
        if (!OperatingSystem.IsWindows()) return; // en Linux las mayúsculas cuentan: es otro archivo
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        var id = await CandidaturaAsync();

        var documento = await _documentos.VincularAsync(id,
            new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf".ToLowerInvariant()));

        Assert.Equal($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf", documento!.RutaRelativa);
        Assert.Equal($"{CvMarcoTalento}.pdf", documento.NombreArchivo);
        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.PDF".ToUpperInvariant())));
        Assert.Equal("Ese documento ya está en la ficha.", ex.Message);
    }

    [Fact]
    public async Task Vincular_EnlaceDuroHaciaDocumentosPersonales_LanzaValidacion()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dni = Crear("Documentos personales/DNI.pdf", "dni");
        var falso = Path.Combine(_carpeta, SemanaMarcoTalento, $"{CvMarcoTalento}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(falso)!);
        RutaSeguraTests.Mklink("/H", falso, dni);
        var id = await CandidaturaAsync();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf")));
        Assert.Equal("rutaRelativa", ex.Campo);
    }

    [Fact]
    public async Task AbrirArchivo_SustituidoPorUnEnlaceDuroDespuesDeVincular_Null()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ruta = Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        var dni = Crear("Documentos personales/DNI.pdf", "dni");
        var documento = await _documentos.VincularAsync(await CandidaturaAsync(), new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));
        File.Delete(ruta);
        RutaSeguraTests.Mklink("/H", ruta, dni);

        Assert.Null(await _documentos.AbrirArchivoAsync(documento!.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ficha_DiceSiHayCarpetaCurriculum(bool hay)
    {
        var id = await CandidaturaAsync();
        if (!hay) Directory.Delete(_carpeta, recursive: true);

        var ficha = await _candidaturas.ObtenerAsync(id);

        Assert.Equal(hay, ficha!.HayCarpetaCurriculum);
        if (!hay) Directory.CreateDirectory(_carpeta); // para el Dispose
    }

    // ---------------------------------------------------------------- quitar

    [Fact]
    public async Task Quitar_ElVigente_ElArchivoSigueYPasaASerVigenteElMasReciente()
    {
        var ruta = Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        Crear($"{Antiguo}/{CvMarcoTalento}.pdf");
        var id = await CandidaturaAsync();
        var antiguo = await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{Antiguo}/{CvMarcoTalento}.pdf"));
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var vigente = await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));

        Assert.True(await _documentos.QuitarAsync(vigente!.Id));

        Assert.True(File.Exists(ruta));
        var queda = Assert.Single((await _candidaturas.ObtenerAsync(id))!.Documentos);
        Assert.Equal(antiguo!.Id, queda.Id);
        Assert.True(queda.Vigente);
    }

    [Fact]
    public async Task Quitar_QueNoExiste_False()
    {
        Assert.False(await _documentos.QuitarAsync(999));
    }

    [Fact]
    public async Task BorrarCandidatura_BorraSusDocumentosPeroNoLosArchivos()
    {
        var ruta = Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        var id = await CandidaturaAsync();
        await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));

        Assert.True(await _candidaturas.BorrarAsync(id));

        await using var db = CreateContext();
        Assert.Empty(db.Documentos);
        Assert.True(File.Exists(ruta));
    }

    // ---------------------------------------------------------------- ficha y archivo

    [Fact]
    public async Task Ficha_ArchivoQueYaNoEsta_SaleComoNoEncontrado()
    {
        var ruta = Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        var id = await CandidaturaAsync();
        await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));
        File.Delete(ruta);

        var documento = Assert.Single((await _candidaturas.ObtenerAsync(id))!.Documentos);

        Assert.False(documento.Encontrado);
    }

    [Fact]
    public async Task Ficha_SinDocumentos_ListaVacia()
    {
        Assert.Empty((await _candidaturas.ObtenerAsync(await CandidaturaAsync()))!.Documentos);
    }

    [Fact]
    public async Task AbrirArchivo_Vinculado_DevuelveElContenido()
    {
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf", "%PDF-1.4 inventado");
        var id = await CandidaturaAsync();
        var documento = await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));

        var leido = await _documentos.AbrirArchivoAsync(documento!.Id);

        Assert.NotNull(leido);
        await using (leido.Contenido)
        {
            using var lector = new StreamReader(leido.Contenido);
            Assert.Equal("%PDF-1.4 inventado", await lector.ReadToEndAsync());
        }
        Assert.Equal("application/pdf", leido.TipoContenido);
    }

    [Fact]
    public async Task AbrirArchivo_CarpetaSustituidaPorUnaUnionDespuesDeVincular_Null()
    {
        Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        Crear($"Documentos personales/{CvMarcoTalento}.pdf", "DNI");
        var id = await CandidaturaAsync();
        var documento = await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));

        // Alguien cambia la carpeta de la semana por una unión hacia «Documentos personales».
        Directory.Delete(Path.Combine(_carpeta, SemanaMarcoTalento), recursive: true);
        RutaSeguraTests.CrearUnion(Path.Combine(_carpeta, SemanaMarcoTalento), Path.Combine(_carpeta, "Documentos personales"));

        Assert.Null(await _documentos.AbrirArchivoAsync(documento!.Id));
        Assert.False(Assert.Single((await _candidaturas.ObtenerAsync(id))!.Documentos).Encontrado);
    }

    [Fact]
    public async Task AbrirArchivo_ArchivoBorrado_Null()
    {
        var ruta = Crear($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf");
        var id = await CandidaturaAsync();
        var documento = await _documentos.VincularAsync(id, new VincularDocumentoRequest($"{SemanaMarcoTalento}/{CvMarcoTalento}.pdf"));
        File.Delete(ruta);

        Assert.Null(await _documentos.AbrirArchivoAsync(documento!.Id));
    }

    [Fact]
    public async Task AbrirArchivo_RutaGuardadaQueNoEsSegura_Null()
    {
        Crear("Documentos personales/DNI.pdf");
        var id = await CandidaturaAsync();
        await using (var db = CreateContext())
        {
            db.Documentos.Add(new Documento
            {
                CandidaturaId = id, Tipo = TipoDocumento.Otro, NombreArchivo = "DNI.pdf",
                RutaRelativa = "Documentos personales/DNI.pdf", FechaAlta = AhoraInicial
            });
            await db.SaveChangesAsync();
        }
        var docId = await CreateContext().Documentos.Select(d => d.Id).SingleAsync();

        Assert.Null(await _documentos.AbrirArchivoAsync(docId));
    }

    [Fact]
    public async Task AbrirArchivo_QueNoExiste_Null()
    {
        Assert.Null(await _documentos.AbrirArchivoAsync(999));
    }
}
