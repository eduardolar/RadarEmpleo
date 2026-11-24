using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RadarEmpleo.Services.Agentes;

/// <summary>Lo variable de los argumentos de una ejecución, calculado por la app (nunca del cuerpo de una petición).</summary>
/// <param name="CarpetaDatos">Carpeta de datos de la app (<c>%LOCALAPPDATA%\RadarEmpleo</c>): se deniega leerla.</param>
/// <param name="NombreInforme">El <c>rastreo_AAAA-MM-DD(_N).md</c> que da la app (Rastreo).</param>
public sealed record ValoresArgumentos(string CarpetaDatos, string? NombreInforme = null);

/// <summary>
/// Monta los argumentos de <c>claude</c> para una tarea, sin el ejecutable. Puro: siempre la misma lista para lo
/// mismo. Cada valor va en su propio argumento (<see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>), así
/// que nunca hay comillas que escapar. Comunes, en este orden:
/// <c>-p --output-format json --json-schema &lt;esquema&gt; --max-budget-usd &lt;p&gt; --permission-mode dontAsk
/// --no-session-persistence --strict-mcp-config --setting-sources ""</c>. <c>--strict-mcp-config</c> sin
/// <c>--mcp-config</c> deja a Claude sin servidores MCP (y sin Chrome), y <c>--setting-sources ""</c> no carga ninguna
/// configuración (ni las reglas, hooks, CLAUDE.md, agentes ni skills de Eduardo; paso 0).
/// </summary>
public static partial class ConstructorArgumentos
{
    /// <param name="esquemaJson">El esquema de la salida; se minifica en un solo argumento.</param>
    /// <param name="presupuesto">El de <c>--max-budget-usd</c> si no es el de la definición: el de cada parte de una tarea
    /// por partes (<see cref="DefinicionTarea.PresupuestoParte"/>). Nunca puede pasar del de la definición.</param>
    /// <exception cref="ArgumentException">El esquema no es JSON, falta o no vale el nombre del informe, o la carpeta de
    /// datos no se puede escribir como regla.</exception>
    /// <exception cref="ArgumentOutOfRangeException">El presupuesto no es positivo o pasa del de la definición.</exception>
    public static IReadOnlyList<string> Construir(DefinicionTarea definicion, string esquemaJson, ValoresArgumentos valores,
        decimal? presupuesto = null)
    {
        var tope = presupuesto ?? definicion.PresupuestoUsd;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tope, nameof(presupuesto));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tope, definicion.PresupuestoUsd, nameof(presupuesto));
        var argumentos = new List<string>
        {
            "-p",
            "--output-format", "json",
            "--json-schema", Minificar(esquemaJson),
            "--max-budget-usd", tope.ToString("0.00", CultureInfo.InvariantCulture),
            "--permission-mode", "dontAsk",
            "--no-session-persistence",
            "--strict-mcp-config",
            "--setting-sources", "",
            "--tools", definicion.Herramientas
        };
        if (definicion.Permitidas is { } permitidas)
            argumentos.AddRange(["--allowedTools", Sustituir(permitidas, valores)]);
        if (definicion.Denegadas is { } denegadas)
            argumentos.AddRange(["--disallowedTools", Sustituir(denegadas, valores)]);
        if (definicion.Modelo is { } modelo)
            argumentos.AddRange(["--model", modelo]);
        return argumentos;
    }

    /// <summary>El esquema en una sola línea y sin espacios.</summary>
    public static string Minificar(string esquemaJson)
    {
        try
        {
            using var documento = JsonDocument.Parse(esquemaJson);
            return JsonSerializer.Serialize(documento.RootElement);
        }
        catch (JsonException error)
        {
            throw new ArgumentException("El esquema de la tarea no es JSON válido.", nameof(esquemaJson), error);
        }
    }

    /// <summary>Una carpeta de Windows en la forma de las reglas de Claude: <c>C:\Users\x</c> da <c>//c/Users/x</c> (paso 0).</summary>
    /// <exception cref="ArgumentException">No es una ruta local con unidad, o tiene caracteres que romperían la lista de reglas.</exception>
    public static string RutaRegla(string carpeta)
    {
        var completa = Path.GetFullPath(carpeta).TrimEnd('\\', '/');
        // En Linux (CI) la ruta ya empieza por «/»: la regla absoluta es «//…».
        if (!OperatingSystem.IsWindows() && completa.StartsWith('/') && completa.IndexOfAny([',', '(', ')', '*', '?', '"']) < 0)
            return "/" + completa;
        if (completa.Length < 3 || completa[1] != ':' || !char.IsAsciiLetter(completa[0]) || completa.IndexOfAny([',', '(', ')', '*', '?', '"']) >= 0
            || completa.IndexOf(':', 2) >= 0)
            throw new ArgumentException("La carpeta de datos no se puede escribir como regla de Claude.", nameof(carpeta));
        return $"//{char.ToLowerInvariant(completa[0])}{completa[2..].Replace('\\', '/')}";
    }

    private static string Sustituir(string lista, ValoresArgumentos valores)
    {
        if (lista.Contains(DefinicionesTareas.MarcaCarpetaDatos, StringComparison.Ordinal))
            lista = lista.Replace(DefinicionesTareas.MarcaCarpetaDatos, RutaRegla(valores.CarpetaDatos), StringComparison.Ordinal);
        if (!lista.Contains(DefinicionesTareas.MarcaNombreInforme, StringComparison.Ordinal)) return lista;
        if (valores.NombreInforme is not { } nombre || !NombreInforme().IsMatch(nombre))
            throw new ArgumentException("Falta el nombre del informe de rastreo o no es válido.", nameof(valores));
        return lista.Replace(DefinicionesTareas.MarcaNombreInforme, nombre, StringComparison.Ordinal);
    }

    // Solo el nombre que calcula la app: ni rutas, ni comodines, ni paréntesis que cierren la regla, ni salto final.
    [GeneratedRegex(@"\Arastreo_\d{4}-\d{2}-\d{2}(?:_\d{1,3})?\.md\z", RegexOptions.CultureInvariant)]
    private static partial Regex NombreInforme();

}
