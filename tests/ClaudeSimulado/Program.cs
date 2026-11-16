using System.Diagnostics;
using System.Text;
using System.Text.Json;

// ClaudeSimulado: hace de claude.exe en los tests del EjecutorClaudeCli, sin Claude y sin red.
// Sus propias opciones van delante, con «--sim:clave=valor» (los tests las ponen en ArgumentosPrevios); el resto
// son los argumentos que habría recibido Claude.
//   --sim:modo=json | respuesta | espera | hijo | hijoysale | grande | codigo1 | basura | presupuesto | vallas
//     hijoysale: lanza un hijo que hereda la salida y no termina, responde bien y sale
//     grande: escribe 5 MB de basura en la salida (más de lo que guarda el ejecutor)
//   --sim:segundos=N        cuánto espera en «espera» y «hijo» (por defecto, 300)
//   --sim:registro=<ruta>   guarda ahí, en JSON, los argumentos y la entrada estándar que ha recibido
//   --sim:respuesta=<ruta>  en «respuesta», el archivo cuyo contenido escribe tal cual en la salida
//   --sim:pidhijo=<ruta>    en «hijo», guarda ahí el pid del hijo que lanza y que nunca termina

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = utf8;
Console.InputEncoding = utf8;

var opciones = args.TakeWhile(a => a.StartsWith("--sim:", StringComparison.Ordinal))
    .Select(a => a["--sim:".Length..].Split('=', 2))
    .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
var argumentos = args.SkipWhile(a => a.StartsWith("--sim:", StringComparison.Ordinal)).ToArray();
var modo = opciones.GetValueOrDefault("modo", "json");
var segundos = int.Parse(opciones.GetValueOrDefault("segundos", "300"));

// En «--version» no hay entrada que leer.
if (argumentos is ["--version"])
{
    Console.WriteLine("2.1.185 (Claude Code)");
    return 0;
}

string entrada;
using (var lector = new StreamReader(Console.OpenStandardInput(), utf8))
    entrada = lector.ReadToEnd();

if (opciones.TryGetValue("registro", out var registro))
    File.WriteAllText(registro, JsonSerializer.Serialize(new { argumentos, entrada }), utf8);

switch (modo)
{
    case "respuesta":
        Console.Write(File.ReadAllText(opciones["respuesta"], utf8));
        return 0;
    case "espera":
        Thread.Sleep(TimeSpan.FromSeconds(segundos));
        Console.Write(Exito());
        return 0;
    case "hijo":
        LanzarHijo();
        Thread.Sleep(TimeSpan.FromSeconds(segundos));
        return 0;
    case "hijoysale":
        LanzarHijo();
        Console.Write(Exito());
        return 0;
    case "grande":
        var trozo = new string('x', 64 * 1024);
        for (var i = 0; i < 80; i++) Console.Write(trozo);
        return 0;
    case "codigo1":
        Console.Error.WriteLine("Fallo simulado.");
        return 1;
    case "basura":
        Console.Write("esto no es JSON <html>");
        return 0;
    case "presupuesto":
        Console.Write("""{"type":"result","subtype":"error_max_budget_usd","is_error":true,"num_turns":2,"session_id":"s-sim","total_cost_usd":0.0167,"errors":["Reached maximum budget ($0.01)"]}""");
        return 1;
    case "vallas":
        Console.Write(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["num_turns"] = 1, ["session_id"] = "s-sim",
            ["total_cost_usd"] = 0.02m, ["result"] = "```json\n{\"ok\":true,\"texto\":\"Peña\"}\n```"
        }));
        return 0;
    default:
        Console.Write(Exito());
        return 0;
}

// Lanzado con «dotnet ClaudeSimulado.dll» o con su .exe: el hijo, igual. Hereda la salida estándar (como un nieto
// de Claude que se quedara con ella abierta).
void LanzarHijo()
{
    var inicioHijo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") inicioHijo.ArgumentList.Add(typeof(Program).Assembly.Location);
    inicioHijo.ArgumentList.Add("--sim:modo=espera");
    inicioHijo.ArgumentList.Add($"--sim:segundos={segundos}");
    var hijo = Process.Start(inicioHijo)!;
    hijo.StandardInput.Close();
    File.WriteAllText(opciones["pidhijo"], hijo.Id.ToString());
}

static string Exito() => JsonSerializer.Serialize(new Dictionary<string, object>
{
    ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["num_turns"] = 3, ["session_id"] = "s-sim",
    ["total_cost_usd"] = 0.0184m, ["result"] = "Listo.", ["structured_output"] = new { ok = true, marca = "«Peña» 12 €" }
});
