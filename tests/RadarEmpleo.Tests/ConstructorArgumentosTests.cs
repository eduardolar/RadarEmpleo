using RadarEmpleo.Domain;
using RadarEmpleo.Services.Agentes;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="ConstructorArgumentos"/>: la lista exacta de cada tarea (tabla 4.4 del plan de la F7, con lo que fijó el
/// paso 0) y lo que nunca puede aparecer.
/// </summary>
public class ConstructorArgumentosTests
{
    private const string Esquema = """
        {
          "type": "object",
          "properties": { "ok": { "type": "boolean" } }
        }
        """;

    private const string EsquemaMinificado = """{"type":"object","properties":{"ok":{"type":"boolean"}}}""";

    private static readonly string CarpetaDatos = OperatingSystem.IsWindows() ? @"C:\Users\Prueba\AppData\Local\RadarEmpleo" : "/home/prueba/RadarEmpleo";
    private static readonly string ReglaDatos = OperatingSystem.IsWindows() ? "//c/Users/Prueba/AppData/Local/RadarEmpleo" : "//home/prueba/RadarEmpleo";

    private static string Lecturas =>
        $"Read(~/.claude/**),Read({ReglaDatos}/**),Read(./**/appsettings*.json),Read(./Documentos personales/**),Read(./Marca personal/**)";

    private static readonly string[] Comunes =
    [
        "-p", "--output-format", "json", "--json-schema", EsquemaMinificado, "--max-budget-usd", "PRESUPUESTO",
        "--permission-mode", "dontAsk", "--no-session-persistence", "--strict-mcp-config", "--setting-sources", ""
    ];

    private static string[] Con(string presupuesto, params string[] resto) =>
        [.. Comunes.Select(a => a == "PRESUPUESTO" ? presupuesto : a), .. resto];

    private static ValoresArgumentos Valores(string? informe = null) => new(CarpetaDatos, informe);

    private static IReadOnlyList<string> Construir(DefinicionTarea definicion, ValoresArgumentos? valores = null) =>
        ConstructorArgumentos.Construir(definicion, Esquema, valores ?? Valores(informe: "rastreo_2026-11-16.md"));

    [Fact]
    public void AnalizarEncaje_ListaExactaSinNingunaHerramienta()
    {
        // Decisión del 14/11: la app le da la oferta, el perfil y el CV en el prompt; no abre webs ni lee archivos.
        Assert.Equal(Con("0.50", "--tools", ""), Construir(DefinicionesTareas.AnalizarEncaje));
        Assert.Equal(CarpetaTrabajo.Ejecucion, DefinicionesTareas.AnalizarEncaje.Carpeta);
    }

    [Fact]
    public void ExtraerRegistro_ListaExacta()
    {
        Assert.Equal(Con("1.00", "--tools", "", "--model", "sonnet"), Construir(DefinicionesTareas.ExtraerRegistro));
    }

    [Fact]
    public void Rastreo_ListaExacta()
    {
        Assert.Equal(
            Con("3.00",
                "--tools", "Read,Grep,Glob,Write,WebFetch,WebSearch,ToolSearch",
                "--allowedTools", "Read(./**),WebFetch,WebSearch,ToolSearch,Edit(./rastreos/rastreo_2026-11-16_2.md)",
                "--disallowedTools", $"Bash,NotebookEdit,Agent,Skill,Edit(./ofertas-enviadas.md),Edit(./perfil-busqueda.md),{Lecturas}"),
            Construir(DefinicionesTareas.Rastreo, Valores(informe: "rastreo_2026-11-16_2.md")));
    }

    [Fact]
    public void ElEsquemaVaMinificadoEnUnSoloArgumento()
    {
        var argumentos = Construir(DefinicionesTareas.AnalizarEncaje);
        var posicion = argumentos.ToList().IndexOf("--json-schema");
        Assert.Equal(EsquemaMinificado, argumentos[posicion + 1]);
        Assert.DoesNotContain('\n', argumentos[posicion + 1]);
    }

    [Fact]
    public void EsquemaQueNoEsJson_Lanza()
    {
        Assert.Throws<ArgumentException>(() => ConstructorArgumentos.Construir(DefinicionesTareas.AnalizarEncaje, "{no", Valores()));
    }

    public static TheoryData<TareaAgente> Tareas => [TareaAgente.AnalizarEncaje, TareaAgente.ExtraerRegistro, TareaAgente.Rastreo];

    [Theory]
    [MemberData(nameof(Tareas))]
    public void NuncaSaltanLosPermisosNiCarganLaConfiguracionDeUsuario(TareaAgente tarea)
    {
        var argumentos = Construir(DefinicionesTareas.De(tarea)!, Valores("rastreo_2026-11-16.md"));
        var todo = string.Join(" ", argumentos);

        Assert.DoesNotContain("bypassPermissions", todo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dangerously", todo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--chrome", argumentos);
        Assert.DoesNotContain("--mcp-config", argumentos);
        Assert.DoesNotContain("--agent", argumentos);
        Assert.DoesNotContain("mcp__", todo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--resume", argumentos);
        Assert.Contains("--strict-mcp-config", argumentos);
        Assert.Contains("--no-session-persistence", argumentos);
        Assert.Equal("dontAsk", Valor(argumentos, "--permission-mode"));
        // Ninguna fuente de configuración: ni las reglas, ni los hooks, ni los agentes y skills de Eduardo.
        Assert.Equal("", Valor(argumentos, "--setting-sources"));
        var herramientas = (Valor(argumentos, "--tools") ?? "").Split(',');
        var permitidas = (Valor(argumentos, "--allowedTools") ?? "").Split(',');
        foreach (var prohibida in new[] { "Bash", "Agent", "Skill", "PowerShell" })
        {
            Assert.DoesNotContain(prohibida, herramientas);
            Assert.DoesNotContain(permitidas, p => p == prohibida || p.StartsWith(prohibida + "(", StringComparison.Ordinal));
        }
        // Read solo con ruta: dentro de la carpeta de trabajo.
        Assert.DoesNotContain("Read", permitidas);
    }

    [Theory]
    [MemberData(nameof(Tareas))]
    public void LasQueLeenDeniegaLaConfiguracionDeClaudeLaCarpetaDeDatosYAppsettings(TareaAgente tarea)
    {
        var argumentos = Construir(DefinicionesTareas.De(tarea)!);
        if (!(Valor(argumentos, "--tools") ?? "").Contains("Read", StringComparison.Ordinal)) return;
        var denegadas = Valor(argumentos, "--disallowedTools")!.Split(',');

        Assert.Contains("Read(~/.claude/**)", denegadas);
        Assert.Contains($"Read({ReglaDatos}/**)", denegadas);
        Assert.Contains("Read(./**/appsettings*.json)", denegadas);
        Assert.Contains("Read(./Documentos personales/**)", denegadas);
        Assert.Contains("Read(./Marca personal/**)", denegadas);
    }

    [Fact]
    public void Rastreo_SoloPermiteEscribirSuInforme()
    {
        var argumentos = Construir(DefinicionesTareas.Rastreo);
        var permitidas = Valor(argumentos, "--allowedTools")!.Split(',');
        var escrituras = permitidas.Where(p => p.StartsWith("Edit", StringComparison.Ordinal) || p.StartsWith("Write", StringComparison.Ordinal)).ToList();

        Assert.Equal(["Edit(./rastreos/rastreo_2026-11-16.md)"], escrituras);
        var denegadas = Valor(argumentos, "--disallowedTools")!.Split(',');
        Assert.Contains("Edit(./ofertas-enviadas.md)", denegadas);
        Assert.Contains("Edit(./perfil-busqueda.md)", denegadas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../ofertas-enviadas.md")]
    [InlineData("rastreo_*.md")]
    [InlineData("rastreo_2026-11-16.md),Bash(")]
    [InlineData("rastreo_2026-11-16.md\n")]
    [InlineData("informe.md")]
    public void Rastreo_ConUnNombreQueNoEsElDeLaApp_Lanza(string? nombre)
    {
        Assert.Throws<ArgumentException>(() => ConstructorArgumentos.Construir(DefinicionesTareas.Rastreo, Esquema, Valores(informe: nombre)));
    }

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\RadarEmpleo", "//c/Users/x/AppData/Local/RadarEmpleo")]
    [InlineData(@"D:\datos\", "//d/datos")]
    public void RutaRegla_DaLaFormaQueEntiendeClaude(string carpeta, string regla)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(regla, ConstructorArgumentos.RutaRegla(carpeta));
    }

    [Theory]
    [InlineData(@"C:\datos,Bash(x)")]
    [InlineData(@"C:\da(tos)")]
    [InlineData(@"\\servidor\recurso\datos")]
    public void RutaRegla_ConAlgoQueRomperiaLaLista_Lanza(string carpeta)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => ConstructorArgumentos.RutaRegla(carpeta));
    }

    [Fact]
    public void LasTareasDeLaF8EnAdelanteNoTienenDefinicion()
    {
        Assert.Null(DefinicionesTareas.De(TareaAgente.Redactar));
        Assert.Null(DefinicionesTareas.De(TareaAgente.ConfirmarEnvio));
    }

    private static string? Valor(IReadOnlyList<string> argumentos, string opcion)
    {
        var posicion = argumentos.ToList().IndexOf(opcion);
        return posicion < 0 ? null : argumentos[posicion + 1];
    }
}
