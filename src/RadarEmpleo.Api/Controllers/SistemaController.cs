using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RadarEmpleo.Api.Configuracion;

namespace RadarEmpleo.Api.Controllers;

/// <summary>Información del sistema para el front (modo, agentes y versión).</summary>
/// <param name="Modo">Local (PC de Eduardo) o Demo (datos inventados).</param>
/// <param name="AgentesHabilitados">Si los agentes de Claude están activos (Agentes:Habilitado).</param>
/// <param name="Version">Versión de la API.</param>
public sealed record SistemaInfoDto(ModoEjecucion Modo, bool AgentesHabilitados, string Version);

[ApiController]
[Route("api/sistema")]
[Produces("application/json")]
public class SistemaController(IOptions<OpcionesRadar> radar, IOptions<OpcionesAgentes> agentes) : ControllerBase
{
    private static readonly string VersionApi = LeerVersion();

    /// <summary>Modo de ejecución, si los agentes están habilitados y versión de la API.</summary>
    [HttpGet("info")]
    public SistemaInfoDto Info() => new(radar.Value.Modo, agentes.Value.Habilitado, VersionApi);

    private static string LeerVersion()
    {
        var informativa = typeof(SistemaController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        // Quita el «+hash del commit» que añade el SDK.
        var mas = informativa.IndexOf('+');
        return mas >= 0 ? informativa[..mas] : informativa;
    }
}
