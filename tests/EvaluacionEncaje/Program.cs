using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Agentes;

// EvaluacionEncaje: mide cuánto acierta AnalizarEncaje con las 20 ofertas inventadas de datos-ejemplo/encaje contra
// perfil-ejemplo.md (decisiones 5 y 8 de la F7b). Usa el prompt real (PromptAnalizarEncaje.Montar), los argumentos reales
// (ConstructorArgumentos) y el EjecutorClaudeCli: gasta del cupo. Por eso solo arranca con «--confirmo-gasto» y
// «--tope-usd <n>», y no lanza una oferta más si con su presupuesto (0,50) pasaría del tope.

const string Uso = """
    Evaluación del encaje: lanza claude de verdad con 20 ofertas inventadas y gasta del cupo.
    No hago nada sin las dos opciones obligatorias:
      --confirmo-gasto       confirmas que sabes que gasta (cada oferta, hasta 0,50 USD)
      --tope-usd <n>         gasto máximo de toda la evaluación, entre 0,50 y 10 (por ejemplo, 5)
    Opcionales:
      --salida <carpeta>     dónde escribir el resumen (por defecto, una carpeta nueva bajo %TEMP%)
      --ruta-claude <ruta>   claude.exe (por defecto, el que instala npm)
      --falso                sin claude: el ejecutor falso responde «Medio» y 0,20 USD (para probar la consola)
    """;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
var definicion = DefinicionesTareas.AnalizarEncaje;
var opciones = new Dictionary<string, string?>(StringComparer.Ordinal);
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "--confirmo-gasto" or "--falso") opciones[args[i]] = "";
    else if (args[i] is "--tope-usd" or "--salida" or "--ruta-claude" && i + 1 < args.Length) opciones[args[i]] = args[++i];
    else return Salir($"No entiendo «{args[i]}».", 2);
}
if (!opciones.ContainsKey("--confirmo-gasto") || !opciones.TryGetValue("--tope-usd", out var textoTope))
    return Salir("Falta --confirmo-gasto o --tope-usd: no he lanzado nada.", 1);
if (!decimal.TryParse(textoTope?.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var tope)
    || tope < definicion.PresupuestoUsd || tope > 10m)
    return Salir("El tope tiene que ser un número entre 0,50 y 10: no he lanzado nada.", 2);

var conjunto = Path.Combine(AppContext.BaseDirectory, "datos-ejemplo", "encaje");
var perfil = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "datos-ejemplo", "perfil-ejemplo.md"));
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
var casos = JsonSerializer.Deserialize<Conjunto>(File.ReadAllText(Path.Combine(conjunto, "esperado.json")), json)!.Ofertas;
// El id forma la ruta del archivo de la oferta: solo «oferta-NN».
if (casos.FirstOrDefault(c => !Regex.IsMatch(c.Id, @"\Aoferta-\d{2}\z")) is { } raro)
    return Salir($"Id de oferta no válido: «{raro.Id}».", 2);

var salida = opciones.GetValueOrDefault("--salida") is { Length: > 0 } elegida ? Path.GetFullPath(elegida)
    : Path.Combine(Path.GetTempPath(), "RadarEmpleo-evaluacion-encaje", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
Directory.CreateDirectory(salida);

var reloj = new RelojSistema();
IEjecutorClaude ejecutor;
if (opciones.ContainsKey("--falso"))
{
    var falso = new EjecutorClaudeFalso(reloj);
    falso.Programar(definicion.Tarea, new RespuestaFalsa(CosteUsd: 0.20m,
        Salida: JsonSerializer.SerializeToElement(new { encaje = "Medio", pega = "Respuesta del ejecutor falso.", analisis = "", resumen = "" })));
    ejecutor = falso;
}
else
{
    // Como OpcionesAgentes.RutaClaudeEfectiva de la API (esta consola no la referencia).
    var rutaClaude = opciones.GetValueOrDefault("--ruta-claude") is { Length: > 0 } ruta ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(ruta))
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe");
    ejecutor = new EjecutorClaudeCli(new OpcionesEjecutor(rutaClaude, [], salida, tope, ""), reloj, NullLogger<EjecutorClaudeCli>.Instance);
}

// Ctrl+C corta la oferta en curso (el ejecutor mata el árbol de procesos) y escribe el resumen de lo hecho.
using var corte = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; corte.Cancel(); };

var hashPrompt = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Prompts.Plantilla(definicion))));
var version = await ejecutor.VersionAsync(corte.Token);
var argumentos = ConstructorArgumentos.Construir(definicion, Prompts.Esquema(definicion), new ValoresArgumentos(salida));
var resultados = new List<ResultadoOferta>();
var gastado = 0m;
string? parada = null;
Console.WriteLine($"Evaluando {casos.Count} ofertas con tope {tope:0.00} USD. Salida: {salida}");

foreach (var caso in casos)
{
    if (corte.IsCancellationRequested) { parada = "Cortada a mano (Ctrl+C)."; break; }
    if (!ReglasGasto.CabeEnElTope(gastado, 0m, definicion.PresupuestoUsd, tope)) { parada = "Cortada por el tope de gasto."; break; }

    var prompt = PromptAnalizarEncaje.Montar(new DatosAnalisisEncaje(caso.Empresa, caso.Puesto, null, caso.Ubicacion, caso.Modalidad,
        File.ReadAllText(Path.Combine(conjunto, caso.Id + ".md")), perfil));
    var carpeta = Directory.CreateDirectory(Path.Combine(salida, "ejecuciones", caso.Id)).FullName;
    var peticion = new PeticionClaude(definicion.Tarea, argumentos, carpeta, prompt, definicion.TiempoMaximo, Path.Combine(salida, "logs", caso.Id + ".log"));
    var respuesta = await ejecutor.EjecutarAsync(peticion, (_, _) => { }, corte.Token);
    if (respuesta.Fallo == FalloClaude.NoInstalado) { parada = "No encuentro claude.exe (usa --ruta-claude)."; break; }

    // Sin coste conocido cuenta el presupuesto entero, como ReglasGasto.GastoComputable.
    var coste = respuesta.CosteUsd ?? definicion.PresupuestoUsd;
    gastado += coste;
    var (encaje, pega) = Leer(respuesta.SalidaEstructurada);
    resultados.Add(new ResultadoOferta(caso.Id, caso.Encaje, encaje, coste, pega, respuesta.Fallo?.ToString()));
    Console.WriteLine($"{caso.Id}: esperado {caso.Encaje}, obtenido {encaje?.ToString() ?? "sin respuesta"} ({coste:0.00} USD, acumulado {gastado:0.00})");
}

var puntuacion = PuntuacionEncaje.Calcular(resultados.Select(r => new CasoPuntuado(r.Esperado, r.Obtenido)));
var resumen = Resumen(puntuacion, resultados, casos.Count, gastado, tope, hashPrompt, version, parada);
await File.WriteAllTextAsync(Path.Combine(salida, "resumen.md"), resumen);
await File.WriteAllTextAsync(Path.Combine(salida, "resultados.json"), JsonSerializer.Serialize(resultados, new JsonSerializerOptions(json) { WriteIndented = true }));
Console.WriteLine(resumen);
return 0;

static int Salir(string mensaje, int codigo)
{
    Console.Error.WriteLine(mensaje);
    Console.Error.WriteLine(Uso);
    return codigo;
}

// El encaje solo vale con el texto exacto de uno de los cuatro niveles; si no, cuenta como sin respuesta.
static (Encaje? Encaje, string? Pega) Leer(JsonElement? salida)
{
    if (salida is not { ValueKind: JsonValueKind.Object } objeto) return (null, null);
    var texto = objeto.TryGetProperty("encaje", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    var encaje = PuntuacionEncaje.Niveles.Select(n => (Encaje?)n).FirstOrDefault(n => n.ToString() == texto);
    var pega = objeto.TryGetProperty("pega", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    return (encaje, pega);
}

static string Resumen(ResultadoPuntuacion p, List<ResultadoOferta> resultados, int total, decimal gastado, decimal tope, string hash, string? version, string? parada)
{
    static string Celda(string? texto) => (texto ?? "").ReplaceLineEndings(" ").Replace("|", "\\|");
    var r = new StringBuilder();
    r.AppendLine("# Evaluación del encaje (datos-ejemplo/encaje contra perfil-ejemplo.md)").AppendLine();
    r.AppendLine($"- Fecha (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
    r.AppendLine($"- Hash SHA-256 de la plantilla del prompt: `{hash}`");
    r.AppendLine($"- Claude Code: {version ?? "versión desconocida"}");
    r.AppendLine($"- Evaluadas: {p.Total} de {total}. {parada ?? "Completa."}");
    r.AppendLine($"- Exactos: {p.Exactos}; a un nivel: {p.AUnNivel}; exactos o a un nivel: {p.Exactos + p.AUnNivel}; lejos: {p.Lejos}; sin respuesta: {p.SinRespuesta}");
    r.AppendLine($"- Coste total: {gastado:0.00} USD (tope {tope:0.00})").AppendLine();
    r.AppendLine("## Matriz de confusión (filas: esperado; columnas: obtenido)").AppendLine();
    r.AppendLine("| | " + string.Join(" | ", PuntuacionEncaje.Niveles) + " |");
    r.AppendLine("|---|" + string.Concat(PuntuacionEncaje.Niveles.Select(_ => "---|")));
    for (var fila = 0; fila < PuntuacionEncaje.Niveles.Count; fila++)
        r.AppendLine($"| {PuntuacionEncaje.Niveles[fila]} | " + string.Join(" | ", Enumerable.Range(0, PuntuacionEncaje.Niveles.Count).Select(c => p.Matriz[fila, c])) + " |");
    r.AppendLine().AppendLine("## Por oferta").AppendLine();
    r.AppendLine("| Oferta | Esperado | Obtenido | Coste (USD) | Pega |").AppendLine("|---|---|---|---|---|");
    foreach (var o in resultados)
        r.AppendLine($"| {o.Id} | {o.Esperado} | {o.Obtenido?.ToString() ?? $"sin respuesta ({o.Fallo ?? "no se entiende"})"} | {o.CosteUsd:0.00} | {Celda(o.Pega)} |");
    return r.ToString();
}

/// <summary>Una oferta de <c>esperado.json</c>: la ficha y el encaje que se espera.</summary>
internal sealed record CasoConjunto(string Id, string Empresa, string Puesto, string? Ubicacion, string? Modalidad, Encaje Encaje, string Porque);

internal sealed record Conjunto(List<CasoConjunto> Ofertas);

/// <summary>Lo que ha salido con una oferta.</summary>
internal sealed record ResultadoOferta(string Id, Encaje Esperado, Encaje? Obtenido, decimal CosteUsd, string? Pega, string? Fallo);
