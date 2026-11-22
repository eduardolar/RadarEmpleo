using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Agentes;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="EjecutorClaudeCli"/> de verdad, contra <c>ClaudeSimulado</c> (un proceso que hace de claude.exe):
/// nunca se lanza un Claude real.
/// </summary>
public sealed class EjecutorClaudeCliTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"radar-cli-{Guid.NewGuid():N}");
    private readonly RelojFijo _reloj = new(new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc));

    public EjecutorClaudeCliTests() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); } catch (IOException) { /* temporal */ }
    }

    /// <summary>El ejecutable de ClaudeSimulado, que se copia junto a los tests.</summary>
    private static string Simulado => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "ClaudeSimulado.exe" : "ClaudeSimulado");

    private string Ruta(string nombre) => Path.Combine(_carpeta, nombre);

    private EjecutorClaudeCli Ejecutor(params string[] opcionesSimulado) =>
        EjecutorEn(Simulado, opcionesSimulado);

    private EjecutorClaudeCli EjecutorEn(string ruta, params string[] opcionesSimulado) =>
        new(new OpcionesEjecutor(ruta, opcionesSimulado, _carpeta, 15m, "2.1.185"), _reloj, NullLogger<EjecutorClaudeCli>.Instance);

    private PeticionClaude Peticion(string entrada = "Responde solo OK", IReadOnlyList<string>? argumentos = null, TimeSpan? tiempo = null) =>
        new(TareaAgente.AnalizarEncaje, argumentos ?? ["-p", "--output-format", "json"], _carpeta, entrada,
            tiempo ?? TimeSpan.FromSeconds(60), Ruta("logs/ejecucion-1.log"));

    private static Task<RespuestaClaude> Ejecutar(EjecutorClaudeCli ejecutor, PeticionClaude peticion, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(peticion, (_, _) => { }, cancelacion);

    private sealed record Registro(string[] Argumentos, string Entrada);

    private Registro LeerRegistro() =>
        JsonSerializer.Deserialize<Registro>(File.ReadAllText(Ruta("registro.json"), Encoding.UTF8), FabricaApiJson)!;

    private static readonly JsonSerializerOptions FabricaApiJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task LaEntradaLlegaEnteraEnUtf8()
    {
        var linea = "Peña «Informática» cobra 12 € — sí\n";
        var entrada = new StringBuilder();
        while (entrada.Length < 140_000) entrada.Append(linea);

        var respuesta = await Ejecutar(Ejecutor($"--sim:registro={Ruta("registro.json")}"), Peticion(entrada.ToString()));

        Assert.True(respuesta.Correcta);
        Assert.Equal(entrada.ToString(), LeerRegistro().Entrada);
    }

    [Fact]
    public async Task LosArgumentosLleganUnoAUno()
    {
        string[] argumentos = ["-p", "--json-schema", """{"type":"object","properties":{"a b":{"type":"string"}}}""", "--tools", "",
            "--disallowedTools", "Read(./Documentos personales/**),Edit(./ofertas-enviadas.md)", "con \"comillas\" y \\barras\\"];

        await Ejecutar(Ejecutor($"--sim:registro={Ruta("registro.json")}"), Peticion(argumentos: argumentos));

        Assert.Equal(argumentos, LeerRegistro().Argumentos);
    }

    [Fact]
    public async Task LeeStructuredOutputYElCoste()
    {
        var respuesta = await Ejecutar(Ejecutor(), Peticion());

        Assert.True(respuesta.Correcta);
        Assert.Null(respuesta.Fallo);
        Assert.Equal("success", respuesta.Subtipo);
        Assert.Equal(0.0184m, respuesta.CosteUsd);
        Assert.Equal(3, respuesta.Turnos);
        Assert.Equal("s-sim", respuesta.SesionId);
        Assert.Equal("«Peña» 12 €", respuesta.SalidaEstructurada!.Value.GetProperty("marca").GetString());
    }

    [Fact]
    public async Task SinStructuredOutput_LeeResultConVallasComoPlanB()
    {
        var respuesta = await Ejecutar(Ejecutor("--sim:modo=vallas"), Peticion());

        Assert.True(respuesta.Correcta);
        Assert.Equal("Peña", respuesta.SalidaEstructurada!.Value.GetProperty("texto").GetString());
    }

    [Fact]
    public async Task SalidaQueNoEsJson_DaSalidaNoValida()
    {
        var respuesta = await Ejecutar(Ejecutor("--sim:modo=basura"), Peticion());
        Assert.False(respuesta.Correcta);
        Assert.Equal(FalloClaude.SalidaNoValida, respuesta.Fallo);
    }

    [Fact]
    public async Task CodigoDeSalidaUno_DaCodigoSalida()
    {
        var respuesta = await Ejecutar(Ejecutor("--sim:modo=codigo1"), Peticion());
        Assert.Equal(FalloClaude.CodigoSalida, respuesta.Fallo);
    }

    [Fact]
    public async Task PresupuestoAgotado_ConCodigoUnoYJson_DaPresupuestoAgotadoConSuCoste()
    {
        var respuesta = await Ejecutar(Ejecutor("--sim:modo=presupuesto"), Peticion());
        Assert.False(respuesta.Correcta);
        Assert.Equal(FalloClaude.PresupuestoAgotado, respuesta.Fallo);
        Assert.Equal("error_max_budget_usd", respuesta.Subtipo);
        Assert.Equal(0.0167m, respuesta.CosteUsd);
    }

    [Fact]
    public async Task RutaQueNoExiste_DaNoInstalado()
    {
        var respuesta = await Ejecutar(EjecutorEn(Ruta("no-existe/claude.exe")), Peticion());
        Assert.Equal(FalloClaude.NoInstalado, respuesta.Fallo);
    }

    [Fact]
    public async Task AlEmpezar_RecibeElPidDelProceso()
    {
        var pid = 0;
        var inicio = DateTime.MinValue;
        await Ejecutor().EjecutarAsync(Peticion(), (p, i) => { pid = p; inicio = i; }, CancellationToken.None);

        Assert.True(pid > 0);
        Assert.Equal(DateTimeKind.Utc, inicio.Kind);
        Assert.True(Math.Abs((DateTime.UtcNow - inicio).TotalMinutes) < 5);
    }

    [Fact]
    public async Task TiempoAgotado_MataAlProcesoYASuHijo()
    {
        var pid = 0;
        var cronometro = Stopwatch.StartNew();
        var respuesta = await Ejecutor("--sim:modo=hijo", $"--sim:pidhijo={Ruta("hijo.txt")}")
            .EjecutarAsync(Peticion(tiempo: TimeSpan.FromSeconds(3)), (p, _) => pid = p, CancellationToken.None);

        Assert.Equal(FalloClaude.TiempoAgotado, respuesta.Fallo);
        Assert.Null(respuesta.CosteUsd);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(30));
        await ComprobarMuertosAsync(pid, int.Parse(File.ReadAllText(Ruta("hijo.txt"))));
    }

    [Fact]
    public async Task Cancelacion_MataAlProcesoYASuHijo()
    {
        var pid = 0;
        using var cancelar = new CancellationTokenSource();
        var ejecucion = Ejecutor("--sim:modo=hijo", $"--sim:pidhijo={Ruta("hijo.txt")}")
            .EjecutarAsync(Peticion(), (p, _) => pid = p, cancelar.Token);

        // Se cancela cuando el hijo ya existe.
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (!File.Exists(Ruta("hijo.txt")) && DateTime.UtcNow < limite) await Task.Delay(100);
        await Task.Delay(200);
        cancelar.Cancel();
        var respuesta = await ejecucion;

        Assert.Equal(FalloClaude.Cancelado, respuesta.Fallo);
        await ComprobarMuertosAsync(pid, int.Parse(File.ReadAllText(Ruta("hijo.txt"))));
    }

    [Fact]
    public async Task UnHijoQueSeQuedaConLaSalida_MuereAlSalirElPrincipalYNoSeEspera()
    {
        // El Job Object solo existe en Windows, que es donde la app lanza los agentes: en Linux (el CI) no hay nada que probar.
        if (!OperatingSystem.IsWindows()) return;
        var cronometro = Stopwatch.StartNew();
        var respuesta = await Ejecutar(Ejecutor("--sim:modo=hijoysale", $"--sim:pidhijo={Ruta("hijo.txt")}"), Peticion());

        Assert.True(respuesta.Correcta);
        // Sin cerrar el Job antes de leer, la lectura esperaría al hijo (o a los 10 s de margen).
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(8), $"Ha tardado {cronometro.Elapsed}.");
        if (OperatingSystem.IsWindows()) await ComprobarMuertosAsync(int.Parse(File.ReadAllText(Ruta("hijo.txt"))));
    }

    [Fact]
    public async Task UnaSalidaEnorme_SeRecortaYDaSalidaNoValida()
    {
        var respuesta = await Ejecutar(Ejecutor("--sim:modo=grande"), Peticion());

        Assert.Equal(FalloClaude.SalidaNoValida, respuesta.Fallo);
        Assert.True(new FileInfo(Ruta("logs/ejecucion-1.log")).Length < EjecutorClaudeCli.MaximoSalida + 1024 * 1024);
    }

    [Fact]
    public async Task ElLogNoContieneElPrompt()
    {
        await Ejecutar(Ejecutor(), Peticion("DATO-SECRETO-DEL-PROMPT", ["-p", "--tools", ""]));

        var log = await File.ReadAllTextAsync(Ruta("logs/ejecucion-1.log"));
        Assert.DoesNotContain("DATO-SECRETO-DEL-PROMPT", log);
        Assert.Contains("-p --tools \"\"", log);
        Assert.Contains("--- stdout ---", log);
    }

    [Fact]
    public async Task ConLaSalidaPrivada_ElLogNoGuardaResultNiStructuredOutputSoloSuResumen()
    {
        const string Centinela = "EMPRESA-CENTINELA-DEL-REGISTRO";
        var salida = $$$"""{"type":"result","subtype":"success","is_error":false,"num_turns":3,"total_cost_usd":0.42,"result":"{{{Centinela}}}","structured_output":{"cita":"{{{Centinela}}}"}}""";
        File.WriteAllText(Ruta("respuesta.json"), salida);

        var respuesta = await Ejecutar(Ejecutor("--sim:modo=respuesta", $"--sim:respuesta={Ruta("respuesta.json")}"),
            Peticion() with { SalidaPrivada = true });

        Assert.True(respuesta.Correcta);
        Assert.Equal(Centinela, respuesta.SalidaEstructurada!.Value.GetProperty("cita").GetString());
        var log = await File.ReadAllTextAsync(Ruta("logs/ejecucion-1.log"));
        Assert.DoesNotContain(Centinela, log);
        Assert.Contains("subtype success, coste 0.42, turnos 3.", log);
        Assert.Contains($"{Encoding.UTF8.GetByteCount(salida)} bytes, sha256 ", log);
    }

    [Fact]
    public async Task Version_LaLeeYLaGuarda()
    {
        var ejecutor = Ejecutor();
        Assert.Equal("2.1.185", await ejecutor.VersionAsync(CancellationToken.None));
        Assert.Equal("2.1.185", await ejecutor.VersionAsync(CancellationToken.None));
        Assert.Null(await EjecutorEn(Ruta("no-existe.exe")).VersionAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"type":"result","subtype":"success","is_error":false,"result":"Hola","total_cost_usd":0.01}""", 0, null)]
    [InlineData("""{"type":"result","subtype":"error_during_execution","is_error":true}""", 1, FalloClaude.ErrorDeClaude)]
    [InlineData("""{"type":"result","subtype":"success","is_error":false}""", 2, FalloClaude.CodigoSalida)]
    [InlineData("""[1]""", 0, FalloClaude.SalidaNoValida)]
    public void Interpretar(string salida, int codigo, FalloClaude? fallo)
    {
        var respuesta = EjecutorClaudeCli.Interpretar(salida, codigo);
        Assert.Equal(fallo, respuesta.Fallo);
        Assert.Equal(fallo is null, respuesta.Correcta);
    }

    private static async Task ComprobarMuertosAsync(params int[] pids)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (pids.Any(Vivo) && DateTime.UtcNow < limite) await Task.Delay(100);
        Assert.All(pids, pid => Assert.False(Vivo(pid), $"El proceso {pid} sigue vivo."));
    }

    private static bool Vivo(int pid)
    {
        try
        {
            using var proceso = Process.GetProcessById(pid);
            return !proceso.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
