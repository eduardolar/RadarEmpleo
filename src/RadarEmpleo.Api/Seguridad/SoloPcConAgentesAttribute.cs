using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using RadarEmpleo.Api.Configuracion;

namespace RadarEmpleo.Api.Seguridad;

/// <summary>¿Se pueden usar los agentes desde esta petición? Lo comparten el filtro, Sistema y el estado de los agentes.</summary>
public sealed class DisponibilidadAgentes(IClasificadorCanal clasificador, IOptions<OpcionesAgentes> agentes, IOptions<OpcionesRadar> radar,
    IHostEnvironment entorno)
{
    public const string MensajeSoloPc = "Los agentes solo se pueden lanzar desde el PC.";
    public const string MensajeApagados = "Los agentes están apagados.";
    public const string MensajeOtroSitio = "Esta petición no viene de RadarEmpleo.";

    /// <summary>
    /// El front de desarrollo (<c>ng serve</c>): con su proxy llega <c>same-origin</c>, pero en el entorno Development también
    /// se admite como <c>same-site</c>. Fuera de Development, no.
    /// </summary>
    public const string OrigenFrontDesarrollo = "http://localhost:4200";

    /// <summary>Si están encendidos en la configuración y la app va en modo Local (nunca en la demo).</summary>
    public bool Encendidos => agentes.Value.Habilitado && radar.Value.Modo == ModoEjecucion.Local;

    /// <summary>
    /// El motivo por el que no están disponibles, o null si lo están: el canal, la configuración y, por último, de
    /// dónde viene la petición. Con <c>Sec-Fetch-Site</c> solo pasan <c>same-origin</c>, <c>none</c> o sin cabecera
    /// (no la mandan los navegadores antiguos ni las herramientas), y <c>same-site</c> solo si <c>Origin</c> es la propia
    /// API o (solo en Development) el front de desarrollo. <c>localhost:8099</c> y cualquier otro puerto de la máquina son
    /// <c>same-site</c>.
    /// </summary>
    public string? Motivo(HttpContext http)
    {
        if (!DesdeElPc(http)) return MensajeSoloPc;
        if (!Encendidos) return MensajeApagados;
        return DesdeRadarEmpleo(http.Request, entorno.IsDevelopment()) ? null : MensajeOtroSitio;
    }

    /// <summary>Lo que <c>/api/sistema/info</c> puede decir de <c>Agentes:Habilitado</c>: por el remoto, siempre false.</summary>
    public bool HabilitadosVisibles(HttpContext http) =>
        DesdeElPc(http) && agentes.Value.Habilitado;

    /// <summary>Si la petición llega por el endpoint «Local» (el PC) y no por el remoto (el móvil).</summary>
    public bool DesdeElPc(HttpContext http) => clasificador.Canal(http) == CanalPeticion.Local;

    private static bool DesdeRadarEmpleo(HttpRequest peticion, bool desarrollo)
    {
        var sitio = peticion.Headers["Sec-Fetch-Site"].ToString();
        if (sitio.Length == 0 || sitio.Equals("same-origin", StringComparison.OrdinalIgnoreCase) || sitio.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!sitio.Equals("same-site", StringComparison.OrdinalIgnoreCase)) return false;
        var origen = peticion.Headers.Origin.ToString();
        return origen.Equals($"{peticion.Scheme}://{peticion.Host}", StringComparison.OrdinalIgnoreCase)
            || (desarrollo && origen.Equals(OrigenFrontDesarrollo, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Solo deja pasar si la petición llega por el endpoint «Local», los agentes están encendidos
/// (<c>Agentes:Habilitado</c> y modo Local) y viene de RadarEmpleo (<see cref="DisponibilidadAgentes.Motivo"/>). Si no, 403
/// con ProblemDetails en español. Es un filtro de autorización: va antes que el 415 de <c>[Consumes]</c> y que el
/// 400 de la validación, así que por el móvil ni siquiera se lee el cuerpo.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SoloPcConAgentesAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext contexto)
    {
        var http = contexto.HttpContext;
        var motivo = http.RequestServices.GetRequiredService<DisponibilidadAgentes>().Motivo(http);
        if (motivo is null) return;

        contexto.Result = Prohibido(motivo);
    }

    /// <summary>
    /// El 403 del filtro, para los endpoints que solo lo exigen según lo que piden (aplicar una extracción de Claude).
    /// </summary>
    public static ObjectResult Prohibido(string motivo) =>
        new(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "No tienes permiso para hacer esto.",
            Detail = motivo,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4"
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" }
        };
}
