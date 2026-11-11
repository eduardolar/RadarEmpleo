using System.Diagnostics;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="RutaSegura"/> sobre una carpeta Curriculum temporal: cada riesgo del plan de la F5 tiene su
/// caso (rutas absolutas, UNC, «..», flujos alternativos, carpetas no permitidas, enlaces y uniones…).
/// </summary>
public sealed class RutaSeguraTests : IDisposable
{
    private const string Semana = "Semana 2026-08-31 (31 ago - 6 sep)";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), $"radar-ruta-{Guid.NewGuid():N}", "Curriculum");
    private readonly string _fuera;

    public RutaSeguraTests()
    {
        _fuera = Path.Combine(Path.GetDirectoryName(_raiz)!, "Fuera");
        Directory.CreateDirectory(Path.Combine(_raiz, "Candidaturas", Semana));
        Directory.CreateDirectory(Path.Combine(_raiz, "Archivo", "_estilo_antiguo"));
        Directory.CreateDirectory(Path.Combine(_raiz, "Documentos personales"));
        Directory.CreateDirectory(_fuera);
        File.WriteAllText(Path.Combine(_raiz, "Candidaturas", Semana, "CV_MarcoTalento.pdf"), "pdf");
        File.WriteAllText(Path.Combine(_raiz, "Documentos personales", "DNI.pdf"), "dni");
        File.WriteAllText(Path.Combine(_fuera, "secreto.pdf"), "secreto");
    }

    public void Dispose()
    {
        var temporal = Path.GetDirectoryName(_raiz)!;
        // Primero los enlaces, para no borrar a través de ellos lo que hay al otro lado.
        foreach (var carpeta in Directory.EnumerateDirectories(temporal, "*", SearchOption.AllDirectories)
                     .Where(c => new DirectoryInfo(c).LinkTarget is not null).ToList())
            Directory.Delete(carpeta);
        Directory.Delete(temporal, recursive: true);
    }

    private static void AssertNoValida(string raiz, string? ruta)
    {
        var ex = Assert.Throws<ValidacionException>(() => RutaSegura.Resolver(raiz, ruta));
        Assert.Equal("rutaRelativa", ex.Campo);
        Assert.DoesNotContain(raiz, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CV_Eduardo_Lara_Navarro.pdf")]
    [InlineData("CV_Eduardo_Lara_Navarro.DOCX")]
    [InlineData("Candidaturas/" + Semana + "/CV_MarcoTalento.pdf")]
    [InlineData("Candidaturas\\" + Semana + "\\CV_MarcoTalento.pdf")]
    [InlineData("Archivo/_estilo_antiguo/CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf")]
    [InlineData("Archivo/Antiguos/CV Eduardo 2025.pdf")]
    public void Resolver_RutasPermitidas_DevuelveLaRutaDentroDeLaRaiz(string ruta)
    {
        var completa = RutaSegura.Resolver(_raiz, ruta);

        Assert.StartsWith(_raiz + Path.DirectorySeparatorChar, completa);
        Assert.Equal(ruta.Replace('\\', '/'), RutaSegura.Relativa(_raiz, completa));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolver_RutaVacia_LaRechaza(string? ruta) => AssertNoValida(_raiz, ruta);

    [Theory]
    [InlineData(@"C:\Users\Ana\Desktop\Curriculum\CV.pdf")]
    [InlineData("C:/CV.pdf")]
    [InlineData(@"\CV.pdf")]
    [InlineData("/etc/CV.pdf")]
    [InlineData(@"\\servidor\compartida\CV.pdf")]
    [InlineData("//servidor/compartida/CV.pdf")]
    [InlineData(@"\\?\C:\CV.pdf")]
    [InlineData(@"\\.\C:\CV.pdf")]
    [InlineData("C:CV.pdf")]
    public void Resolver_RutasAbsolutasUncYDeUnidad_LasRechaza(string ruta) => AssertNoValida(_raiz, ruta);

    [Theory]
    [InlineData("CV.pdf:secreto")]
    [InlineData("CV.pdf::$DATA")]
    [InlineData("Candidaturas/" + Semana + "/CV_MarcoTalento.pdf:oculto.pdf")]
    public void Resolver_FlujosDeDatosAlternativos_LosRechaza(string ruta) => AssertNoValida(_raiz, ruta);

    [Theory]
    [InlineData("../Fuera/secreto.pdf")]
    [InlineData(@"..\Fuera\secreto.pdf")]
    [InlineData("Candidaturas/../Documentos personales/DNI.pdf")]
    [InlineData("Candidaturas/" + Semana + "/../../CV.pdf")]
    [InlineData("./CV.pdf")]
    [InlineData("Candidaturas/./" + Semana + "/CV_MarcoTalento.pdf")]
    [InlineData("Candidaturas//CV.pdf")]
    [InlineData("Candidaturas/.../CV.pdf")]
    [InlineData(@"..\perfil-busqueda.md")]
    public void Resolver_PuntosYSegmentosVacios_LosRechaza(string ruta) => AssertNoValida(_raiz, ruta);

    [Theory]
    [InlineData("CV<1>.pdf")]
    [InlineData("CV|1.pdf")]
    [InlineData("CV*.pdf")]
    [InlineData("CV?.pdf")]
    [InlineData("CV\".pdf")]
    [InlineData("CV\t.pdf")]
    [InlineData("CV\0.pdf")]
    [InlineData("CV.pdf.")]
    [InlineData("CV.pdf ")]
    [InlineData(" CV.pdf")]
    [InlineData("Candidaturas /CV.pdf")]
    public void Resolver_CaracteresNoValidosOEspaciosYPuntosFinales_LosRechaza(string ruta) => AssertNoValida(_raiz, ruta);

    [Theory]
    [InlineData("perfil-busqueda.md")]
    [InlineData("ofertas-enviadas.md")]
    [InlineData("_rastreo_semanal.ps1")]
    [InlineData("CV.doc")]
    [InlineData("CV.pdf.exe")]
    [InlineData("Candidaturas/" + Semana + "/Mensaje_Vectech.txt")]
    [InlineData("Candidaturas/" + Semana + "/Dosier.pptx")]
    [InlineData("CV")]
    public void Resolver_ExtensionDistintaDePdfODocx_LaRechaza(string ruta) => AssertNoValida(_raiz, ruta);

    [Theory]
    [InlineData("Documentos personales/DNI.pdf")]
    [InlineData(@"Documentos personales\DNI.pdf")]
    [InlineData("Marca personal/Post.pdf")]
    [InlineData("radar/candidaturas.pdf")]
    [InlineData("Otra carpeta/CV.pdf")]
    public void Resolver_CarpetaNoPermitida_LaRechaza(string ruta) => AssertNoValida(_raiz, ruta);

    [Fact]
    public void Resolver_RutaDemasiadoLarga_LaRechaza() =>
        AssertNoValida(_raiz, "Candidaturas/" + new string('a', 500) + ".pdf");

    [Fact]
    public void Resolver_CarpetaPermitidaConOtrasMayusculasEnWindows_LaAcepta()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.StartsWith(_raiz, RutaSegura.Resolver(_raiz, "candidaturas/" + Semana + "/CV_MarcoTalento.pdf"));
    }

    [Fact]
    public void Resolver_UnionDentroDeUnaCarpetaPermitida_LaRechaza()
    {
        // La unión «Candidaturas\Semana trampa» apunta fuera de Curriculum.
        CrearUnion(Path.Combine(_raiz, "Candidaturas", "Semana trampa"), _fuera);
        Assert.True(File.Exists(Path.Combine(_raiz, "Candidaturas", "Semana trampa", "secreto.pdf")));

        AssertNoValida(_raiz, "Candidaturas/Semana trampa/secreto.pdf");
    }

    [Fact]
    public void Resolver_CarpetaPermitidaQueEsUnaUnion_LaRechaza()
    {
        // «Archivo» entero es una unión hacia «Documentos personales».
        Directory.Delete(Path.Combine(_raiz, "Archivo"), recursive: true);
        CrearUnion(Path.Combine(_raiz, "Archivo"), Path.Combine(_raiz, "Documentos personales"));

        AssertNoValida(_raiz, "Archivo/DNI.pdf");
    }

    [Fact]
    public void Resolver_ArchivoQueEsUnEnlaceSimbolico_LoRechaza()
    {
        var enlace = Path.Combine(_raiz, "Candidaturas", Semana, "CV_enlace.pdf");
        try
        {
            File.CreateSymbolicLink(enlace, Path.Combine(_fuera, "secreto.pdf"));
        }
        catch (IOException)
        {
            return; // Windows sin modo de desarrollador no deja crear enlaces simbólicos: lo cubre la unión.
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        AssertNoValida(_raiz, "Candidaturas/" + Semana + "/CV_enlace.pdf");
    }

    [Fact]
    public void Existe_ArchivoEnSuSitio_True()
    {
        Assert.True(RutaSegura.Existe(_raiz, "Candidaturas/" + Semana + "/CV_MarcoTalento.pdf"));
    }

    [Theory]
    [InlineData("Candidaturas/" + Semana + "/No_existe.pdf")]
    [InlineData("Documentos personales/DNI.pdf")]
    [InlineData("../Fuera/secreto.pdf")]
    [InlineData(null)]
    public void Existe_ArchivoQueFaltaORutaNoValida_FalseSinLanzar(string? ruta)
    {
        Assert.False(RutaSegura.Existe(_raiz, ruta));
    }

    [Fact]
    public void Existe_SinRaiz_False()
    {
        Assert.False(RutaSegura.Existe(null, "CV.pdf"));
    }

    // ---------------------------------------------------------------- raíz de Curriculum

    [Theory]
    [InlineData("DNI.pdf")]
    [InlineData("DNI_escaneado.pdf")]
    [InlineData("Certificado.docx")]
    [InlineData("cv.pdf")]
    [InlineData("CVPerez.pdf")]
    public void Resolver_EnLaRaizLoQueNoEsCvNiCarta_LoRechaza(string ruta) => AssertNoValida(_raiz, ruta);

    [Theory]
    [InlineData("CV_Eduardo_Lara_Navarro.pdf")]
    [InlineData("cv_minusculas.pdf")]
    [InlineData("Carta_Presentacion_General.docx")]
    public void Resolver_EnLaRaizUnCvOUnaCarta_LoAcepta(string ruta)
    {
        Assert.StartsWith(_raiz, RutaSegura.Resolver(_raiz, ruta));
    }

    [Fact]
    public void Resolver_DentroDeLasCarpetasPermitidas_NoHaceFaltaElPrefijo()
    {
        Assert.StartsWith(_raiz, RutaSegura.Resolver(_raiz, "Archivo/Antiguos/General.docx"));
    }

    [Theory]
    [InlineData("Candidaturas/" + Semana + "/~$CV_MarcoTalento.docx")]
    [InlineData("~$CV_General.docx")]
    public void Resolver_ArchivoDeBloqueoDeWord_LoRechaza(string ruta) => AssertNoValida(_raiz, ruta);

    // ---------------------------------------------------------------- el archivo ya abierto

    private static FileStream Abrir(string ruta) =>
        new(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private void AssertAbiertoNoValido(string rutaCompleta)
    {
        using var abierto = Abrir(rutaCompleta);
        var ex = Assert.Throws<ValidacionException>(() => RutaSegura.ComprobarAbierto(_raiz, abierto));
        Assert.Equal("rutaRelativa", ex.Campo);
    }

    [Fact]
    public void ComprobarAbierto_ArchivoNormal_DevuelveLaRutaRealConLasMayusculasDelDisco()
    {
        var pedida = OperatingSystem.IsWindows()
            ? "candidaturas/" + Semana.ToLowerInvariant() + "/cv_marcotalento.PDF"
            : "Candidaturas/" + Semana + "/CV_MarcoTalento.pdf";
        using var abierto = Abrir(RutaSegura.Resolver(_raiz, pedida));

        Assert.Equal("Candidaturas/" + Semana + "/CV_MarcoTalento.pdf", RutaSegura.ComprobarAbierto(_raiz, abierto));
    }

    [Fact]
    public void ComprobarAbierto_AbiertoAtravesDeUnaUnionHaciaFuera_LoRechaza()
    {
        // Lo que pasaría si cambian la carpeta por una unión justo entre Resolver y abrir el archivo.
        CrearUnion(Path.Combine(_raiz, "Candidaturas", "Semana trampa"), _fuera);

        AssertAbiertoNoValido(Path.Combine(_raiz, "Candidaturas", "Semana trampa", "secreto.pdf"));
    }

    [Fact]
    public void ComprobarAbierto_AbiertoAtravesDeUnaUnionHaciaDocumentosPersonales_LoRechaza()
    {
        CrearUnion(Path.Combine(_raiz, "Candidaturas", "Semana trampa"), Path.Combine(_raiz, "Documentos personales"));

        AssertAbiertoNoValido(Path.Combine(_raiz, "Candidaturas", "Semana trampa", "DNI.pdf"));
    }

    [Fact]
    public void ComprobarAbierto_ArchivoConUnEnlaceDuro_LoRechazaAunqueLaRutaSeaBuena()
    {
        if (!OperatingSystem.IsWindows()) return; // la comprobación de enlaces duros es solo de Windows

        // «CV_falso.pdf» es otro nombre del mismo archivo que «Documentos personales\DNI.pdf».
        var falso = Path.Combine(_raiz, "Candidaturas", Semana, "CV_falso.pdf");
        Mklink("/H", falso, Path.Combine(_raiz, "Documentos personales", "DNI.pdf"));
        var resuelta = RutaSegura.Resolver(_raiz, "Candidaturas/" + Semana + "/CV_falso.pdf");

        AssertAbiertoNoValido(resuelta);
    }

    [Fact]
    public void ComprobarAbierto_NombreCortoQueEscondeOtraExtension_LoRechaza()
    {
        if (!OperatingSystem.IsWindows()) return;

        // «notas.pdfx» tiene el nombre corto «NOTAS~1.PDF»: pasa Resolver, pero lo que se abre no es un PDF.
        var real = Path.Combine(_raiz, "Candidaturas", Semana, "notas.pdfx");
        File.WriteAllText(real, "no es un pdf");
        var corto = Path.GetFileName(NombreCorto(real));
        if (string.Equals(corto, "notas.pdfx", StringComparison.OrdinalIgnoreCase))
            return; // este disco no genera nombres cortos (8.3): el caso no se puede dar
        Assert.EndsWith(".PDF", corto, StringComparison.OrdinalIgnoreCase);

        // Primera barrera: GetFullPath expande el nombre corto y la ruta final ya no es un PDF.
        AssertNoValida(_raiz, "Candidaturas/" + Semana + "/" + corto);
        // Segunda: aunque se llegara a abrir, la ruta real del archivo abierto tampoco lo es.
        AssertAbiertoNoValido(Path.Combine(_raiz, "Candidaturas", Semana, corto));
    }

    [Fact]
    public void ComprobarAbierto_NombreCortoDeUnPdfDeVerdad_DevuelveElNombreLargo()
    {
        if (!OperatingSystem.IsWindows()) return;

        var real = Path.Combine(_raiz, "Candidaturas", Semana, "CV_Nombre_Largo_MarcoTalento.pdf");
        File.WriteAllText(real, "pdf");
        var corto = Path.GetFileName(NombreCorto(real));
        if (string.Equals(corto, "CV_Nombre_Largo_MarcoTalento.pdf", StringComparison.OrdinalIgnoreCase)) return;
        using var abierto = Abrir(RutaSegura.Resolver(_raiz, "Candidaturas/" + Semana + "/" + corto));

        Assert.Equal("Candidaturas/" + Semana + "/CV_Nombre_Largo_MarcoTalento.pdf", RutaSegura.ComprobarAbierto(_raiz, abierto));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetShortPathName(string larga, char[] corta, uint largo);

    private static string NombreCorto(string ruta)
    {
        var bufer = new char[1024];
        var largo = GetShortPathName(ruta, bufer, (uint)bufer.Length);
        Assert.NotEqual(0u, largo);
        return new string(bufer, 0, (int)largo);
    }

    /// <summary>
    /// Unión de directorios: en Windows con <c>mklink /J</c>, que no pide permisos de administrador, y sin
    /// ventana; en Linux, un enlace simbólico.
    /// </summary>
    internal static void CrearUnion(string enlace, string destino)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(enlace, destino);
            return;
        }

        Mklink("/J", enlace, destino);
        Assert.NotNull(new DirectoryInfo(enlace).LinkTarget);
    }

    /// <summary><c>mklink</c> por cmd, sin ventana: «/J» para uniones y «/H» para enlaces duros (sin administrador).</summary>
    internal static void Mklink(string opcion, string enlace, string destino)
    {
        var inicio = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argumento in new[] { "/c", "mklink", opcion, enlace, destino })
            inicio.ArgumentList.Add(argumento);
        using var proceso = Process.Start(inicio)!;
        proceso.StandardOutput.ReadToEnd();
        proceso.StandardError.ReadToEnd();
        proceso.WaitForExit();
        Assert.Equal(0, proceso.ExitCode);
    }
}
