using System.Net;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;

namespace RadarEmpleo.Api.Seguridad;

/// <summary>
/// Reconoce el canal por el NOMBRE del endpoint de Kestrel: <c>Program.cs</c> marca cada conexión del endpoint
/// «Local» o «Remoto» con <see cref="MarcarCanal"/>, y aquí se lee esa marca de <see cref="IConnectionItemsFeature"/>.
/// Si la petición no trae esa característica (plan B), se compara el puerto local con los de
/// <c>Kestrel:Endpoints:Local:Url</c> y <c>…:Remoto:Url</c> leídos al arrancar (nunca un puerto escrito en el código).
/// Sin marca ni puerto conocido: <see cref="CanalPeticion.Desconocido"/>, que se trata como Remoto. Además, Local exige
/// que la conexión venga de la propia máquina (loopback): si alguien cambia la URL de «Local» a <c>0.0.0.0</c>, lo que
/// llega de fuera no cuenta como el PC.
/// </summary>
public sealed class ClasificadorCanalKestrel : IClasificadorCanal
{
    /// <summary>Clave de la marca en los <c>Items</c> de la conexión.</summary>
    public const string ClaveCanal = "RadarEmpleo.Canal";

    private readonly int? _puertoLocal;
    private readonly int? _puertoRemoto;

    public ClasificadorCanalKestrel(IConfiguration configuracion)
    {
        _puertoLocal = Puerto(configuracion["Kestrel:Endpoints:Local:Url"]);
        _puertoRemoto = Puerto(configuracion["Kestrel:Endpoints:Remoto:Url"]);
    }

    public CanalPeticion Canal(HttpContext http)
    {
        var canal = PorEndpoint(http);
        return canal == CanalPeticion.Local && !(http.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip))
            ? CanalPeticion.Desconocido
            : canal;
    }

    private CanalPeticion PorEndpoint(HttpContext http)
    {
        if (http.Features.Get<IConnectionItemsFeature>() is { } conexion)
            return conexion.Items.TryGetValue(ClaveCanal, out var marca) && marca is CanalPeticion canal ? canal : CanalPeticion.Desconocido;

        var puerto = http.Connection.LocalPort;
        if (puerto != 0 && puerto == _puertoLocal) return CanalPeticion.Local;
        if (puerto != 0 && puerto == _puertoRemoto) return CanalPeticion.Remoto;
        return CanalPeticion.Desconocido;
    }

    /// <summary>Middleware de conexión que marca todo lo que entra por un endpoint con su canal.</summary>
    public static Func<ConnectionDelegate, ConnectionDelegate> MarcarCanal(CanalPeticion canal) => siguiente => conexion =>
    {
        conexion.Items[ClaveCanal] = canal;
        return siguiente(conexion);
    };

    private static int? Puerto(string? url) =>
        Uri.TryCreate(url?.Replace("*", "localhost", StringComparison.Ordinal).Replace("+", "localhost", StringComparison.Ordinal),
            UriKind.Absolute, out var uri) ? uri.Port : null;
}
