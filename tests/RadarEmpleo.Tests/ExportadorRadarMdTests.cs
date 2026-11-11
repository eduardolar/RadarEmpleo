using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="ExportadorRadarMd"/> sobre SQLite en memoria y una carpeta temporal. El reloj del arnés
/// marca el 27/10/2026 a las 10:00 UTC (11:00 en Madrid).
/// </summary>
public sealed class ExportadorRadarMdTests : ArnesSqliteTests, IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"radar-exportador-{Guid.NewGuid():N}");
    private readonly RutasCurriculum _rutas;
    private readonly ExportadorRadarMd _exportador;

    public ExportadorRadarMdTests()
    {
        Directory.CreateDirectory(_carpeta);
        _rutas = new RutasCurriculum(_carpeta);
        _exportador = Crear(_rutas);
    }

    void IDisposable.Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
        Dispose();
    }

    private ExportadorRadarMd Crear(RutasCurriculum rutas) =>
        new(Factory, rutas, Reloj, NullLogger<ExportadorRadarMd>.Instance);

    private string Radar => _rutas.Radar!;

    private async Task SembrarAsync(params Candidatura[] candidaturas)
    {
        await using var db = CreateContext();
        db.Candidaturas.AddRange(candidaturas);
        await db.SaveChangesAsync();
    }

    private static Candidatura Nueva(string empresa, string puesto, DateTime fecha) =>
        Candidatura.Nueva(new Empresa { Nombre = empresa, NombreNormalizado = empresa.ToLowerInvariant() }, puesto, fecha, Origen.Manual);

    private static string[] Filas(string contenido) =>
        contenido.Split('\n').Where(l => l.StartsWith("| 20", StringComparison.Ordinal)).ToArray();

    [Fact]
    public async Task Exportar_ConCandidaturas_EscribeExactamenteElFormatoEsperado()
    {
        var enviada = Nueva("Acme", "Backend", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        enviada.Enlace = "https://ejemplo.com/1";
        enviada.CambiarEstado(EstadoCandidatura.Enviada, new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc), Origen.Manual);
        var descartada = Nueva("Globex", "Tester", new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc));
        descartada.CambiarEstado(EstadoCandidatura.Descartada, AhoraInicial, Origen.Manual, MotivoDescarte.Otro, "Piden inglés");
        var cerrada = Nueva("Initech", "Analista", new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc));
        cerrada.CambiarEstado(EstadoCandidatura.Descartada, AhoraInicial, Origen.Manual, MotivoDescarte.Zona);
        await SembrarAsync(enviada, descartada, cerrada);

        Assert.True(await _exportador.ExportarAsync());

        var esperado = """
            # Candidaturas

            NO EDITAR: lo genera RadarEmpleo. Lo que se cambie aquí se pierde.

            Generado el 2026-10-27 11:00 (hora de Madrid).

            | Fecha | Estado | Empresa | Puesto | Motivo de descarte | Enlace |
            |---|---|---|---|---|---|
            | 2026-09-03 | Enviada | Acme | Backend | — | https://ejemplo.com/1 |
            | 2026-09-02 | Descartada | Globex | Tester | Piden inglés | — |
            | 2026-08-01 | Descartada | Initech | Analista | Zona | — |

            """.ReplaceLineEndings("\n");
        Assert.Equal(esperado, await File.ReadAllTextAsync(Radar));
    }

    [Fact]
    public async Task Exportar_DosVecesConLosMismosDatos_ContenidoIdentico()
    {
        var fecha = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        await SembrarAsync(Nueva("Beta", "Uno", fecha), Nueva("Alfa", "Dos", fecha), Nueva("Gamma", "Tres", fecha));

        await _exportador.ExportarAsync();
        var primera = await File.ReadAllBytesAsync(Radar);
        await _exportador.ExportarAsync();

        Assert.Equal(primera, await File.ReadAllBytesAsync(Radar));
        // Misma fecha: desempata el orden de alta, no el de la BD.
        Assert.Equal(["Beta", "Alfa", "Gamma"], Filas(await File.ReadAllTextAsync(Radar)).Select(l => l.Split(" | ")[2]));
    }

    [Fact]
    public async Task Exportar_FechaEnHoraDeMadrid_NoEnUtc()
    {
        // 1/9 a las 22:30 UTC ya es día 2 en Madrid (verano, UTC+2).
        await SembrarAsync(Nueva("Acme", "Backend", new DateTime(2026, 9, 1, 22, 30, 0, DateTimeKind.Utc)));

        await _exportador.ExportarAsync();

        Assert.StartsWith("| 2026-09-02 |", Assert.Single(Filas(await File.ReadAllTextAsync(Radar))));
    }

    [Fact]
    public async Task Exportar_TextosConSaltosYBarras_UnaSolaLineaPorCandidatura()
    {
        var candidatura = Nueva("Acme\r\nSoftware", "Backend\n\n  y | Frontend", AhoraInicial);
        await SembrarAsync(candidatura);

        await _exportador.ExportarAsync();

        var fila = Assert.Single(Filas(await File.ReadAllTextAsync(Radar)));
        Assert.Contains(@"| Acme Software | Backend y \| Frontend |", fila);
    }

    [Theory]
    [InlineData(MotivoDescarte.DecisionPropia, "Decisión propia")]
    [InlineData(MotivoDescarte.Ingles, "Inglés")]
    [InlineData(MotivoDescarte.Titulacion, "Titulación")]
    [InlineData(MotivoDescarte.Cerrada, "Oferta cerrada")]
    [InlineData(MotivoDescarte.Experiencia, "Experiencia")]
    public async Task Exportar_MotivoSinTexto_EnEspanolYNoConElNombreInterno(MotivoDescarte motivo, string texto)
    {
        var descartada = Nueva("Acme", "Backend", AhoraInicial);
        descartada.CambiarEstado(EstadoCandidatura.Descartada, AhoraInicial, Origen.Manual, motivo);
        await SembrarAsync(descartada);

        await _exportador.ExportarAsync();

        Assert.Contains($"| Backend | {texto} |", Assert.Single(Filas(await File.ReadAllTextAsync(Radar))));
    }

    [Theory]
    [InlineData(MotivoDescarte.Ingles, "piden C1", "Inglés: piden C1")]
    [InlineData(MotivoDescarte.DecisionPropia, "no me convence", "Decisión propia: no me convence")]
    [InlineData(MotivoDescarte.Zona, null, "Zona")]
    // «Otro» delante no dice nada: es lo que llevan todas las importadas del registro.
    [InlineData(MotivoDescarte.Otro, "Descartada (piden inglés)", "Descartada (piden inglés)")]
    [InlineData(MotivoDescarte.Otro, null, "Otro")]
    public async Task Exportar_MotivoYTexto_VanJuntos(MotivoDescarte motivo, string? texto, string esperado)
    {
        var descartada = Nueva("Acme", "Backend", AhoraInicial);
        descartada.CambiarEstado(EstadoCandidatura.Descartada, AhoraInicial, Origen.Manual, motivo, texto);
        await SembrarAsync(descartada);

        await _exportador.ExportarAsync();

        Assert.Contains($"| Backend | {esperado} |", Assert.Single(Filas(await File.ReadAllTextAsync(Radar))));
    }

    [Fact]
    public void TextoDelMotivo_TodosLosMotivosTienenSuTextoEnEspanol()
    {
        var textos = Enum.GetValues<MotivoDescarte>().Select(ReglasEstado.Texto).ToList();

        Assert.Equal(["Experiencia", "Inglés", "Titulación", "Zona", "Modalidad", "Perfil", "Salario", "Oferta cerrada", "Decisión propia", "Otro"], textos);
    }

    [Fact]
    public async Task Exportar_NoSacaNotasNiAnalisisNiPega()
    {
        var candidatura = Nueva("Acme", "Backend", AhoraInicial);
        candidatura.Analisis = "ANALISIS-SECRETO";
        candidatura.Pega = "PEGA-SECRETA";
        candidatura.Salario = "SALARIO-SECRETO";
        candidatura.Notas.Add(new Nota { Texto = "NOTA-SECRETA", Fecha = AhoraInicial });
        await SembrarAsync(candidatura);

        await _exportador.ExportarAsync();

        Assert.DoesNotContain("SECRET", await File.ReadAllTextAsync(Radar));
    }

    [Fact]
    public async Task Exportar_Utf8SinBom()
    {
        await SembrarAsync(Nueva("Consultoría", "Técnico", AhoraInicial));

        await _exportador.ExportarAsync();

        var bytes = await File.ReadAllBytesAsync(Radar);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains("Consultoría", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Exportar_SinCandidaturas_EscribeSoloLaCabecera()
    {
        Assert.True(await _exportador.ExportarAsync());

        var contenido = await File.ReadAllTextAsync(Radar);
        Assert.Contains("NO EDITAR: lo genera RadarEmpleo", contenido);
        Assert.Empty(Filas(contenido));
    }

    [Fact]
    public async Task Exportar_SinLaCarpetaRadar_LaCrea()
    {
        Assert.False(Directory.Exists(Path.Combine(_carpeta, "radar")));

        await _exportador.ExportarAsync();

        Assert.True(File.Exists(Radar));
    }

    [Fact]
    public async Task Exportar_NoTocaNingunOtroArchivoNiDejaTemporales()
    {
        var registro = _rutas.Registro!;
        var vecino = Path.Combine(_carpeta, "radar", "otro.md");
        Directory.CreateDirectory(Path.GetDirectoryName(vecino)!);
        await File.WriteAllTextAsync(registro, "registro");
        await File.WriteAllTextAsync(vecino, "vecino");
        var fechaRegistro = File.GetLastWriteTimeUtc(registro);

        await _exportador.ExportarAsync();
        await _exportador.ExportarAsync();

        Assert.Equal("registro", await File.ReadAllTextAsync(registro));
        Assert.Equal("vecino", await File.ReadAllTextAsync(vecino));
        Assert.Equal(fechaRegistro, File.GetLastWriteTimeUtc(registro));
        var archivos = Directory.GetFiles(_carpeta, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal);
        Assert.Equal(new[] { registro, vecino, Radar }.Order(StringComparer.Ordinal), archivos);
    }

    [Fact]
    public async Task Exportar_SobreUnRadarAnterior_LoSustituye()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Radar)!);
        await File.WriteAllTextAsync(Radar, "contenido viejo");

        Assert.True(await _exportador.ExportarAsync());

        Assert.DoesNotContain("contenido viejo", await File.ReadAllTextAsync(Radar));
    }

    [Fact]
    public async Task Exportar_NoSePuedeCrearLaCarpetaRadar_DevuelveFalseSinLanzar()
    {
        // Un archivo llamado «radar» impide crear la carpeta.
        await File.WriteAllTextAsync(Path.Combine(_carpeta, "radar"), "estorbo");

        Assert.False(await _exportador.ExportarAsync());

        Assert.Equal("estorbo", await File.ReadAllTextAsync(Path.Combine(_carpeta, "radar")));
    }

    [Fact]
    public async Task Exportar_NoSePuedeMoverEncimaDelDestino_DevuelveFalseYNoDejaTemporal()
    {
        // Una carpeta con el nombre del radar hace fallar el File.Move.
        Directory.CreateDirectory(Radar);

        Assert.False(await _exportador.ExportarAsync());

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Radar)!));
    }

    [Fact]
    public async Task Exportar_TrasUnFallo_SePuedeVolverAExportar()
    {
        Directory.CreateDirectory(Radar);
        await _exportador.ExportarAsync();
        Directory.Delete(Radar);

        Assert.True(await _exportador.ExportarAsync());

        Assert.True(File.Exists(Radar));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Exportar_SinCarpetaConfigurada_NoHaceNada(string? carpeta)
    {
        Assert.False(await Crear(new RutasCurriculum(carpeta)).ExportarAsync());
    }

    [Fact]
    public async Task Exportar_CarpetaQueNoExiste_NoLaCrea()
    {
        var noExiste = Path.Combine(_carpeta, "no-existe");

        Assert.False(await Crear(new RutasCurriculum(noExiste)).ExportarAsync());

        Assert.False(Directory.Exists(noExiste));
    }

    [Fact]
    public async Task Exportar_VariasALaVez_TodasBienYQuedaElArchivoCompleto()
    {
        await SembrarAsync(Nueva("Acme", "Backend", AhoraInicial));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _exportador.ExportarAsync()));

        Assert.All(resultados, Assert.True);
        Assert.Single(Filas(await File.ReadAllTextAsync(Radar)));
    }

    // ---------------------------------------------------------------- temporales huérfanos

    private string CarpetaRadar => Path.GetDirectoryName(Radar)!;

    private string CrearArchivo(string relativo, TimeSpan antiguedad)
    {
        var ruta = Path.Combine(CarpetaRadar, relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, "x");
        File.SetLastWriteTimeUtc(ruta, Reloj.AhoraUtc - antiguedad);
        return ruta;
    }

    [Fact]
    public void LimpiarTemporales_BorraSoloLosHuerfanosViejosDeRadar()
    {
        var viejo = CrearArchivo($"candidaturas.md.{Guid.NewGuid():N}.tmp", TimeSpan.FromHours(2));
        var viejoEnvios = CrearArchivo($"envios.md.{Guid.NewGuid():N}.tmp", TimeSpan.FromMinutes(11));
        var radar = CrearArchivo("candidaturas.md", TimeSpan.FromDays(3));
        var otro = CrearArchivo("otro.tmp", TimeSpan.FromDays(3));
        var reciente = CrearArchivo($"candidaturas.md.{Guid.NewGuid():N}.tmp", TimeSpan.FromMinutes(5));
        var enSubcarpeta = CrearArchivo(Path.Combine("sub", $"candidaturas.md.{Guid.NewGuid():N}.tmp"), TimeSpan.FromHours(2));
        var mayusculas = CrearArchivo($"candidaturas.md.{Guid.NewGuid().ToString("N").ToUpperInvariant()}.tmp", TimeSpan.FromHours(2));
        var guidCorto = CrearArchivo("candidaturas.md.abc123.tmp", TimeSpan.FromHours(2));

        Assert.Equal(2, _exportador.LimpiarTemporalesHuerfanos());

        Assert.False(File.Exists(viejo));
        Assert.False(File.Exists(viejoEnvios));
        Assert.All(new[] { radar, otro, reciente, enSubcarpeta, mayusculas, guidCorto }, r => Assert.True(File.Exists(r), r));
    }

    [Fact]
    public void LimpiarTemporales_NoTocaNadaFueraDeRadar()
    {
        var fuera = Path.Combine(_carpeta, $"candidaturas.md.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(fuera, "x");
        File.SetLastWriteTimeUtc(fuera, Reloj.AhoraUtc.AddDays(-1));
        Directory.CreateDirectory(CarpetaRadar);

        Assert.Equal(0, _exportador.LimpiarTemporalesHuerfanos());

        Assert.True(File.Exists(fuera));
    }

    [Fact]
    public void LimpiarTemporales_SinCarpetaRadar_DevuelveCeroYNoLaCrea()
    {
        Assert.Equal(0, _exportador.LimpiarTemporalesHuerfanos());

        Assert.False(Directory.Exists(CarpetaRadar));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LimpiarTemporales_SinCarpetaCurriculum_DevuelveCero(string? carpeta)
    {
        Assert.Equal(0, Crear(new RutasCurriculum(carpeta)).LimpiarTemporalesHuerfanos());
    }

    [Fact]
    public async Task LimpiarTemporales_DespuesDeExportar_ElRadarSigueYSePuedeVolverAExportar()
    {
        CrearArchivo($"candidaturas.md.{Guid.NewGuid():N}.tmp", TimeSpan.FromHours(1));
        Assert.True(await _exportador.ExportarAsync());

        Assert.Equal(1, _exportador.LimpiarTemporalesHuerfanos());

        Assert.True(File.Exists(Radar));
        Assert.True(await _exportador.ExportarAsync());
    }
}
