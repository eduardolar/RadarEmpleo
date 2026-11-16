using System.Collections.Concurrent;
using System.Text;

namespace RadarEmpleo.Services.Agentes;

/// <summary>
/// Las plantillas (<c>prompts/&lt;nombre&gt;.md</c>) y los esquemas de salida (<c>prompts/&lt;nombre&gt;.schema.json</c>)
/// de las tareas. Van embebidos en el ensamblado (<c>RadarEmpleo.Services.csproj</c>): no se pueden cambiar en tiempo de
/// ejecución, ni desde la configuración ni desde una petición. El nombre sale de <see cref="DefinicionTarea.NombrePrompt"/>.
/// </summary>
public static class Prompts
{
    private const string Carpeta = "prompts/";
    private static readonly ConcurrentDictionary<string, string> Leidos = new(StringComparer.Ordinal);

    /// <summary>La plantilla de la tarea, para <see cref="PlantillaPrompt.Rellenar"/>.</summary>
    /// <exception cref="InvalidOperationException">La tarea no tiene plantilla embebida (error de código).</exception>
    public static string Plantilla(DefinicionTarea definicion) => Leer($"{definicion.NombrePrompt}.md");

    /// <summary>El esquema de la salida de la tarea, para <see cref="ConstructorArgumentos.Construir"/>.</summary>
    /// <exception cref="InvalidOperationException">La tarea no tiene esquema embebido (error de código).</exception>
    public static string Esquema(DefinicionTarea definicion) => Leer($"{definicion.NombrePrompt}.schema.json");

    /// <summary>Si la tarea tiene su plantilla y su esquema embebidos.</summary>
    public static bool Existen(DefinicionTarea definicion) =>
        Existe($"{definicion.NombrePrompt}.md") && Existe($"{definicion.NombrePrompt}.schema.json");

    private static bool Existe(string archivo) =>
        typeof(Prompts).Assembly.GetManifestResourceInfo(Carpeta + archivo) is not null;

    private static string Leer(string archivo) => Leidos.GetOrAdd(archivo, static nombre =>
    {
        using var flujo = typeof(Prompts).Assembly.GetManifestResourceStream(Carpeta + nombre)
            ?? throw new InvalidOperationException($"No está embebido {Carpeta}{nombre}.");
        using var lector = new StreamReader(flujo, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        // Siempre con saltos LF, se haya sacado el repositorio en Windows o en Linux.
        return lector.ReadToEnd().ReplaceLineEndings("\n");
    });
}
