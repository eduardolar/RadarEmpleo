using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RadarEmpleo.Domain;

namespace RadarEmpleo.EvaluacionEncaje;

/// <summary>Una oferta de <c>esperado.json</c>: la ficha y el encaje que se espera.</summary>
public sealed record CasoConjunto(string Id, string Empresa, string Puesto, string? Ubicacion, string? Modalidad, Encaje Encaje, string Porque);

public sealed record Conjunto(List<CasoConjunto> Ofertas);

/// <summary>Lo que el usuario ha pedido a la consola.</summary>
public sealed record OpcionesEvaluacion(decimal Tope, string? Salida, string? RutaClaude, bool Falso);

/// <summary>Las opciones leídas, o el error y el código de salida (1: falta confirmar o el tope; 2: no se entiende).</summary>
public sealed record OpcionesLeidas(OpcionesEvaluacion? Opciones, string? Error, int Codigo);

/// <summary>Las reglas puras del evaluador del encaje: sin disco, sin reloj y sin claude.</summary>
public static partial class ConsolaEvaluacion
{
    /// <summary>
    /// Lo que se guarda por oferta además de su presupuesto: Claude se puede pasar de <c>--max-budget-usd</c> en el
    /// último turno, y eso no puede sacar la evaluación del tope.
    /// </summary>
    public const decimal MargenPorOfertaUsd = 0.25m;

    public const decimal TopeMaximoUsd = 10m;

    // El id forma la ruta del archivo de la oferta: solo «oferta-NN», con cifras ASCII (\d admite las de otros alfabetos).
    [GeneratedRegex(@"\Aoferta-[0-9]{2}\z")]
    private static partial Regex IdOferta();

    /// <summary>El tope más pequeño con el que se puede lanzar al menos una oferta.</summary>
    public static decimal TopeMinimo(decimal presupuesto) => presupuesto + MargenPorOfertaUsd;

    /// <summary>Lee los argumentos. No lanza nada ni toca el disco.</summary>
    public static OpcionesLeidas LeerOpciones(IReadOnlyList<string> args, decimal presupuesto)
    {
        var opciones = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is "--confirmo-gasto" or "--falso") opciones[args[i]] = "";
            else if (args[i] is "--tope-usd" or "--salida" or "--ruta-claude" && i + 1 < args.Count) opciones[args[i]] = args[++i];
            else return new(null, $"No entiendo «{args[i]}».", 2);
        }
        if (!opciones.ContainsKey("--confirmo-gasto") || !opciones.TryGetValue("--tope-usd", out var textoTope))
            return new(null, "Falta --confirmo-gasto o --tope-usd: no he lanzado nada.", 1);
        var minimo = TopeMinimo(presupuesto);
        if (!decimal.TryParse(textoTope.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var tope)
            || tope < minimo || tope > TopeMaximoUsd)
            return new(null, string.Create(CultureInfo.GetCultureInfo("es-ES"),
                $"El tope tiene que ser un número entre {minimo:0.00} y {TopeMaximoUsd:0}: no he lanzado nada."), 2);
        return new(new OpcionesEvaluacion(tope, Vacia(opciones.GetValueOrDefault("--salida")), Vacia(opciones.GetValueOrDefault("--ruta-claude")),
            opciones.ContainsKey("--falso")), null, 0);

        static string? Vacia(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;
    }

    /// <summary>
    /// ¿Se puede lanzar una oferta más? Solo si lo gastado, su presupuesto y el margen caben en el tope (el borde exacto
    /// cabe).
    /// </summary>
    public static bool CabeOtraOferta(decimal gastado, decimal presupuesto, decimal tope) =>
        ReglasGasto.CabeEnElTope(gastado, 0m, presupuesto + MargenPorOfertaUsd, tope);

    /// <summary>
    /// La carpeta de esta evaluación: siempre una subcarpeta con la fecha, dentro de la elegida o de
    /// <paramref name="porDefecto"/>, para que dos evaluaciones nunca se pisen.
    /// </summary>
    public static string CarpetaSalida(string? elegida, string porDefecto, DateTime ahoraUtc) =>
        Path.Combine(Path.GetFullPath(string.IsNullOrWhiteSpace(elegida) ? porDefecto : elegida),
            ahoraUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

    /// <summary>
    /// SHA-256 de la plantilla con los finales de línea en «\n»: el mismo prompt da el mismo hash venga de un checkout
    /// con CRLF o con LF.
    /// </summary>
    public static string HashPrompt(string plantilla) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plantilla.ReplaceLineEndings("\n"))));

    /// <summary>
    /// Qué tiene mal el conjunto, o <c>null</c> si vale. Se mira antes de lanzar nada, para no gastar y fallar a mitad.
    /// </summary>
    public static string? ErrorDelConjunto(Conjunto? conjunto, Func<string, bool> existeLaOferta)
    {
        if (conjunto?.Ofertas is not { Count: > 0 } casos) return "esperado.json no tiene ofertas.";
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var caso in casos)
        {
            if (caso is null) return "esperado.json tiene una oferta vacía.";
            if (caso.Id is null || !IdOferta().IsMatch(caso.Id)) return $"Id de oferta no válido: «{caso.Id}».";
            if (!vistos.Add(caso.Id)) return $"La oferta «{caso.Id}» está repetida.";
            if (string.IsNullOrWhiteSpace(caso.Empresa) || string.IsNullOrWhiteSpace(caso.Puesto))
                return $"A la oferta «{caso.Id}» le falta la empresa o el puesto.";
            if (!PuntuacionEncaje.Niveles.Contains(caso.Encaje)) return $"La oferta «{caso.Id}» no tiene un encaje válido.";
            if (!existeLaOferta(caso.Id)) return $"Falta el archivo de la oferta «{caso.Id}».";
        }
        return null;
    }

    /// <summary>El aviso del resumen si se ha gastado más que el tope (ni con el margen ha bastado), o <c>null</c>.</summary>
    public static string? AvisoTope(decimal gastado, decimal tope) =>
        gastado > tope
            ? string.Create(CultureInfo.InvariantCulture, $"AVISO: se ha gastado {gastado:0.00} USD, más que el tope de {tope:0.00}: Claude se ha pasado de su presupuesto por oferta más de lo que cubre el margen.")
            : null;
}
