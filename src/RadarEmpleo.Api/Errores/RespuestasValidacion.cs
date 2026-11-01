using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.WebUtilities;

namespace RadarEmpleo.Api.Errores;

/// <summary>
/// Respuestas 400 en español. Sustituye la respuesta automática de <c>[ApiController]</c> cuando el
/// modelo no es válido (<see cref="CrearRespuesta"/>) para que:
/// <list type="bullet">
/// <item>el título sea «Hay datos que no son válidos.» y <c>detail</c> junte los mensajes, listo para el front;</item>
/// <item>un valor de tipo incorrecto en el JSON (un enum que no existe, un número donde va texto…)
/// salga como «El valor de «estado» no es válido.» sin nombres internos de clases ni rutas
/// <c>$.campo</c>;</item>
/// <item>la petición sin cuerpo, o con un JSON cortado o mal escrito, salga como «Falta el cuerpo de la
/// petición o no es un JSON válido.» y no como «The peticion field is required.».</item>
/// </list>
/// </summary>
public static class RespuestasValidacion
{
    public const string Titulo = "Hay datos que no son válidos.";

    private const string CuerpoIlegible = "Falta el cuerpo de la petición o no es un JSON válido.";

    /// <summary>
    /// ProblemDetails de validación (400) con el detalle en español y los errores por campo en
    /// <c>errors</c> (mismo formato que ValidationProblemDetails: campo → lista de mensajes). Va en
    /// <see cref="ProblemDetails.Extensions"/> y no como ValidationProblemDetails porque el escritor de
    /// ProblemDetails serializa el tipo base y perdería la propiedad <c>Errors</c>.
    /// </summary>
    public static ProblemDetails Problema(IDictionary<string, string[]> errores, string? detalle = null) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title = Titulo,
        Detail = detalle ?? string.Join(" ", errores.Values.SelectMany(m => m).Distinct()),
        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        Extensions = { ["errors"] = errores }
    };

    /// <summary>Para <see cref="ApiBehaviorOptions.InvalidModelStateResponseFactory"/>.</summary>
    public static IActionResult CrearRespuesta(ActionContext contexto)
    {
        var parametrosCuerpo = contexto.ActionDescriptor.Parameters
            .Where(p => p.BindingInfo?.BindingSource == BindingSource.Body)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var estado = contexto.ModelState;
        var hayErroresDeJson = estado.Any(e => e.Key.StartsWith('$') && e.Value?.Errors.Count > 0);

        var errores = new Dictionary<string, List<string>>();
        void Anadir(string campo, string mensaje)
        {
            if (!errores.TryGetValue(campo, out var lista)) errores[campo] = lista = [];
            if (!lista.Contains(mensaje)) lista.Add(mensaje);
        }

        foreach (var (clave, entrada) in estado)
        {
            if (entrada.Errors.Count == 0) continue;

            if (clave.StartsWith('$'))
            {
                // Error del lector de JSON: la clave es la ruta («$», «$.estado», «$.notas[0].texto»).
                // Un JSON cortado o mal escrito también trae la ruta del campo por el que iba
                // («{"puesto": » → «$.puesto»), pero el fallo es del cuerpo entero, no del valor.
                var campo = clave.TrimStart('$').TrimStart('.');
                if (campo.Length == 0 || entrada.Errors.Any(e => EsJsonMalFormado(e.Exception)))
                    Anadir("cuerpo", CuerpoIlegible);
                else
                    Anadir(campo, $"El valor de «{campo}» no es válido.");
            }
            else if (clave.Length == 0 || parametrosCuerpo.Contains(clave))
            {
                // La petición entera (sin cuerpo o nula tras un JSON roto). Si ya hay un error de JSON
                // más concreto, este sobra.
                if (!hayErroresDeJson) Anadir("cuerpo", CuerpoIlegible);
            }
            else
            {
                var campo = CamelCase(clave);
                foreach (var error in entrada.Errors)
                {
                    // Los mensajes de las anotaciones y de ModelBindingMessageProvider ya están en
                    // español; si alguno viniera de una excepción (sin mensaje), uno genérico.
                    Anadir(campo, string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? $"El valor de «{campo}» no es válido."
                        : error.ErrorMessage);
                }
            }
        }

        return new ResultadoProblema(Problema(errores.ToDictionary(e => e.Key, e => e.Value.ToArray())));
    }

    /// <summary>
    /// Escribe el ProblemDetails con <see cref="IProblemDetailsService"/>, como el manejador de
    /// excepciones: así sale con <c>application/problem+json</c> y <c>traceId</c> aunque el controlador
    /// tenga <c>[Produces("application/json")]</c> (que con un ObjectResult pisaría el tipo de contenido).
    /// </summary>
    private sealed class ResultadoProblema(ProblemDetails problema) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var http = context.HttpContext;
            http.Response.StatusCode = problema.Status ?? StatusCodes.Status400BadRequest;
            await http.RequestServices.GetRequiredService<IProblemDetailsService>()
                .WriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problema });
        }
    }

    /// <summary>
    /// Mensajes en español del enlace de modelos (query string, ruta y cuerpo vacío), que por
    /// defecto vienen en inglés («The value 'x' is not valid for desde.»).
    /// </summary>
    public static void TraducirMensajes(DefaultModelBindingMessageProvider m)
    {
        m.SetAttemptedValueIsInvalidAccessor((valor, campo) => $"El valor «{valor}» no es válido para «{campo}».");
        m.SetMissingBindRequiredValueAccessor(campo => $"Falta el valor de «{campo}».");
        m.SetMissingKeyOrValueAccessor(() => "Falta un valor.");
        m.SetMissingRequestBodyRequiredValueAccessor(() => CuerpoIlegible);
        m.SetNonPropertyAttemptedValueIsInvalidAccessor(valor => $"El valor «{valor}» no es válido.");
        m.SetNonPropertyUnknownValueIsInvalidAccessor(() => "El valor no es válido.");
        m.SetNonPropertyValueMustBeANumberAccessor(() => "Tiene que ser un número.");
        m.SetUnknownValueIsInvalidAccessor(campo => $"El valor de «{campo}» no es válido.");
        m.SetValueIsInvalidAccessor(valor => $"El valor «{valor}» no es válido.");
        m.SetValueMustBeANumberAccessor(campo => $"«{campo}» tiene que ser un número.");
        m.SetValueMustNotBeNullAccessor(valor => $"«{valor}» no puede estar vacío.");
    }

    /// <summary>
    /// Títulos en español para los ProblemDetails genéricos (404 de una ruta que no existe, 500…),
    /// sin pisar los títulos propios que ya traen los nuestros.
    /// </summary>
    public static void TraducirTitulo(ProblemDetailsContext contexto)
    {
        var problema = contexto.ProblemDetails;
        var status = problema.Status ?? contexto.HttpContext.Response.StatusCode;
        var tituloIngles = string.IsNullOrEmpty(problema.Title)
            || problema.Title == ReasonPhrases.GetReasonPhrase(status)
            || problema.Title == "An error occurred while processing your request."
            || problema.Title == "One or more validation errors occurred.";
        if (!tituloIngles) return;

        problema.Title = status switch
        {
            StatusCodes.Status400BadRequest => "La petición no es correcta.",
            StatusCodes.Status401Unauthorized => "Hace falta identificarse.",
            StatusCodes.Status403Forbidden => "No tienes permiso para hacer esto.",
            StatusCodes.Status404NotFound => "No se ha encontrado.",
            StatusCodes.Status405MethodNotAllowed => "Método no permitido.",
            StatusCodes.Status409Conflict => "No se puede hacer esa operación",
            StatusCodes.Status415UnsupportedMediaType => "El tipo de contenido no está admitido: envía JSON.",
            >= 500 => "Ha ocurrido un error inesperado.",
            _ => problema.Title
        };
    }

    /// <summary>
    /// ¿El error de lectura es de sintaxis (JSON cortado, una coma que falta, texto sin comillas…) y no
    /// de un valor bien escrito pero del tipo equivocado? System.Text.Json envuelve los errores del
    /// lector (<c>JsonReaderException</c>, interna, que hereda de <see cref="JsonException"/>) en una
    /// <see cref="JsonException"/> con la ruta; los de conversión (un número donde va texto, un enum
    /// que no existe) llegan como <see cref="JsonException"/> sin esa excepción interna. El formateador
    /// guarda la <see cref="JsonException"/> tal cual en el ModelState porque
    /// <c>AllowInputFormatterExceptionMessages</c> está desactivado.
    /// </summary>
    private static bool EsJsonMalFormado(Exception? error) =>
        error is JsonException { InnerException: JsonException };

    private static string CamelCase(string clave) =>
        string.Join('.', clave.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
