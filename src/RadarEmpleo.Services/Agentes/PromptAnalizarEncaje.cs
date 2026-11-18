namespace RadarEmpleo.Services.Agentes;

/// <summary>Lo que va al prompt de AnalizarEncaje: la ficha, el texto de la oferta y el perfil de búsqueda.</summary>
/// <param name="Modalidad">Ya en texto («Híbrido (2 días en Madrid)»).</param>
/// <param name="TextoOferta">El texto descargado por la app, o null si no se ha podido leer.</param>
/// <param name="OfertaRecortada">La descarga solo trajo el principio de la página.</param>
/// <param name="AvisoOferta">Lo que la app sabe de la oferta y Claude debe tener en cuenta (por ejemplo, que no sabe si sigue abierta).</param>
public sealed record DatosAnalisisEncaje(
    string Empresa,
    string Puesto,
    string? Enlace,
    string? Ubicacion,
    string? Modalidad,
    string? TextoOferta,
    string Perfil,
    bool OfertaRecortada = false,
    string? AvisoOferta = null);

/// <summary>
/// Monta el prompt de AnalizarEncaje con su plantilla embebida y <see cref="PlantillaPrompt.Rellenar"/>. Pura: la usan el
/// manejador y el evaluador del acierto (PR 6 de la F7b), para que los dos midan el mismo prompt.
/// </summary>
public static class PromptAnalizarEncaje
{
    /// <summary>Largo máximo del texto de la oferta en el prompt (unos 6.000 tokens).</summary>
    public const int LargoOferta = 20_000;

    /// <summary>Largo máximo del perfil de búsqueda en el prompt.</summary>
    public const int LargoPerfil = 20_000;

    public const string OfertaSinLeer = "no se ha podido leer: analiza con los datos de la ficha y dilo";

    public static string Montar(DatosAnalisisEncaje datos)
    {
        var leida = !string.IsNullOrWhiteSpace(datos.TextoOferta);
        var lectura = !leida ? OfertaSinLeer
            : datos.OfertaRecortada || datos.TextoOferta!.Length > LargoOferta ? "descargada por la app; solo el principio, la página era muy larga"
            : "descargada por la app";
        if (datos.AvisoOferta is { Length: > 0 } aviso) lectura += $"; aviso de la app: {aviso}";

        return PlantillaPrompt.Rellenar(Prompts.Plantilla(DefinicionesTareas.AnalizarEncaje), new Dictionary<string, DatoPrompt>
        {
            ["empresa"] = new(datos.Empresa, 200),
            ["puesto"] = new(datos.Puesto, 300),
            ["enlace"] = new(PlantillaPrompt.EnlaceSeguro(datos.Enlace), 2000),
            ["ubicacion"] = new(datos.Ubicacion, 200),
            ["modalidad"] = new(datos.Modalidad, 400),
            ["lecturaOferta"] = new(lectura, 500),
            ["oferta"] = new(leida ? datos.TextoOferta : "(sin texto)", LargoOferta, UnaLinea: false),
            ["perfil"] = new(datos.Perfil, LargoPerfil, UnaLinea: false)
        });
    }
}
