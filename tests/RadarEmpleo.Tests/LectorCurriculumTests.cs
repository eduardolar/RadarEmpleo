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
    public async Task LeerHashAsync_DeUnArchivoConocido_DaSuSha256EnMinusculas()
    {
        await File.WriteAllTextAsync(_registro, "abc", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            await _lector.LeerHashAsync(ArchivoVigilado.Registro));
    }

    [Fact]
    public async Task LeerHashAsync_SinElArchivo_DaAusente()
    {
        Assert.Equal(ILectorCurriculum.HashAusente, await _lector.LeerHashAsync(ArchivoVigilado.Registro));
        Assert.Equal(ILectorCurriculum.HashAusente, await _lector.LeerHashAsync(ArchivoVigilado.Perfil));
    }

    [Fact]
    public async Task LeerHashAsync_ConLaCarpetaConfiguradaPeroQueNoEsta_DaAusente()
    {
        var lector = new LectorCurriculum(new RutasCurriculum(Path.Combine(_carpeta, "no-existe")));
        Assert.Equal(ILectorCurriculum.HashAusente, await lector.LeerHashAsync(ArchivoVigilado.Registro));
    }

    [Fact]
    public async Task LeerHashAsync_SinCarpetaConfigurada_DaNull()
    {
        Assert.Null(await new LectorCurriculum(new RutasCurriculum(null)).LeerHashAsync(ArchivoVigilado.Registro));
    }

    [FactSoloWindows("abrir un archivo en exclusiva (FileShare.None) solo impide leerlo a otros en Windows")]
    public async Task LeerHashAsync_AbiertoEnExclusiva_LanzaYNoDiceAusente()
    {
        await File.WriteAllTextAsync(_registro, "contenido");
        await using var exclusivo = new FileStream(_registro, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<ReglaNegocioException>(() => _lector.LeerHashAsync(ArchivoVigilado.Registro));
    }

    [Fact]
    public async Task LeerHashAsync_FicheroAbiertoPorOtroQueEscribe_NoLoBloquea()
    {
        await File.WriteAllTextAsync(_registro, "contenido");
        await using var otro = new FileStream(_registro, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        Assert.NotNull(await _lector.LeerHashAsync(ArchivoVigilado.Registro));
        // El otro puede seguir escribiendo después.
        otro.Write("más"u8);
    }

    [Fact]
    public async Task TextoLeido_Hash_CoincideConLeerHashAsync()
    {
        File.Copy(FabricaApi.RutaRegistroEjemplo, _registro);
        File.Copy(FabricaApi.RutaPerfilEjemplo, Path.Combine(_carpeta, RutasCurriculum.ArchivoPerfil));

        var registro = await _lector.LeerRegistroConFechaAsync();
        var perfil = await _lector.LeerPerfilAsync();

        Assert.Equal(await _lector.LeerHashAsync(ArchivoVigilado.Registro), registro.Hash);
        Assert.Equal(await _lector.LeerHashAsync(ArchivoVigilado.Perfil), perfil.Hash);
        Assert.Equal(await File.ReadAllTextAsync(_registro), registro.Texto);
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

        // Dice qué falta, cómo se llama y qué hacer, sin la ruta
        Assert.Equal(
            "Falta tu registro de candidaturas (ofertas-enviadas.md) en la carpeta Curriculum. Ponlo ahí y vuelve a intentarlo.",
            ex.Message);
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

    // ---------------------------------------------------------------- codificación y contenido raro del registro

    [Fact]
    public async Task LeerRegistroConFechaAsync_ConBomUtf8_QuitaLaMarcaYElHashEsElDeLosBytes()
    {
        var texto = "| Fecha | Empresa |\n|---|---|\n";
        await File.WriteAllTextAsync(_registro, texto, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var leido = await _lector.LeerRegistroConFechaAsync();

        Assert.Equal(texto, leido.Texto);
        Assert.Equal(await _lector.LeerHashAsync(ArchivoVigilado.Registro), leido.Hash);
    }

    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public async Task LeerRegistroAsync_Utf16OUtf32ConBom_LoLeeConSusTildes(string codificacion)
    {
        var texto = "| Fecha | Empresa |\r\n| 2026-09-01 | Señor Pérez |\r\n";
        await File.WriteAllTextAsync(_registro, texto, Encoding.GetEncoding(codificacion));

        Assert.Equal(texto, await _lector.LeerRegistroAsync());
    }

    public static TheoryData<byte[]> NoUtf8 => new()
    {
        // «Málaga, señor» guardado en Windows-1252.
        { [0x4D, 0xE1, 0x6C, 0x61, 0x67, 0x61, 0x2C, 0x20, 0x73, 0x65, 0xF1, 0x6F, 0x72] },
        // Un byte suelto que nunca es UTF-8, en medio de texto bueno.
        { [.. "Acme "u8, 0xFF, .. " Uno"u8] },
        // Una «é» cortada al final (le falta el segundo byte).
        { [.. "Caf"u8, 0xC3] },
        // Con BOM UTF-8 delante y el resto en Windows-1252 (lo que deja un editor que «convierte» a medias).
        { [0xEF, 0xBB, 0xBF, .. "Se"u8, 0xF1, .. "or"u8] },
        // UTF-16 LE y BE con una mitad de par suelta (U+D800) entre dos letras.
        { [0xFF, 0xFE, 0x41, 0x00, 0x00, 0xD8, 0x42, 0x00] },
        { [0xFE, 0xFF, 0x00, 0x41, 0xD8, 0x00, 0x00, 0x42] },
        // UTF-32 LE con un carácter fuera de Unicode (0x00110000).
        { [0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x00] }
    };

    [Theory]
    [MemberData(nameof(NoUtf8))]
    public async Task LeerRegistroAsync_NoEsUtf8Valido_LanzaReglaNegocioQueLoDiceSinLaRuta(byte[] bytes)
    {
        await File.WriteAllBytesAsync(_registro, bytes);

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => _lector.LeerRegistroAsync());

        Assert.Contains(RutasCurriculum.ArchivoRegistro, ex.Message);
        Assert.Contains("UTF-8", ex.Message);
        Assert.DoesNotContain(_carpeta, ex.Message);
    }

    [Fact]
    public async Task LeerRegistroConFechaAsync_NoEsUtf8Valido_LanzaReglaNegocioYElHashSiSeCalcula()
    {
        await File.WriteAllBytesAsync(_registro, [0x4D, 0xE1, 0x6C, 0x61, 0x67, 0x61]);

        await Assert.ThrowsAsync<ReglaNegocioException>(() => _lector.LeerRegistroConFechaAsync());
        // El hash no decodifica: el aviso de «ha cambiado» sigue funcionando con un archivo así.
        Assert.Matches("^[0-9a-f]{64}$", await _lector.LeerHashAsync(ArchivoVigilado.Registro));
    }

    [Fact]
    public async Task LeerPerfilAsync_NoEsUtf8Valido_LanzaReglaNegocioConSuNombre()
    {
        await File.WriteAllBytesAsync(Path.Combine(_carpeta, RutasCurriculum.ArchivoPerfil), [0x73, 0x65, 0xF1, 0x6F, 0x72]);

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => _lector.LeerPerfilAsync());
        Assert.Contains(RutasCurriculum.ArchivoPerfil, ex.Message);
    }

    [Fact]
    public async Task LeerInformesRastreoAsync_UnoQueNoEsUtf8_SeApuntaAparteDeLosQueNoSeAbrenYSeLeenLosDemas()
    {
        await File.WriteAllBytesAsync(Crear("rastreos/rastreo_2026-11-09.md"), [0x73, 0x65, 0xF1, 0x6F, 0x72]);
        Crear("rastreos/rastreo_2026-11-16.md", "dos");

        var informes = await _lector.LeerInformesRastreoAsync();

        Assert.Equal("rastreos/rastreo_2026-11-16.md", Assert.Single(informes.Informes).RutaRelativa);
        Assert.Equal(["rastreos/rastreo_2026-11-09.md"], informes.NoUtf8);
        Assert.Empty(informes.NoLeidos);
    }

    public static TheoryData<byte[], string> CasiVacios => new()
    {
        { [], "" },
        { [0xEF, 0xBB, 0xBF], "" },
        { [0xFF, 0xFE], "" },
        { "  \r\n\t\n \r"u8.ToArray(), "  \r\n\t\n \r" }
    };

    [Theory]
    [MemberData(nameof(CasiVacios))]
    public async Task LeerRegistroAsync_VacioSoloBomOSoloBlancos_DevuelveElTextoSinLanzar(byte[] bytes, string esperado)
    {
        await File.WriteAllBytesAsync(_registro, bytes);

        Assert.Equal(esperado, await _lector.LeerRegistroAsync());
    }

    [Fact]
    public async Task LeerRegistroAsync_FinalesDeLineaMezclados_LosDejaTalCual()
    {
        var texto = "uno\r\ndos\nañadido\rtres\n\rcuatro";
        await File.WriteAllTextAsync(_registro, texto, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.Equal(texto, await _lector.LeerRegistroAsync());
    }

    // ---------------------------------------------------------------- registro abierto por otro

    [FactSoloWindows("solo Windows impide abrir un archivo que otro tiene sin compartir")]
    public async Task LeerRegistroAsync_AbiertoEnExclusiva_LanzaReglaNegocioDeReintentarSinLaRuta()
    {
        await File.WriteAllTextAsync(_registro, "contenido");
        await using var exclusivo = new FileStream(_registro, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => _lector.LeerRegistroAsync());
        var conFecha = await Assert.ThrowsAsync<ReglaNegocioException>(() => _lector.LeerRegistroConFechaAsync());

        Assert.Contains("Vuelve a intentarlo", ex.Message);
        Assert.Contains(RutasCurriculum.ArchivoRegistro, ex.Message);
        Assert.DoesNotContain(_carpeta, ex.Message);
        Assert.Equal(ex.Message, conFecha.Message);
    }

    [FactSoloWindows("solo Windows impide abrir un archivo que otro tiene sin compartir")]
    public async Task LeerRegistroAsync_AbiertoParaEscribirSinCompartirLaLectura_LanzaReglaNegocio()
    {
        await File.WriteAllTextAsync(_registro, "contenido");
        await using var escritor = new FileStream(_registro, FileMode.Open, FileAccess.Write, FileShare.Write);

        await Assert.ThrowsAsync<ReglaNegocioException>(() => _lector.LeerRegistroAsync());
    }

    [Fact]
    public async Task LeerRegistroConFechaAsync_AbiertoPorUnEditorQueLoComparteTodo_LoLeeYElEditorSigueEscribiendo()
    {
        await File.WriteAllTextAsync(_registro, "antes");
        await using (var editor = new FileStream(_registro, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
        {
            Assert.Equal("antes", (await _lector.LeerRegistroConFechaAsync()).Texto);

            editor.SetLength(0);
            editor.Write("después"u8);
        }

        Assert.Equal("después", await _lector.LeerRegistroAsync());
    }

    [Fact]
    public async Task LeerRegistroAsync_DespuesDeLeer_NoSeQuedaAbiertoYSePuedeSustituir()
    {
        await File.WriteAllTextAsync(_registro, "viejo");
        await _lector.LeerRegistroAsync();

        // Lo que hace un editor al guardar: escribir aparte y cambiar uno por otro.
        var nuevo = Crear("registro.tmp", "nuevo");
        File.Move(nuevo, _registro, overwrite: true);
        await using (new FileStream(_registro, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }

        Assert.Equal("nuevo", await _lector.LeerRegistroAsync());
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

    [FactSoloWindows("los enlaces duros se crean con mklink y se detectan con la API de Windows")]
    public void LeerDocumento_EnlaceDuroHaciaDocumentosPersonales_LanzaValidacionYNoDejaElArchivoAbierto()
    {
        var dni = Crear("Documentos personales/DNI.pdf", "dni");
        var falso = Path.Combine(_carpeta, Semana, "CV_falso.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(falso)!);
        RutaSeguraTests.Mklink("/H", falso, dni);

        Assert.Throws<ValidacionException>(() => _lector.LeerDocumento(Semana + "/CV_falso.pdf"));

        File.Delete(falso); // si se hubiera quedado abierto, no se podría borrar
    }

    [FactSoloWindows("los enlaces duros se crean con mklink y se detectan con la API de Windows")]
    public void LeerDocumentosDisponibles_UnEnlaceDuro_NoSeSugiere()
    {
        var dni = Crear("Documentos personales/DNI.pdf", "dni");
        Crear(Semana + "/CV_NET_MarcoTalento.pdf");
        RutaSeguraTests.Mklink("/H", Path.Combine(_carpeta, Semana, "CV_falso.pdf"), dni);

        var rutas = _lector.LeerDocumentosDisponibles().Select(a => a.RutaRelativa).ToList();

        Assert.Contains(Semana + "/CV_NET_MarcoTalento.pdf", rutas);
        Assert.DoesNotContain(Semana + "/CV_falso.pdf", rutas);
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
