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
    public const string MensajeRegistroCambiandoAlEmpezar = "El registro ha cambiado justo al empezar la extracción. Vuelve a lanzarla.";

    /// <summary>Largo máximo del texto libre en el prompt (unos 100.000 tokens): si se pasa, no se lanza.</summary>
    public const int LargoRegistro = 250_000;

    /// <summary>Límites del esquema (<c>prompts/extraer-registro.schema.json</c>), los mismos que los de <see cref="LimitesLote"/>.</summary>
    public const int MaximoDudas = 50, LargoDuda = 500, LargoSeccion = 300, LargoCita = 2000;

    /// <summary>Lo que se guarda de la cita de una fila sin leer, para encontrarla en el registro.</summary>
    public const int LargoCitaSinLeer = 200;

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

    /// <summary>
    /// El texto del prompt sale de los mismos bytes que el hash que se compara al aplicar: si no son los del hash guardado al
    /// empezar (<see cref="ContextoTarea.HashRegistroAntes"/>; también si ese hash no se pudo leer), la extracción nunca se
    /// podría aplicar y acaba en Error sin lanzar Claude.
    /// </summary>
    public async Task<PreparacionTarea> ConstruirAsync(EjecucionAgente ejecucion, ContextoTarea contexto, CancellationToken cancelacion)
    {
        var leido = await lector.LeerRegistroConFechaAsync(cancelacion);
        if (!string.Equals(leido.Hash, contexto.HashRegistroAntes, StringComparison.Ordinal))
            throw new ReglaNegocioException(MensajeRegistroCambiandoAlEmpezar);
        var texto = AnalizadorRegistro.TextoLibre(leido.Texto).Texto;
        // Se mide lo que de verdad iría al prompt (sin invisibles y con NFKC, que puede alargarlo): así Rellenar nunca
        // recorta en silencio, y un texto que solo tiene invisibles cuenta como vacío.
        var limpio = PlantillaPrompt.Normalizar(texto, unaLinea: false);
        if (string.IsNullOrWhiteSpace(limpio))
            return PreparacionTarea.Terminada(new ResultadoTarea(MensajeSinTextoLibre, Guardar(new ResultadoExtraccionRegistro([], [], []))));
        if (limpio.Length > LargoRegistro) throw new ReglaNegocioException(MensajeDemasiadoLargo);

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
    /// La salida de Claude contra el esquema, en dos niveles. La <b>estructura</b> (lectura estricta: sin campos de más, de
    /// menos ni repetidos, con sus tipos; como mucho 1000 filas y 50 dudas) o vale entera o es
    /// <see cref="MensajesEjecucion.SalidaNoValida"/>. Dentro, una <b>fila</b> que no cumple (fecha imposible, enumerado que
    /// no es exactamente uno de los nombres, campo obligatorio vacío o más largo que el esquema) va a
    /// <see cref="ResultadoExtraccionRegistro.SinLeer"/> sin tumbar las demás. Antes de medir, los textos se quedan sin
    /// controles, invisibles ni marcas de dirección, y los de una línea sin saltos; los largos se cuentan en caracteres
    /// Unicode, como el esquema. Una duda demasiado larga se recorta.
    /// </summary>
    /// <exception cref="ReglaNegocioException">La estructura no cumple el esquema.</exception>
    public static ResultadoExtraccionRegistro Validar(JsonElement? salida)
    {
        SalidaCruda? cruda = null;
        try
        {
            if (salida is { ValueKind: JsonValueKind.Object } objeto) cruda = objeto.Deserialize<SalidaCruda>(Lectura);
        }
        catch (JsonException)
        {
            // Un tipo que no es el del esquema, o un campo de más, de menos o repetido.
        }
        if (cruda is null || cruda.Candidaturas.Count > LimitesLote.Filas || cruda.Dudas.Count > MaximoDudas
            || cruda.Candidaturas.Any(f => f is null) || cruda.Dudas.Any(d => d is null))
            throw new ReglaNegocioException(MensajesEjecucion.SalidaNoValida);

        var filas = new List<FilaExtraida>();
        var sinLeer = new List<FilaExtraidaSinLeer>();
        foreach (var fila in cruda.Candidaturas)
        {
            try
            {
                filas.Add(Fila(fila!));
            }
            catch (FilaNoValida error)
            {
                sinLeer.Add(new FilaExtraidaSinLeer(Linea(fila!.Cita) is { Length: > 0 } cita ? Recortar(cita, LargoCitaSinLeer) : "(sin cita)",
                    error.Message));
            }
        }
        var dudas = cruda.Dudas.Select(d => Recortar(Linea(d!), LargoDuda)).Where(d => d.Length > 0).ToList();
        return new ResultadoExtraccionRegistro(filas, dudas, sinLeer);
    }

    private static FilaExtraida Fila(FilaCruda f) => new(
        Texto(f.Seccion, LargoSeccion, "seccion", obligatorio: false),
        Fecha(f.Fecha),
        f.FechaAproximada,
        Texto(f.Empresa, LimitesLote.Empresa, "empresa"),
        Texto(f.Puesto, LimitesLote.Puesto, "puesto"),
        Enumerado<EstadoCandidatura>(f.Estado, "estado"),
        Opcional(f.Enlace, LimitesLote.Enlace, "enlace"),
        Enumerado<Modalidad>(f.Modalidad, "modalidad"),
        Opcional(f.Ubicacion, LimitesLote.Ubicacion, "ubicacion"),
        f.MotivoDescarte is null ? null : Enumerado<MotivoDescarte>(f.MotivoDescarte, "motivoDescarte"),
        Opcional(f.Nota, LimitesLote.Nota, "nota", unaLinea: false),
        Texto(f.Cita, LargoCita, "cita"));

    private static string Texto(string valor, int largo, string campo, bool obligatorio = true, bool unaLinea = true)
    {
        var limpio = unaLinea ? Linea(valor) : PlantillaPrompt.QuitarInvisibles(valor).Trim();
        if (obligatorio && limpio.Length == 0) throw new FilaNoValida($"Falta el campo «{campo}».");
        return Largo(limpio) > largo ? throw new FilaNoValida($"El campo «{campo}» pasa de {largo} caracteres.") : limpio;
    }

    private static string? Opcional(string? valor, int largo, string campo, bool unaLinea = true) =>
        valor is null ? null : Texto(valor, largo, campo, obligatorio: false, unaLinea) is { Length: > 0 } limpio ? limpio : null;

    /// <summary>Sin saltos de ningún tipo ni tabuladores (pasan a espacio), sin invisibles y sin espacios en los extremos.</summary>
    private static string Linea(string valor) =>
        PlantillaPrompt.QuitarInvisibles(PlantillaPrompt.EnUnaLinea(valor).Replace('\t', ' ')).Trim();

    private static int Largo(string texto) => texto.EnumerateRunes().Count();

    /// <summary>Recorta a <paramref name="maximo"/> caracteres Unicode, sin partir ninguno.</summary>
    private static string Recortar(string texto, int maximo) => Largo(texto) <= maximo
        ? texto
        : string.Concat(texto.EnumerateRunes().Take(maximo - 1).Select(r => r.ToString())) + "…";

    private static DateOnly Fecha(string valor) =>
        valor.Length == 10 && DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? dia
            : throw new FilaNoValida($"La fecha «{Recortar(Linea(valor), 40)}» no es un día válido con la forma AAAA-MM-DD.");

    private static T Enumerado<T>(string valor, string campo) where T : struct, Enum =>
        Enum.GetNames<T>().Contains(valor, StringComparer.Ordinal)
            ? Enum.Parse<T>(valor)
            : throw new FilaNoValida($"El campo «{campo}» no admite «{Recortar(Linea(valor), 40)}».");

    private static string Guardar(ResultadoExtraccionRegistro resultado) => JsonSerializer.Serialize(resultado, Guardado);

    /// <summary>Lo que se ha guardado en <c>ResultadoJson</c>, con las mismas opciones, o null si falta o no se puede leer.</summary>
    public static ResultadoExtraccionRegistro? LeerGuardado(string? json)
    {
        if (json is null) return null;
        try
        {
            // Una lista que falta llega como null: eso tampoco es algo que haya guardado Guardar.
            return JsonSerializer.Deserialize<ResultadoExtraccionRegistro>(json, Guardado) is { Candidaturas: not null, Dudas: not null, SinLeer: not null } leido
                ? leido
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>«Ha encontrado 34 candidaturas en el texto libre.», y cuántas filas no se han leído y cuántas dudas hay.</summary>
    private static string Resumen(ResultadoExtraccionRegistro r)
    {
        var texto = r.Candidaturas.Count switch
        {
            0 => "No ha encontrado candidaturas en el texto libre.",
            1 => "Ha encontrado 1 candidatura en el texto libre.",
            var n => $"Ha encontrado {n} candidaturas en el texto libre."
        };
        texto += r.SinLeer.Count switch
        {
            0 => "",
            1 => " 1 no se ha podido leer.",
            var n => $" {n} no se han podido leer."
        };
        return texto + r.Dudas.Count switch
        {
            0 => "",
            1 => " Tiene 1 duda.",
            var n => $" Tiene {n} dudas."
        };
    }

    /// <summary>Una fila que cumple el esquema pero no las reglas: va a <see cref="ResultadoExtraccionRegistro.SinLeer"/>.</summary>
    private sealed class FilaNoValida(string motivo) : Exception(motivo);

    // Lo que dice el esquema, con los enumerados como texto para compararlos aquí letra a letra.
    private sealed record SalidaCruda(IReadOnlyList<FilaCruda?> Candidaturas, IReadOnlyList<string?> Dudas);

    private sealed record FilaCruda(
        string Seccion, string Fecha, bool FechaAproximada, string Empresa, string Puesto, string Estado, string? Enlace,
        string Modalidad, string? Ubicacion, string? MotivoDescarte, string? Nota, string Cita);
}
