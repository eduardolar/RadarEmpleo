using System.Text;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary><see cref="LectorCurriculum"/> y <see cref="RutasCurriculum"/> sobre una carpeta temporal.</summary>
public sealed class LectorCurriculumTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"radar-lector-{Guid.NewGuid():N}");
    private readonly string _registro;
    private readonly LectorCurriculum _lector;

    public LectorCurriculumTests()
    {
        Directory.CreateDirectory(_carpeta);
        var rutas = new RutasCurriculum(_carpeta);
        _registro = rutas.Registro!;
        _lector = new LectorCurriculum(rutas);
    }

    public void Dispose()
    {
        foreach (var enlace in Directory.EnumerateDirectories(_carpeta, "*", SearchOption.AllDirectories)
                     .Where(c => new DirectoryInfo(c).LinkTarget is not null).ToList())
            Directory.Delete(enlace);
        Directory.Delete(_carpeta, recursive: true);
    }

    private string Crear(string relativa, string contenido = "x")
    {
        var ruta = Path.Combine(_carpeta, relativa);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, contenido);
        return ruta;
    }

    [Fact]
    public void Contrato_SoloTieneMetodosDeLectura()
    {
        var metodos = typeof(ILectorCurriculum).GetMethods().Concat(typeof(LectorCurriculum).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));

        Assert.All(metodos, m => Assert.StartsWith("Leer", m.Name));
        Assert.Empty(typeof(ILectorCurriculum).GetProperties());
    }

    [Fact]
    public async Task LeerRegistroAsync_FicheroUtf8SinBomConCrlf_LoDevuelveTalCual()
    {
        var texto = "# Candidaturas\r\n\r\nInformática en Málaga, señor\r\n";
        await File.WriteAllTextAsync(_registro, texto, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.Equal(texto, await _lector.LeerRegistroAsync());
    }

    [Fact]
    public async Task LeerRegistroAsync_FicheroAbiertoPorOtroQueEscribe_LoLeeIgual()
    {
        await File.WriteAllTextAsync(_registro, "contenido");
        await using var otro = new FileStream(_registro, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        Assert.Equal("contenido", await _lector.LeerRegistroAsync());
    }

    [Fact]
    public async Task LeerRegistroAsync_NoCambiaElFichero()
    {
        File.Copy(FabricaApi.RutaRegistroEjemplo, _registro);
        var bytes = await File.ReadAllBytesAsync(_registro);
        var fecha = File.GetLastWriteTimeUtc(_registro);

        await _lector.LeerRegistroAsync();

        Assert.Equal(bytes, await File.ReadAllBytesAsync(_registro));
        Assert.Equal(fecha, File.GetLastWriteTimeUtc(_registro));
    }

    [Fact]
    public async Task LeerRegistroAsync_SinElRegistro_LanzaReglaNegocioConMensajeClaro()
    {
        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => _lector.LeerRegistroAsync());

        Assert.Contains(RutasCurriculum.ArchivoRegistro, ex.Message);
        Assert.DoesNotContain(_carpeta, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LeerRegistroAsync_SinCarpetaConfigurada_LanzaReglaNegocio(string? carpeta)
    {
        var lector = new LectorCurriculum(new RutasCurriculum(carpeta));

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => lector.LeerRegistroAsync());
        Assert.Contains("carpeta Curriculum", ex.Message);
    }

    [Fact]
    public async Task LeerRegistroAsync_CarpetaQueNoExiste_LanzaReglaNegocio()
    {
        var lector = new LectorCurriculum(new RutasCurriculum(Path.Combine(_carpeta, "no-existe")));

        await Assert.ThrowsAsync<ReglaNegocioException>(() => lector.LeerRegistroAsync());
    }

    [Fact]
    public void Rutas_ConCarpeta_ApuntanAlRegistroYAlRadarDentroDeElla()
    {
        var rutas = new RutasCurriculum(_carpeta);

        Assert.True(rutas.HayCarpeta);
        Assert.Equal(Path.Combine(_carpeta, "ofertas-enviadas.md"), rutas.Registro);
        Assert.Equal(Path.Combine(_carpeta, "radar", "candidaturas.md"), rutas.Radar);
    }

    [Fact]
    public void Rutas_SinCarpeta_SonNull()
    {
        var rutas = new RutasCurriculum(null);

        Assert.False(rutas.HayCarpeta);
        Assert.Null(rutas.Carpeta);
        Assert.Null(rutas.Registro);
        Assert.Null(rutas.Radar);
        Assert.Null(rutas.Perfil);
        Assert.Null(rutas.CarpetaRadar);
    }

    // ---------------------------------------------------------------- perfil

    [Fact]
    public async Task LeerPerfilAsync_ConElPerfil_DevuelveElTextoYSuFecha()
    {
        var ruta = Crear(RutasCurriculum.ArchivoPerfil, "# Perfil\n\n## Filtros de búsqueda\n");
        var fecha = new DateTime(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(ruta, fecha);

        var perfil = await _lector.LeerPerfilAsync();

        Assert.Equal("# Perfil\n\n## Filtros de búsqueda\n", perfil.Texto);
        Assert.Equal(fecha, perfil.FechaModificacion);
        Assert.Equal(fecha, File.GetLastWriteTimeUtc(ruta));
    }

    [Fact]
    public async Task LeerPerfilAsync_SinElPerfil_LanzaReglaNegocioSinLaRuta()
    {
        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => _lector.LeerPerfilAsync());

        Assert.Contains(RutasCurriculum.ArchivoPerfil, ex.Message);
        Assert.DoesNotContain(_carpeta, ex.Message);
    }

    [Fact]
    public async Task LeerPerfilAsync_SinCarpeta_LanzaReglaNegocio()
    {
        var lector = new LectorCurriculum(new RutasCurriculum(null));

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => lector.LeerPerfilAsync());
        Assert.Contains("carpeta Curriculum", ex.Message);
    }

    // ---------------------------------------------------------------- documentos disponibles

    private const string Semana = "Candidaturas/Semana 2026-08-31 (31 ago - 6 sep)";

    [Fact]
    public void LeerDocumentosDisponibles_SoloLasCarpetasPermitidasYConSuProfundidad()
    {
        Crear("CV_General.pdf");
        Crear("CV_General.docx");
        Crear("perfil-busqueda.md");
        Crear(Semana + "/CV_NET_MarcoTalento.pdf");
        Crear(Semana + "/Carta_Presentacion_X.docx");
        Crear(Semana + "/Entrevista_X.md");
        Crear(Semana + "/Dosier.pptx");
        Crear(Semana + "/Sub/CV_Profundo.pdf");
        Crear("Candidaturas/Suelto.pdf");
        Crear("Candidaturas/Otra carpeta/CV_Otra.pdf");
        Crear("Archivo/CV_Archivo.pdf");
        Crear("Archivo/_estilo_antiguo/CV_NET_MarcoTalento.pdf");
        Crear("Archivo/_estilo_antiguo/Mas/CV_Tercer_Nivel.pdf");
        Crear("Archivo/__pycache__/CV_Cache.pdf");
        Crear("Documentos personales/DNI.pdf");
        Crear("Marca personal/Post.pdf");
        Crear("radar/candidaturas.pdf");

        var rutas = _lector.LeerDocumentosDisponibles().Select(a => a.RutaRelativa).Order(StringComparer.Ordinal);

        Assert.Equal(new[]
        {
            "Archivo/CV_Archivo.pdf",
            "Archivo/_estilo_antiguo/CV_NET_MarcoTalento.pdf",
            "CV_General.docx",
            "CV_General.pdf",
            Semana + "/CV_NET_MarcoTalento.pdf",
            Semana + "/Carta_Presentacion_X.docx"
        }.Order(StringComparer.Ordinal), rutas);
    }

    [Fact]
    public void LeerDocumentosDisponibles_DevuelveCarpetaNombreYFecha()
    {
        var ruta = Crear(Semana + "/CV_NET_MarcoTalento.pdf");
        var fecha = new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(ruta, fecha);
        Crear("CV_General.pdf");

        var archivos = _lector.LeerDocumentosDisponibles();

        var cv = Assert.Single(archivos, a => a.NombreArchivo == "CV_NET_MarcoTalento.pdf");
        Assert.Equal(Semana, cv.Carpeta);
        Assert.Equal(Semana + "/CV_NET_MarcoTalento.pdf", cv.RutaRelativa);
        Assert.Equal(fecha, cv.FechaModificacion);
        Assert.Equal("", Assert.Single(archivos, a => a.NombreArchivo == "CV_General.pdf").Carpeta);
    }

    [Fact]
    public void LeerDocumentosDisponibles_SaltaLasCarpetasQueSonUniones()
    {
        var fuera = Path.Combine(_carpeta, "Documentos personales");
        Crear("Documentos personales/DNI.pdf");
        Directory.CreateDirectory(Path.Combine(_carpeta, "Candidaturas"));
        RutaSeguraTests.CrearUnion(Path.Combine(_carpeta, "Candidaturas", "Semana 2026-09-07 (7 - 13 sep)"), fuera);
        RutaSeguraTests.CrearUnion(Path.Combine(_carpeta, "Archivo"), fuera);

        Assert.Empty(_lector.LeerDocumentosDisponibles());
    }

    [Fact]
    public void LeerDocumentosDisponibles_CarpetaVacia_ListaVacia()
    {
        Assert.Empty(_lector.LeerDocumentosDisponibles());
    }

    [Fact]
    public void LeerDocumentosDisponibles_SinCarpeta_LanzaReglaNegocio()
    {
        var lector = new LectorCurriculum(new RutasCurriculum(Path.Combine(_carpeta, "no-existe")));

        Assert.Throws<ReglaNegocioException>(() => lector.LeerDocumentosDisponibles());
    }

    // ---------------------------------------------------------------- abrir un documento

    [Theory]
    [InlineData("CV_NET_MarcoTalento.pdf", "application/pdf")]
    [InlineData("CV_NET_MarcoTalento.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task LeerDocumento_ArchivoPermitido_LoAbreConSuTipo(string nombre, string tipo)
    {
        Crear(Semana + "/" + nombre, "contenido");

        var leido = _lector.LeerDocumento(Semana + "/" + nombre);

        Assert.NotNull(leido);
        await using (leido.Contenido)
        {
            Assert.False(leido.Contenido.CanWrite);
            using var lector = new StreamReader(leido.Contenido);
            Assert.Equal("contenido", await lector.ReadToEndAsync());
        }
        Assert.Equal(nombre, leido.NombreArchivo);
        Assert.Equal(tipo, leido.TipoContenido);
    }

    [Fact]
    public async Task LeerDocumento_AbiertoPorOtroQueEscribe_LoAbreIgualYNoLoBloquea()
    {
        var ruta = Crear(Semana + "/CV.pdf", "pdf");
        await using var otro = new FileStream(ruta, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var leido = _lector.LeerDocumento(Semana + "/CV.pdf");

        Assert.NotNull(leido);
        await using (leido.Contenido)
        {
            otro.SetLength(0); // quien lo tenía abierto puede seguir escribiendo
        }
    }

    [Fact]
    public void LeerDocumento_QueNoEsta_DevuelveNull()
    {
        Assert.Null(_lector.LeerDocumento(Semana + "/No_existe.pdf"));
    }

    [Fact]
    public async Task LeerDocumento_DevuelveLaRutaYElNombreReales()
    {
        Crear(Semana + "/CV_NET_MarcoTalento.pdf");
        var pedida = OperatingSystem.IsWindows()
            ? Semana.ToLowerInvariant() + "/cv_net_marcotalento.PDF"
            : Semana + "/CV_NET_MarcoTalento.pdf";

        var leido = _lector.LeerDocumento(pedida)!;
        await using (leido.Contenido) { }

        Assert.Equal(Semana + "/CV_NET_MarcoTalento.pdf", leido.RutaRelativa);
        Assert.Equal("CV_NET_MarcoTalento.pdf", leido.NombreArchivo);
    }

    [Fact]
    public void LeerDocumento_EnlaceDuroHaciaDocumentosPersonales_LanzaValidacionYNoDejaElArchivoAbierto()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dni = Crear("Documentos personales/DNI.pdf", "dni");
        var falso = Path.Combine(_carpeta, Semana, "CV_falso.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(falso)!);
        RutaSeguraTests.Mklink("/H", falso, dni);

        Assert.Throws<ValidacionException>(() => _lector.LeerDocumento(Semana + "/CV_falso.pdf"));

        File.Delete(falso); // si se hubiera quedado abierto, no se podría borrar
    }

    [Fact]
    public void LeerDocumentosDisponibles_EnLaRaizSoloCvYCartasYNuncaLosBloqueosDeWord()
    {
        Crear("CV_General.pdf");
        Crear("Carta_General.docx");
        Crear("DNI.pdf");
        Crear("Certificado_Discapacidad.pdf");
        Crear("~$CV_General.docx");
        Crear(Semana + "/~$CV_NET_MarcoTalento.docx");
        Crear(Semana + "/CV_NET_MarcoTalento.docx");

        var rutas = _lector.LeerDocumentosDisponibles().Select(a => a.RutaRelativa).Order(StringComparer.Ordinal);

        Assert.Equal(new[] { "CV_General.pdf", "Carta_General.docx", Semana + "/CV_NET_MarcoTalento.docx" }
            .Order(StringComparer.Ordinal), rutas);
    }

    [Theory]
    [InlineData("../fuera.pdf")]
    [InlineData("Documentos personales/DNI.pdf")]
    [InlineData("perfil-busqueda.md")]
    public void LeerDocumento_RutaNoSegura_LanzaValidacion(string ruta)
    {
        Crear("Documentos personales/DNI.pdf");
        Crear("perfil-busqueda.md");

        Assert.Throws<ValidacionException>(() => _lector.LeerDocumento(ruta));
    }

    [Fact]
    public void LeerDocumento_SinCarpeta_DevuelveNull()
    {
        Assert.Null(new LectorCurriculum(new RutasCurriculum(null)).LeerDocumento("CV.pdf"));
    }

    [Fact]
    public async Task Lecturas_NoCambianNingunArchivoDeLaCarpeta()
    {
        Crear(RutasCurriculum.ArchivoPerfil, "perfil");
        Crear(RutasCurriculum.ArchivoRegistro, "registro");
        Crear(Semana + "/CV.pdf", "pdf");
        var antes = Huella();

        await _lector.LeerPerfilAsync();
        await _lector.LeerRegistroAsync();
        _lector.LeerDocumentosDisponibles();
        await using (_lector.LeerDocumento(Semana + "/CV.pdf")!.Contenido) { }
        await _lector.LeerInformesRastreoAsync();

        Assert.Equal(antes, Huella());
    }

    // ---------------------------------------------------------------- informes de rastreo

    [Fact]
    public async Task LeerInformesRastreoAsync_LosMdPorNombreYLosJsonAparte()
    {
        Crear("rastreos/rastreo_2026-11-16.md", "segundo");
        Crear("rastreos/rastreo_2026-11-09.md", "primero");
        Crear("rastreos/rastreo_2026-11-09.json", "{}");
        Crear("rastreos/log_2026-11-09.txt", "log");
        Crear("rastreos/notas.md", "no es un informe");

        var informes = await _lector.LeerInformesRastreoAsync();

        Assert.Equal(
            [new InformeRastreoLeido("rastreos/rastreo_2026-11-09.md", "primero"), new InformeRastreoLeido("rastreos/rastreo_2026-11-16.md", "segundo")],
            informes.Informes);
        Assert.Equal(["rastreos/rastreo_2026-11-09.json"], informes.Json);
        Assert.Empty(informes.NoLeidos);
    }

    [Fact]
    public async Task LeerInformesRastreoAsync_SinCarpetaDeRastreos_TodoVacio()
    {
        var informes = await _lector.LeerInformesRastreoAsync();

        Assert.Empty(informes.Informes);
        Assert.Empty(informes.Json);
    }

    [Fact]
    public async Task LeerInformesRastreoAsync_UnoBloqueado_SeApuntaYSeLeenLosDemas()
    {
        var bloqueado = Crear("rastreos/rastreo_2026-11-09.md", "uno");
        Crear("rastreos/rastreo_2026-11-16.md", "dos");
        await using var exclusivo = new FileStream(bloqueado, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var informes = await _lector.LeerInformesRastreoAsync();

        Assert.Equal("rastreos/rastreo_2026-11-16.md", Assert.Single(informes.Informes).RutaRelativa);
        Assert.Equal(["rastreos/rastreo_2026-11-09.md"], informes.NoLeidos);
    }

    [Fact]
    public async Task LeerInformesRastreoAsync_SinCarpetaCurriculum_LanzaReglaNegocio()
    {
        var lector = new LectorCurriculum(new RutasCurriculum(null));

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => lector.LeerInformesRastreoAsync());
        Assert.Equal(RutasCurriculum.MensajeSinCarpeta, error.Message);
    }

    private string[] Huella() => Directory.GetFiles(_carpeta, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(r => $"{r}|{File.GetLastWriteTimeUtc(r):O}|{Convert.ToHexString(File.ReadAllBytes(r))}")
        .ToArray();

    [Fact]
    public void Rutas_PerfilRadarYRastreos_DentroDeLaCarpeta()
    {
        var rutas = new RutasCurriculum(_carpeta);

        Assert.Equal(Path.Combine(_carpeta, "perfil-busqueda.md"), rutas.Perfil);
        Assert.Equal(Path.Combine(_carpeta, "radar"), rutas.CarpetaRadar);
        Assert.Equal(Path.Combine(_carpeta, "rastreos"), rutas.CarpetaRastreos);
        Assert.Equal(["Candidaturas", "Archivo"], RutasCurriculum.CarpetasPermitidas);
    }
}
