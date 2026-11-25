using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using RadarEmpleo.Domain;
using RadarEmpleo.EvaluacionEncaje;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Agentes;

// EvaluacionEncaje: mide cuánto acierta AnalizarEncaje con las 20 ofertas inventadas de datos-ejemplo/encaje contra
// perfil-ejemplo.md (decisiones 5 y 8 de la F7b). Usa el prompt real (PromptAnalizarEncaje.Montar), los argumentos reales
// (ConstructorArgumentos) y el EjecutorClaudeCli: gasta del cupo. Por eso solo arranca con «--confirmo-gasto» y
// «--tope-usd <n>», y no lanza una oferta más si con su presupuesto (0,50) y un margen (0,25) pasaría del tope. Las reglas
// puras (opciones, tope, carpeta, hash y validación del conjunto) están en ConsolaEvaluacion, con sus tests.

const string Uso = """
    Evaluación del encaje: lanza claude de verdad con 20 ofertas inventadas y gasta del cupo.
    No hago nada sin las dos opciones obligatorias:
      --confirmo-gasto       confirmas que sabes que gasta (cada oferta, hasta 0,50 USD)
      --tope-usd <n>         gasto máximo de toda la evaluación, entre 0,75 y 10 (por ejemplo, 5);
                             cada oferta reserva 0,75: su presupuesto y un margen por si Claude se pasa
    Opcionales:
      --salida <carpeta>     dónde escribir el resumen: siempre en una subcarpeta nueva con la fecha
                             (por defecto, bajo %TEMP%)
      --ruta-claude <ruta>   claude.exe (por defecto, el que instala npm)
      --falso                sin claude: el ejecutor falso responde «Medio» y 0,20 USD (para probar la consola)
    """;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
var definicion = DefinicionesTareas.AnalizarEncaje;
var leidas = ConsolaEvaluacion.LeerOpciones(args, definicion.PresupuestoUsd);
if (leidas.Opciones is not { } opciones) return Salir(leidas.Error!, leidas.Codigo);
var tope = opciones.Tope;

// Todo lo que puede estar mal en el conjunto se mira aquí, antes de gastar nada.
var conjunto = Path.Combine(AppContext.BaseDirectory, "datos-ejemplo", "encaje");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
string perfil;
List<CasoConjunto> casos;
try
{
    perfil = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "datos-ejemplo", "perfil-ejemplo.md"));
    var leido = JsonSerializer.Deserialize<Conjunto>(File.ReadAllText(Path.Combine(conjunto, "esperado.json")), json);
    if (ConsolaEvaluacion.ErrorDelConjunto(leido, id => File.Exists(Path.Combine(conjunto, id + ".md"))) is { } error)
        return Salir(error + " No he lanzado nada.", 2);
    casos = leido!.Ofertas;
}
catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
{
    return Salir($"No puedo leer el conjunto de datos-ejemplo ({e.Message}). No he lanzado nada.", 2);
}

var salida = ConsolaEvaluacion.CarpetaSalida(opciones.Salida, Path.Combine(Path.GetTempPath(), "RadarEmpleo-evaluacion-encaje"), DateTime.UtcNow);
Directory.CreateDirectory(salida);

var reloj = new RelojSistema();
IEjecutorClaude ejecutor;
if (opciones.Falso)
{
    var falso = new EjecutorClaudeFalso(reloj);
    falso.Programar(definicion.Tarea, new RespuestaFalsa(CosteUsd: 0.20m,
        Salida: JsonSerializer.SerializeToElement(new { encaje = "Medio", pega = "Respuesta del ejecutor falso.", analisis = "", resumen = "" })));
    ejecutor = falso;
}
else
{
    // La misma ruta por defecto que la API (RutasClaude).
    ejecutor = new EjecutorClaudeCli(new OpcionesEjecutor(RutasClaude.Efectiva(opciones.RutaClaude), [], salida, tope, ""), reloj, NullLogger<EjecutorClaudeCli>.Instance);
}

// Ctrl+C corta la oferta en curso (el ejecutor mata el árbol de procesos) y escribe el resumen de lo hecho.
using var corte = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; corte.Cancel(); };

var hashPrompt = ConsolaEvaluacion.HashPrompt(Prompts.Plantilla(definicion));
var resultados = new List<ResultadoOferta>();
var gastado = 0m;
string? version = null;
string? parada = null;
var codigo = 0;
Console.WriteLine($"Evaluando {casos.Count} ofertas con tope {tope:0.00} USD. Salida: {salida}");

// Pase lo que pase a mitad, el «finally» escribe el resumen de lo ya gastado.
try
{
    version = await ejecutor.VersionAsync(corte.Token);
    var argumentos = ConstructorArgumentos.Construir(definicion, Prompts.Esquema(definicion), new ValoresArgumentos(salida));
    foreach (var caso in casos)
    {
        if (corte.IsCancellationRequested) { parada = "Cortada a mano (Ctrl+C)."; break; }
        if (!ConsolaEvaluacion.CabeOtraOferta(gastado, definicion.PresupuestoUsd, tope))
        {
            parada = $"Cortada por el tope de gasto (cada oferta reserva su presupuesto, {definicion.PresupuestoUsd:0.00}, y un margen de {ConsolaEvaluacion.MargenPorOfertaUsd:0.00} USD).";
            break;
        }

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
}
catch (OperationCanceledException) when (corte.IsCancellationRequested)
{
    parada = "Cortada a mano (Ctrl+C).";
}
catch (Exception e)
{
    // La oferta que estaba en curso puede haber gastado sin que conste: se dice en el resumen.
    parada = $"Cortada por un error ({e.Message}). La oferta en curso puede haber gastado hasta su presupuesto sin que conste aquí.";
    codigo = 3;
}
finally
{
    var puntuacion = PuntuacionEncaje.Calcular(resultados.Select(r => new CasoPuntuado(r.Esperado, r.Obtenido)));
    var resumen = Resumen(puntuacion, resultados, casos.Count, gastado, tope, hashPrompt, version, parada);
    File.WriteAllText(Path.Combine(salida, "resumen.md"), resumen);
    File.WriteAllText(Path.Combine(salida, "resultados.json"), JsonSerializer.Serialize(resultados, new JsonSerializerOptions(json) { WriteIndented = true }));
    Console.WriteLine(resumen);
}
return codigo;

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
    r.AppendLine($"- Coste total: {gastado:0.00} USD (tope {tope:0.00})");
    if (ConsolaEvaluacion.AvisoTope(gastado, tope) is { } aviso) r.AppendLine($"- **{aviso}**");
    r.AppendLine();
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

/// <summary>Lo que ha salido con una oferta.</summary>
internal sealed record ResultadoOferta(string Id, Encaje Esperado, Encaje? Obtenido, decimal CosteUsd, string? Pega, string? Fallo);
