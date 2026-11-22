using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace RadarEmpleo.Services.Agentes;

/// <summary>
/// Lanza <c>claude.exe</c> directamente (sin <c>cmd</c>) y sin ventana: los argumentos van uno a uno con
/// <see cref="ProcessStartInfo.ArgumentList"/>, el prompt por la entrada estándar en UTF-8 sin BOM, y la salida y
/// el error se leen a la vez (para que el proceso no se quede bloqueado con la salida llena). Al cortar por tiempo
/// o cancelación mata el árbol entero; en Windows, además, el proceso entra en un <see cref="TrabajoWindows"/> que
/// lo mata con sus hijos si la API muere. El log (<c>logs\ejecucion-&lt;id&gt;.log</c>) guarda los argumentos, el
/// error y la salida (de una <see cref="PeticionClaude.SalidaPrivada"/>, solo su tamaño, hash, subtype, coste y turnos),
/// pero nunca el prompt.
/// </summary>
public sealed partial class EjecutorClaudeCli : IEjecutorClaude
{
    private static readonly UTF8Encoding Utf8SinBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan EsperaTrasMatar = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TiempoVersion = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CacheVersion = TimeSpan.FromHours(1);

    /// <summary>Lo máximo que se guarda de la salida (el resto se lee y se tira, para que el proceso no se bloquee).</summary>
    public const int MaximoSalida = 4 * 1024 * 1024;

    /// <summary>Lo máximo que se guarda del error estándar.</summary>
    public const int MaximoError = 256 * 1024;

    private readonly OpcionesEjecutor _opciones;
    private readonly IClock _reloj;
    private readonly ILogger<EjecutorClaudeCli> _log;
    private readonly SemaphoreSlim _cerrojoVersion = new(1, 1);
    private (string? Version, DateTime Hasta)? _version;

    public EjecutorClaudeCli(OpcionesEjecutor opciones, IClock reloj, ILogger<EjecutorClaudeCli> log)
    {
        _opciones = opciones;
        _reloj = reloj;
        _log = log;
    }

    public async Task<RespuestaClaude> EjecutarAsync(PeticionClaude peticion, Action<int, DateTime> alEmpezar, CancellationToken cancelacion)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(peticion.RutaLog)!);
        await using var log = new StreamWriter(new FileStream(peticion.RutaLog, FileMode.Append, FileAccess.Write, FileShare.Read), Utf8SinBom);
        await log.WriteLineAsync($"[{_reloj.AhoraUtc:O}] {peticion.Tarea} en {peticion.CarpetaTrabajo}");
        // Los argumentos sí (sin datos de Eduardo, salvo el esquema); el prompt nunca.
        await log.WriteLineAsync("Argumentos: " + string.Join(" ", peticion.Argumentos.Select(Citar)));
        await log.WriteLineAsync($"Entrada: {Utf8SinBom.GetByteCount(peticion.EntradaEstandar)} bytes (no se guarda).");

        if (!File.Exists(_opciones.RutaClaude))
        {
            await log.WriteLineAsync("No existe el ejecutable configurado.");
            return RespuestaClaude.Fallida(FalloClaude.NoInstalado);
        }

        using var proceso = new Process { StartInfo = Preparar(peticion.CarpetaTrabajo, _opciones.ArgumentosPrevios.Concat(peticion.Argumentos)) };
        try
        {
            proceso.Start();
        }
        catch (Win32Exception error)
        {
            await log.WriteLineAsync($"No se ha podido lanzar: {error.Message}");
            return RespuestaClaude.Fallida(FalloClaude.NoInstalado, error.Message);
        }

        using var trabajo = MeterEnTrabajo(proceso);
        alEmpezar(proceso.Id, proceso.StartTime.ToUniversalTime());

        var salida = LeerLimitadoAsync(proceso.StandardOutput, MaximoSalida);
        var errores = LeerLimitadoAsync(proceso.StandardError, MaximoError);

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        limite.CancelAfter(peticion.TiempoMaximo);
        FalloClaude? corte = null;
        try
        {
            await EscribirEntradaAsync(proceso, peticion.EntradaEstandar, limite.Token);
            await proceso.WaitForExitAsync(limite.Token);
            // Ha salido el principal: lo que quede de su árbol (un nieto con la salida abierta) muere ya, antes de leer.
            trabajo?.Dispose();
        }
        catch (OperationCanceledException)
        {
            corte = cancelacion.IsCancellationRequested ? FalloClaude.Cancelado : FalloClaude.TiempoAgotado;
            Matar(proceso);
            trabajo?.Dispose();
            await EsperarFinAsync(proceso);
        }

        var textoSalida = await LeerConLimiteAsync(salida);
        var textoError = await LeerConLimiteAsync(errores);
        var codigo = proceso.HasExited ? proceso.ExitCode : (int?)null;
        await log.WriteLineAsync($"[{_reloj.AhoraUtc:O}] Fin: código {codigo?.ToString(CultureInfo.InvariantCulture) ?? "?"}{(corte is { } c ? $", cortada ({c})" : "")}");
        await log.WriteLineAsync("--- stderr ---");
        await log.WriteLineAsync(textoError);
        var respuesta = corte is { } motivo ? RespuestaClaude.Fallida(motivo) : Interpretar(textoSalida, codigo ?? -1);
        await log.WriteLineAsync("--- stdout ---");
        await log.WriteLineAsync(peticion.SalidaPrivada ? ResumenSalida(textoSalida, respuesta) : textoSalida);
        return respuesta;
    }

    /// <summary>Lo que el log guarda de una salida con datos personales: nada de su contenido.</summary>
    private static string ResumenSalida(string salida, RespuestaClaude respuesta)
    {
        var bytes = Utf8SinBom.GetBytes(salida);
        return $"(no se guarda: son datos personales) {bytes.Length} bytes, sha256 {Convert.ToHexStringLower(SHA256.HashData(bytes))}, " +
            $"subtype {respuesta.Subtipo ?? "?"}, coste {respuesta.CosteUsd?.ToString(CultureInfo.InvariantCulture) ?? "?"}, " +
            $"turnos {respuesta.Turnos?.ToString(CultureInfo.InvariantCulture) ?? "?"}.";
    }

    public async Task<string?> VersionAsync(CancellationToken cancelacion)
    {
        await _cerrojoVersion.WaitAsync(cancelacion);
        try
        {
            if (_version is { } guardada && guardada.Hasta > _reloj.AhoraUtc) return guardada.Version;
            var version = await LeerVersionAsync(cancelacion);
            _version = (version, _reloj.AhoraUtc + CacheVersion);
            return version;
        }
        finally
        {
            _cerrojoVersion.Release();
        }
    }

    /// <summary>
    /// Lee el JSON de <c>--output-format json</c>. También con código de salida distinto de 0: al pasarse del
    /// presupuesto, Claude sale con 1 y un JSON con <c>is_error</c> y <c>subtype: "error_max_budget_usd"</c> (paso 0, C9).
    /// </summary>
    public static RespuestaClaude Interpretar(string salida, int codigoSalida)
    {
        JsonElement raiz;
        try
        {
            using var documento = JsonDocument.Parse(salida);
            raiz = documento.RootElement.Clone();
        }
        catch (JsonException)
        {
            return RespuestaClaude.Fallida(codigoSalida == 0 ? FalloClaude.SalidaNoValida : FalloClaude.CodigoSalida,
                $"Código {codigoSalida}; la salida no es JSON.");
        }
        if (raiz.ValueKind != JsonValueKind.Object)
            return RespuestaClaude.Fallida(FalloClaude.SalidaNoValida, "La salida no es un objeto JSON.");

        var subtipo = Texto(raiz, "subtype");
        var esError = raiz.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
        var coste = raiz.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out var importe)
            ? importe
            : (decimal?)null;
        int? turnos = raiz.TryGetProperty("num_turns", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var n) ? n : null;
        var sesion = Texto(raiz, "session_id");
        var resultado = Texto(raiz, "result");

        FalloClaude? fallo = (esError, subtipo, codigoSalida) switch
        {
            (true, "error_max_budget_usd", _) => FalloClaude.PresupuestoAgotado,
            (true, _, _) => FalloClaude.ErrorDeClaude,
            (false, _, not 0) => FalloClaude.CodigoSalida,
            _ => null
        };
        var estructurada = fallo is null ? SalidaEstructurada(raiz, resultado) : null;
        return new RespuestaClaude(fallo is null, subtipo, coste, turnos, sesion, resultado, estructurada, fallo,
            fallo is null ? null : $"Código {codigoSalida}, subtype {subtipo ?? "?"}.");
    }

    // structured_output con --json-schema (C4) o, como plan B, result leído como JSON sin las vallas de Markdown.
    private static JsonElement? SalidaEstructurada(JsonElement raiz, string? resultado)
    {
        if (raiz.TryGetProperty("structured_output", out var estructurada) && estructurada.ValueKind == JsonValueKind.Object)
            return estructurada.Clone();
        if (string.IsNullOrWhiteSpace(resultado)) return null;
        var texto = Valla().Match(resultado) is { Success: true } valla ? valla.Groups[1].Value : resultado;
        try
        {
            using var documento = JsonDocument.Parse(texto.Trim());
            return documento.RootElement.ValueKind == JsonValueKind.Object ? documento.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ProcessStartInfo Preparar(string carpeta, IEnumerable<string> argumentos)
    {
        var inicio = new ProcessStartInfo(_opciones.RutaClaude)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8SinBom,
            StandardOutputEncoding = Utf8SinBom,
            StandardErrorEncoding = Utf8SinBom,
            WorkingDirectory = carpeta
        };
        foreach (var argumento in argumentos) inicio.ArgumentList.Add(argumento);
        return inicio;
    }

    private TrabajoWindows? MeterEnTrabajo(Process proceso)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return TrabajoWindows.Para(proceso);
        }
        catch (Win32Exception error)
        {
            // Sin el trabajo sigue valiendo Kill(entireProcessTree): solo se pierde la red de seguridad si la API muere.
            _log.LogWarning(error, "No se ha podido meter el proceso de Claude en un Job Object.");
            return null;
        }
    }

    private static async Task EscribirEntradaAsync(Process proceso, string entrada, CancellationToken cancelacion)
    {
        try
        {
            await proceso.StandardInput.WriteAsync(entrada.AsMemory(), cancelacion);
            await proceso.StandardInput.FlushAsync(cancelacion);
            proceso.StandardInput.Close();
        }
        catch (IOException)
        {
            // El proceso ha cerrado la entrada antes de leerla toda (ha acabado o ha fallado): lo dirá su salida.
        }
    }

    private void Matar(Process proceso)
    {
        try
        {
            proceso.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            // Ya había acabado, o no se ha dejado: el trabajo de Windows lo cierra al salir del using.
            _log.LogDebug(error, "No se ha podido matar el árbol de Claude.");
        }
    }

    private static async Task EsperarFinAsync(Process proceso)
    {
        using var espera = new CancellationTokenSource(EsperaTrasMatar);
        try
        {
            await proceso.WaitForExitAsync(espera.Token);
        }
        catch (OperationCanceledException)
        {
            // Se deja: el trabajo de Windows ya está cerrado.
        }
    }

    /// <summary>Lee todo el flujo pero solo guarda los primeros <paramref name="maximo"/> caracteres.</summary>
    private static async Task<string> LeerLimitadoAsync(StreamReader lector, int maximo)
    {
        var texto = new StringBuilder();
        var bufer = new char[16 * 1024];
        int leidos;
        while ((leidos = await lector.ReadAsync(bufer.AsMemory())) > 0)
        {
            var caben = Math.Min(leidos, maximo - texto.Length);
            if (caben > 0) texto.Append(bufer, 0, caben);
        }
        return texto.ToString();
    }

    // Si un nieto se quedó con la salida abierta (sin Job Object), no se espera para siempre.
    private static async Task<string> LeerConLimiteAsync(Task<string> lectura)
    {
        var terminada = await Task.WhenAny(lectura, Task.Delay(EsperaTrasMatar));
        return terminada == lectura ? await lectura : "";
    }

    private async Task<string?> LeerVersionAsync(CancellationToken cancelacion)
    {
        if (!File.Exists(_opciones.RutaClaude)) return null;
        using var proceso = new Process { StartInfo = Preparar(AppContext.BaseDirectory, _opciones.ArgumentosPrevios.Append("--version")) };
        try
        {
            proceso.Start();
            proceso.StandardInput.Close();
            var salida = proceso.StandardOutput.ReadToEndAsync(cancelacion);
            _ = proceso.StandardError.ReadToEndAsync(cancelacion);
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            limite.CancelAfter(TiempoVersion);
            await proceso.WaitForExitAsync(limite.Token);
            return Version().Match(await salida) is { Success: true } version ? version.Value : null;
        }
        catch (Exception error) when (error is Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            if (!proceso.HasExited) Matar(proceso);
            _log.LogInformation("No se ha podido leer la versión de Claude Code.");
            return null;
        }
    }

    private static string? Texto(JsonElement raiz, string nombre) =>
        raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static string Citar(string argumento) =>
        argumento.Length == 0 ? "\"\"" : argumento.Contains(' ') || argumento.Contains('"') ? $"\"{argumento.Replace("\"", "\\\"")}\"" : argumento;

    [GeneratedRegex(@"^\s*```(?:json)?\s*\n(.*?)\n\s*```\s*$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Valla();

    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Version();
}
