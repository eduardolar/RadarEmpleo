using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;

namespace RadarEmpleo.Services.Ofertas;

/// <summary>Resuelve un nombre a sus direcciones. Inyectable: los tests no consultan ningún DNS.</summary>
public interface IResolvedorDns
{
    Task<IPAddress[]> ResolverAsync(string host, CancellationToken cancelacion);
}

/// <summary>El DNS del sistema.</summary>
public sealed class ResolvedorDnsSistema : IResolvedorDns
{
    public Task<IPAddress[]> ResolverAsync(string host, CancellationToken cancelacion) => Dns.GetHostAddressesAsync(host, cancelacion);
}

/// <summary>La descarga iba a un sitio al que no se conecta: la red local, este PC u otro puerto que no es el 443.</summary>
public sealed class DireccionNoPermitidaException(string mensaje) : Exception(mensaje);

/// <summary>
/// Contra SSRF (apartado 6 del plan): resuelve el nombre, rechaza si <b>alguna</b> de sus direcciones está en
/// <see cref="DireccionesBloqueadas"/> o si el puerto no es el 443, y conecta a una de las direcciones ya comprobadas, sin
/// volver a resolver. Va en el <c>ConnectCallback</c> del <c>SocketsHttpHandler</c> de la descarga: cada conexión, también
/// la de una redirección, pasa por aquí. Si rechaza, no abre ningún socket.
/// </summary>
public sealed class ConectorSeguro
{
    /// <summary>El único puerto al que se conecta.</summary>
    public const int PuertoPermitido = 443;

    private readonly IResolvedorDns _resolvedor;
    private readonly Func<IReadOnlyList<RedLocal>> _redesDeEstePc;
    private readonly Func<IPEndPoint, CancellationToken, ValueTask<Stream>> _abrir;

    /// <param name="redesDeEstePc">Las redes de este PC (por defecto, <see cref="DireccionesBloqueadas.DeEstePc"/>, leídas en cada conexión).</param>
    /// <param name="abrir">Abre la conexión con una dirección ya comprobada (por defecto, un socket TCP). Para los tests.</param>
    public ConectorSeguro(IResolvedorDns resolvedor, Func<IReadOnlyList<RedLocal>>? redesDeEstePc = null,
        Func<IPEndPoint, CancellationToken, ValueTask<Stream>>? abrir = null)
    {
        _resolvedor = resolvedor;
        _redesDeEstePc = redesDeEstePc ?? DireccionesBloqueadas.DeEstePc;
        _abrir = abrir ?? AbrirSocketAsync;
    }

    /// <summary>Las direcciones de <paramref name="host"/>, si todas se pueden usar. Una IP escrita tal cual no se resuelve.</summary>
    /// <exception cref="DireccionNoPermitidaException">Otro puerto, sin direcciones o alguna bloqueada.</exception>
    public async Task<IPAddress[]> ComprobarAsync(string host, int puerto, CancellationToken cancelacion)
    {
        if (puerto != PuertoPermitido) throw new DireccionNoPermitidaException("Solo se descarga por el puerto 443.");
        var nombre = host.Trim().TrimStart('[').TrimEnd(']');
        if (nombre.Length == 0) throw new DireccionNoPermitidaException("La dirección no tiene nombre.");
        var direcciones = IPAddress.TryParse(nombre, out var literal) ? [literal] : await _resolvedor.ResolverAsync(nombre, cancelacion);
        if (direcciones.Length == 0) throw new DireccionNoPermitidaException("El nombre no tiene direcciones.");
        var redes = _redesDeEstePc();
        if (direcciones.Any(d => DireccionesBloqueadas.EstaBloqueada(d, redes)))
            throw new DireccionNoPermitidaException("La dirección es de la red local o de este PC.");
        return direcciones;
    }

    /// <summary>Comprueba el destino y conecta con la primera de sus direcciones que responda.</summary>
    /// <exception cref="DireccionNoPermitidaException">El destino no se admite (no se ha abierto nada).</exception>
    public async ValueTask<Stream> ConectarAsync(DnsEndPoint destino, CancellationToken cancelacion)
    {
        var direcciones = await ComprobarAsync(destino.Host, destino.Port, cancelacion);
        ExceptionDispatchInfo? ultimo = null;
        foreach (var direccion in direcciones)
        {
            try
            {
                return await _abrir(new IPEndPoint(direccion, destino.Port), cancelacion);
            }
            catch (SocketException error)
            {
                ultimo = ExceptionDispatchInfo.Capture(error);
            }
        }
        ultimo!.Throw();
        throw new InvalidOperationException(); // no se llega: Throw siempre lanza
    }

    private static async ValueTask<Stream> AbrirSocketAsync(IPEndPoint destino, CancellationToken cancelacion)
    {
        var socket = new Socket(destino.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(destino, cancelacion);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
