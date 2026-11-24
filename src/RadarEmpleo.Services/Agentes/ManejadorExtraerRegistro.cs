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
/// elija. El registro solo se lee. El texto libre va <b>por partes</b> (<see cref="PartidorTextoLibre"/>, como mucho
/// <see cref="PartesTarea.MaximoPartes"/>), una consulta cada una dentro de la misma ejecución, y lo de todas se junta en un
/// solo resultado: si alguna no se ha podido leer, queda una duda que lo dice.
/// </summary>
public sealed class ManejadorExtraerRegistro(ILectorCurriculum lector, RutasCurriculum rutas) : IManejadorTarea
{
    public const string MensajeSinRegistro = $"No se encuentra {RutasCurriculum.ArchivoRegistro} en la carpeta Curriculum.";
    public const string MensajeSinTextoLibre = "El registro no tiene texto libre fuera de la tabla y los rastreos: no hay nada que extraer.";
    public const string MensajeDemasiadoLargo =
        "El texto libre del registro es demasiado largo para leerlo con Claude: haría falta leerlo en más de 6 partes.";
    public const string MensajeRegistroCambiandoAlEmpezar = "El registro ha cambiado justo al empezar la extracción. Vuelve a lanzarla.";
    public const string MensajeRegistroSinHashAlEmpezar = "No se ha podido leer el registro al empezar. Vuelve a lanzarla.";

    /// <summary>
    /// Recorte de seguridad del texto libre (unos 100.000 tokens): si se pasa, no se lanza. Lo normal es que antes se pase de
    /// las 6 partes.
    /// </summary>
    public const int LargoRegistro = 250_000;

    /// <summary>Lo que se pone como mucho del título de una parte perdida.</summary>
    public const int LargoTituloParte = 80;

    // El título con el que empieza cada parte, según su petición («2 de 4»): para decir desde dónde empezaba la que no se lea.
    // Lo apunta ConstruirAsync y lo lee AplicarPartesAsync, que el procesador llama sobre el mismo manejador (un ámbito por
    // ejecución).
    private readonly Dictionary<string, string?> _desde = new(StringComparer.Ordinal);

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

    // Al volver a leer lo guardado: sin campos que falten ni null donde no cabe (los elementos de las listas, aparte).
    private static readonly JsonSerializerOptions LecturaGuardado = new(Guardado)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
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
    /// empezar (<see cref="ContextoTarea.HashRegistroAntes"/>), o si ese hash no se pudo leer, la extracción nunca se podría
    /// aplicar y acaba en Error sin lanzar Claude.
    /// </summary>
    public async Task<PreparacionTarea> ConstruirAsync(EjecucionAgente ejecucion, ContextoTarea contexto, CancellationToken cancelacion)
    {
        var leido = await lector.LeerRegistroConFechaAsync(cancelacion);
        if (contexto.HashRegistroAntes is null) throw new ReglaNegocioException(MensajeRegistroSinHashAlEmpezar);
        if (!string.Equals(leido.Hash, contexto.HashRegistroAntes, StringComparison.Ordinal))
            throw new ReglaNegocioException(MensajeRegistroCambiandoAlEmpezar);
        var libre = AnalizadorRegistro.TextoLibre(leido.Texto);
        // Se mide lo que de verdad iría al prompt (sin invisibles y con NFKC, que puede alargarlo): así Rellenar nunca
        // recorta en silencio, y un texto que solo tiene invisibles cuenta como vacío.
        var limpio = PlantillaPrompt.Normalizar(libre.Texto, unaLinea: false);
        var partes = Definicion.Partes!;
        var trozos = string.IsNullOrWhiteSpace(limpio) ? [] : PartidorTextoLibre.Partir(libre, partes.LargoParte);
        if (trozos.Count == 0)
            return PreparacionTarea.Terminada(new ResultadoTarea(MensajeSinTextoLibre, Guardar(new ResultadoExtraccionRegistro([], [], []))));
        if (limpio.Length > LargoRegistro || trozos.Count > partes.MaximoPartes) throw new ReglaNegocioException(MensajeDemasiadoLargo);

        var n = trozos.Count;
        var plantilla = Prompts.Plantilla(Definicion);
        var argumentos = ConstructorArgumentos.Construir(Definicion, Prompts.Esquema(Definicion), new ValoresArgumentos(contexto.CarpetaDatos),
            Definicion.PresupuestoParte(n));
        var tiempo = Definicion.TiempoParte(n);
        _desde.Clear();
        var peticiones = trozos.Select((trozo, i) =>
        {
            var parte = $"{i + 1} de {n}";
            _desde[parte] = trozo.Titulo;
            var prompt = PlantillaPrompt.Rellenar(plantilla, new Dictionary<string, DatoPrompt>
            {
                ["parte"] = new(parte, 20),
                ["registro"] = new(trozo.Texto, LargoRegistro, UnaLinea: false)
            });
            return contexto.Peticion(Definicion, argumentos, prompt, tiempo, parte);
        }).ToList();
        return PreparacionTarea.ConClaude(peticiones);
    }

    public Task<ResultadoTarea> AplicarAsync(EjecucionAgente ejecucion, RespuestaClaude respuesta, CancellationToken cancelacion)
    {
        var resultado = Validar(respuesta.SalidaEstructurada);
        return Task.FromResult(new ResultadoTarea(Resumen(resultado, 0), Guardar(resultado)));
    }

    /// <summary>
    /// Cada parte se valida por separado (<see cref="Validar"/>). Se juntan, en el orden de las partes, las candidaturas y
    /// las filas sin leer; las repetidas entre partes las quita después el combinador de la importación. Una parte que no
    /// ha salido o cuya estructura no cumple el esquema deja una duda que lo dice; las dudas de las partes perdidas van antes
    /// que las de Claude, sin repetidas y como mucho <see cref="MaximoDudas"/>. Si no vale ninguna parte,
    /// <see cref="MensajesEjecucion.SalidaNoValida"/>. La forma de lo guardado es la de siempre.
    /// </summary>
    public Task<ResultadoTarea> AplicarPartesAsync(EjecucionAgente ejecucion,
        IReadOnlyList<(PeticionClaude Peticion, RespuestaClaude Respuesta)> partes, CancellationToken cancelacion)
    {
        var leidas = new List<ResultadoExtraccionRegistro>();
        var perdidas = new List<string>();
        foreach (var (peticion, respuesta) in partes)
        {
            try
            {
                if (respuesta.Correcta)
                {
                    leidas.Add(Validar(respuesta.SalidaEstructurada));
                    continue;
                }
            }
            catch (ReglaNegocioException)
            {
                // La estructura de esta parte no cumple el esquema: cuenta como perdida.
            }
            perdidas.Add(DudaParte(peticion, respuesta));
        }
        if (leidas.Count == 0) throw new ReglaNegocioException(MensajesEjecucion.SalidaNoValida);

        var resultado = new ResultadoExtraccionRegistro(
            leidas.SelectMany(r => r.Candidaturas).ToList(),
            perdidas.Concat(leidas.SelectMany(r => r.Dudas)).Distinct(StringComparer.Ordinal).Take(MaximoDudas).ToList(),
            leidas.SelectMany(r => r.SinLeer).ToList());
        return Task.FromResult(new ResultadoTarea(Resumen(resultado, perdidas.Count), Guardar(resultado)));
    }

    /// <summary>«No se ha podido leer la parte 2 de 4 (desde «## Agosto»): ha tardado más de 10 min y la he parado.»</summary>
    private string DudaParte(PeticionClaude peticion, RespuestaClaude respuesta)
    {
        var parte = peticion.Parte ?? "1 de 1";
        var desde = !_desde.TryGetValue(parte, out var titulo) ? ""
            : titulo is null ? " (desde el principio)"
            : $" (desde «{Recortar(Linea(titulo), LargoTituloParte)}»)";
        var fallo = respuesta.Correcta ? FalloClaude.SalidaNoValida : respuesta.Fallo ?? FalloClaude.ErrorDeClaude;
        var motivo = fallo switch
        {
            FalloClaude.TiempoAgotado => $"ha tardado más de {MensajesEjecucion.Duracion(peticion.TiempoMaximo)} y la he parado",
            FalloClaude.PresupuestoAgotado => "ha llegado al tope de uso de la parte y la he parado",
            FalloClaude.SalidaNoValida => "Claude ha respondido en un formato que no entiendo",
            FalloClaude.NoInstalado => "no encuentro Claude Code en este PC",
            _ => "Claude ha devuelto un error"
        };
        return Recortar($"No se ha podido leer la parte {parte}{desde}: {motivo}.", LargoDuda);
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
            // Las opciones no miran dentro de las listas: un elemento null tampoco es algo que haya guardado Guardar.
            return JsonSerializer.Deserialize<ResultadoExtraccionRegistro>(json, LecturaGuardado) is { } leido
                && !leido.Candidaturas.Any(f => f is null) && !leido.Dudas.Any(d => d is null) && !leido.SinLeer.Any(f => f is null)
                ? leido
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// «Ha encontrado 34 candidaturas en el texto libre.», y cuántas filas no se han leído, cuántas partes se han perdido y
    /// cuántas dudas hay.
    /// </summary>
    private static string Resumen(ResultadoExtraccionRegistro r, int partesPerdidas)
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
        texto += partesPerdidas switch
        {
            0 => "",
            1 => " 1 parte no se ha podido leer.",
            var n => $" {n} partes no se han podido leer."
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
