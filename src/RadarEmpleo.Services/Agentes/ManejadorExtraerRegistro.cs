using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Importacion;

namespace RadarEmpleo.Services.Agentes;

/// <summary>
/// ExtraerRegistro (apartado 6 del plan): manda a Claude el texto libre del registro, sin la tabla ni las secciones
/// «## Rastreo …» (<see cref="AnalizadorRegistro.TextoLibre"/>), y guarda en <c>ResultadoJson</c> lo que devuelve, ya
/// validado. <b>No crea nada</b>: las candidaturas solo entran desde la vista previa de la importación, con lo que Eduardo
/// elija. El registro solo se lee.
/// </summary>
public sealed class ManejadorExtraerRegistro(ILectorCurriculum lector, RutasCurriculum rutas) : IManejadorTarea
{
    public const string MensajeSinRegistro = $"No se encuentra {RutasCurriculum.ArchivoRegistro} en la carpeta Curriculum.";
    public const string MensajeSinTextoLibre = "El registro no tiene texto libre fuera de la tabla y los rastreos: no hay nada que extraer.";
    public const string MensajeDemasiadoLargo = "El texto libre del registro es demasiado largo para leerlo de una vez (más de 250.000 caracteres).";

    /// <summary>Largo máximo del texto libre en el prompt (unos 100.000 tokens): si se pasa, no se lanza.</summary>
    public const int LargoRegistro = 250_000;

    /// <summary>Límites del esquema (<c>prompts/extraer-registro.schema.json</c>), los mismos que los de <see cref="LimitesLote"/>.</summary>
    public const int MaximoDudas = 50, LargoDuda = 500, LargoSeccion = 300, LargoCita = 2000;

    // Lo que devuelve Claude se lee estricto: sin propiedades de más ni repetidas, sin nombres en otra caja, con todas las
    // del esquema y sin null donde no cabe. Los enumerados se leen como texto y se comparan aparte, letra a letra.
    private static readonly JsonSerializerOptions Lectura = new(JsonSerializerOptions.Strict)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions Guardado = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public DefinicionTarea Definicion => DefinicionesTareas.ExtraerRegistro;

    public Task<bool> ValidarAsync(int? candidaturaId, CancellationToken cancelacion)
    {
        if (candidaturaId is not null) throw new ValidacionException("Esta tarea no va con una candidatura.", "candidaturaId");
        if (!rutas.HayCarpeta) throw new ReglaNegocioException(RutasCurriculum.MensajeSinCarpeta);
        if (!File.Exists(rutas.Registro)) throw new ReglaNegocioException(MensajeSinRegistro);
        return Task.FromResult(true);
    }

    public async Task<PreparacionTarea> ConstruirAsync(EjecucionAgente ejecucion, ContextoTarea contexto, CancellationToken cancelacion)
    {
        var libre = AnalizadorRegistro.TextoLibre(await lector.LeerRegistroAsync(cancelacion));
        if (libre.Vacio)
            return PreparacionTarea.Terminada(new ResultadoTarea(MensajeSinTextoLibre, Guardar(new ResultadoExtraccionRegistro([], []))));
        var texto = libre.Texto;
        if (texto.Length > LargoRegistro) throw new ReglaNegocioException(MensajeDemasiadoLargo);

        var prompt = PlantillaPrompt.Rellenar(Prompts.Plantilla(Definicion), new Dictionary<string, DatoPrompt>
        {
            ["registro"] = new(texto, LargoRegistro, UnaLinea: false)
        });
        var argumentos = ConstructorArgumentos.Construir(Definicion, Prompts.Esquema(Definicion), new ValoresArgumentos(contexto.CarpetaDatos));
        return PreparacionTarea.ConClaude(contexto.Peticion(Definicion, argumentos, prompt));
    }

    public Task<ResultadoTarea> AplicarAsync(EjecucionAgente ejecucion, RespuestaClaude respuesta, CancellationToken cancelacion)
    {
        var resultado = Validar(respuesta.SalidaEstructurada);
        return Task.FromResult(new ResultadoTarea(Resumen(resultado), Guardar(resultado)));
    }

    /// <summary>
    /// La salida de Claude con los límites del esquema: como mucho 1000 filas y 50 dudas, cada campo con su largo, la fecha
    /// AAAA-MM-DD y los enumerados con su nombre exacto (ni números, ni otra caja, ni espacios, ni «Enviada, Cerrada»). Antes
    /// de medir, quita de los textos los controles, invisibles y marcas de dirección. Si algo no cumple, toda la salida es
    /// <see cref="MensajesEjecucion.SalidaNoValida"/>.
    /// </summary>
    /// <exception cref="ReglaNegocioException">La salida no cumple el esquema.</exception>
    public static ResultadoExtraccionRegistro Validar(JsonElement? salida)
    {
        try
        {
            if (salida is { ValueKind: JsonValueKind.Object } objeto && objeto.Deserialize<SalidaCruda>(Lectura) is { } cruda
                && cruda.Candidaturas.Count <= LimitesLote.Filas && cruda.Dudas.Count <= MaximoDudas)
                return new ResultadoExtraccionRegistro(
                    cruda.Candidaturas.Select(Fila).ToList(),
                    cruda.Dudas.Select(d => Texto(d, LargoDuda)).ToList());
        }
        catch (JsonException)
        {
            // Un tipo que no es el del esquema, un campo de más o de menos, o un valor fuera de sus límites.
        }
        throw new ReglaNegocioException(MensajesEjecucion.SalidaNoValida);
    }

    private static FilaExtraida Fila(FilaCruda? f) => f is null ? throw new JsonException() : new FilaExtraida(
        Texto(f.Seccion, LargoSeccion),
        Fecha(f.Fecha),
        f.FechaAproximada,
        Texto(f.Empresa, LimitesLote.Empresa, obligatorio: true),
        Texto(f.Puesto, LimitesLote.Puesto, obligatorio: true),
        Enumerado<EstadoCandidatura>(f.Estado),
        Opcional(f.Enlace, LimitesLote.Enlace),
        Enumerado<Modalidad>(f.Modalidad),
        Opcional(f.Ubicacion, LimitesLote.Ubicacion),
        f.MotivoDescarte is null ? null : Enumerado<MotivoDescarte>(f.MotivoDescarte),
        Opcional(f.Nota, LimitesLote.Nota),
        Texto(f.Cita, LargoCita, obligatorio: true));

    /// <summary>Sin invisibles y sin espacios en los extremos; vacío si no es <paramref name="obligatorio"/>.</summary>
    private static string Texto(string? valor, int largo, bool obligatorio = false)
    {
        var limpio = PlantillaPrompt.QuitarInvisibles(valor ?? throw new JsonException()).Trim();
        return limpio.Length > largo || (obligatorio && limpio.Length == 0) ? throw new JsonException() : limpio;
    }

    private static string? Opcional(string? valor, int largo) =>
        valor is null ? null : Texto(valor, largo) is { Length: > 0 } limpio ? limpio : null;

    private static DateOnly Fecha(string valor) =>
        valor.Length == 10 && DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? dia
            : throw new JsonException();

    private static T Enumerado<T>(string valor) where T : struct, Enum =>
        Enum.GetNames<T>().Contains(valor, StringComparer.Ordinal) ? Enum.Parse<T>(valor) : throw new JsonException();

    private static string Guardar(ResultadoExtraccionRegistro resultado) => JsonSerializer.Serialize(resultado, Guardado);

    /// <summary>«Ha encontrado 34 candidaturas en el texto libre.», y cuántas dudas tiene si las hay.</summary>
    private static string Resumen(ResultadoExtraccionRegistro r)
    {
        var encontradas = r.Candidaturas.Count switch
        {
            0 => "No ha encontrado candidaturas en el texto libre.",
            1 => "Ha encontrado 1 candidatura en el texto libre.",
            var n => $"Ha encontrado {n} candidaturas en el texto libre."
        };
        return r.Dudas.Count switch
        {
            0 => encontradas,
            1 => $"{encontradas} Tiene 1 duda.",
            var n => $"{encontradas} Tiene {n} dudas."
        };
    }

    // Lo que dice el esquema, con los enumerados como texto para compararlos aquí letra a letra.
    private sealed record SalidaCruda(IReadOnlyList<FilaCruda?> Candidaturas, IReadOnlyList<string?> Dudas);

    private sealed record FilaCruda(
        string Seccion, string Fecha, bool FechaAproximada, string Empresa, string Puesto, string Estado, string? Enlace,
        string Modalidad, string? Ubicacion, string? MotivoDescarte, string? Nota, string Cita);
}
