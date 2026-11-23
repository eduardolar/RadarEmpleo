using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RadarEmpleo.Api.Configuracion;
using RadarEmpleo.Api.Seguridad;

namespace RadarEmpleo.Api.Controllers;

/// <summary>Información del sistema para el front (modo, agentes y versión).</summary>
/// <param name="Modo">Local (PC de Eduardo) o Demo (datos inventados).</param>
/// <param name="AgentesHabilitados">Si los agentes de Claude están activos (Agentes:Habilitado). Por el móvil, siempre false.</param>
/// <param name="Version">Versión de la API.</param>
/// <param name="AgentesDisponibles">Si se pueden lanzar agentes desde esta petición: por el endpoint «Local», con los
/// agentes habilitados y en modo Local. Desde el móvil es false, y el front no enseña los botones.</param>
/// <param name="DesdeElPc">Si la petición llega por el endpoint «Local». Con él, el móvil explica que los agentes son
/// solo del PC en vez de decir que están apagados (por el remoto <paramref name="AgentesHabilitados"/> siempre es false).
/// No dice nada de la configuración: el móvil ya sabe por dónde entra.</param>
public sealed record SistemaInfoDto(ModoEjecucion Modo, bool AgentesHabilitados, string Version, bool AgentesDisponibles,
    bool DesdeElPc);

[ApiController]
[Route("api/sistema")]
[Produces("application/json")]
public class SistemaController(IOptions<OpcionesRadar> radar, DisponibilidadAgentes disponibilidad) : ControllerBase
{
    private static readonly string VersionApi = LeerVersion();

    /// <summary>Modo de ejecución, si los agentes están habilitados (y disponibles desde aquí) y versión de la API.</summary>
    [HttpGet("info")]
    public SistemaInfoDto Info() =>
        new(radar.Value.Modo, disponibilidad.HabilitadosVisibles(HttpContext), VersionApi, disponibilidad.Motivo(HttpContext) is null,
            disponibilidad.DesdeElPc(HttpContext));

    private static string LeerVersion()
    {
        var informativa = typeof(SistemaController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        // Quita el «+hash del commit» que añade el SDK.
        var mas = informativa.IndexOf('+');
        return mas >= 0 ? informativa[..mas] : informativa;
    }
}
