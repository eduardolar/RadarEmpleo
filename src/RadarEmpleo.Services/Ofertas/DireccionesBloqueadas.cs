using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RadarEmpleo.Services.Ofertas;

/// <summary>Una dirección de este PC con el prefijo de su red («192.168.1.20/24»).</summary>
public readonly record struct RedLocal(IPAddress Direccion, int Prefijo);

/// <summary>
/// Contra SSRF: a qué direcciones no se conecta nunca la descarga de ofertas (apartado 6 del plan). Todo lo que no es
/// una dirección pública de internet: sin especificar, loopback, privadas, link-local, CGNAT, de documentación, de
/// pruebas, multicast, reservadas y las de la red de este PC. Las IPv4 dentro de IPv6 (<c>::ffff:a.b.c.d</c>) se miran
/// como IPv4, y las de transición (NAT64, 6to4 y Teredo) se rechazan enteras, porque esconden una IPv4 cualquiera.
/// </summary>
public static class DireccionesBloqueadas
{
    private static readonly (byte[] Red, int Prefijo)[] Rangos =
    [
        .. new[]
        {
            "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.0.0.0/24",
            "192.0.2.0/24", "192.88.99.0/24", "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24",
            "224.0.0.0/4", "240.0.0.0/4",
            // IPv6: sin especificar y loopback (dentro de ::/96, las «compatibles con IPv4»), NAT64, descarte, Teredo,
            // documentación, 6to4, locales únicas, link-local, site-local (obsoletas) y multicast.
            "::/96", "::ffff:0:0:0/96", "64:ff9b::/96", "64:ff9b:1::/48", "100::/64", "2001::/32", "2001:db8::/32",
            "2002::/16", "fc00::/7", "fe80::/10", "fec0::/10", "ff00::/8"
        }.Select(Rango)
    ];

    /// <summary>Si no se puede conectar a <paramref name="direccion"/>.</summary>
    /// <param name="redesDeEstePc">Las direcciones de este PC con su prefijo (<see cref="DeEstePc"/>).</param>
    public static bool EstaBloqueada(IPAddress direccion, IEnumerable<RedLocal> redesDeEstePc)
    {
        var ip = direccion.IsIPv4MappedToIPv6 ? direccion.MapToIPv4() : direccion;
        var bytes = ip.GetAddressBytes();
        if (Rangos.Any(r => Dentro(bytes, r.Red, r.Prefijo))) return true;
        foreach (var red in redesDeEstePc)
        {
            var local = red.Direccion.IsIPv4MappedToIPv6 ? red.Direccion.MapToIPv4() : red.Direccion;
            var deLaRed = local.GetAddressBytes();
            var prefijo = red.Prefijo is > 0 && red.Prefijo <= deLaRed.Length * 8 ? red.Prefijo : deLaRed.Length * 8;
            if (Dentro(bytes, deLaRed, prefijo)) return true;
        }
        return false;
    }

    /// <summary>Las direcciones de las interfaces de red de este PC que están activas, con su prefijo.</summary>
    public static IReadOnlyList<RedLocal> DeEstePc()
    {
        try
        {
            return
            [
                .. NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus != OperationalStatus.Down)
                    .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                    .Where(u => u.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    .Select(u => new RedLocal(u.Address, u.PrefixLength))
            ];
        }
        catch (NetworkInformationException)
        {
            // Sin la lista de interfaces quedan los rangos fijos, que ya cubren loopback y las redes privadas.
            return [];
        }
    }

    private static (byte[] Red, int Prefijo) Rango(string cidr)
    {
        var partes = cidr.Split('/');
        return (IPAddress.Parse(partes[0]).GetAddressBytes(), int.Parse(partes[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool Dentro(byte[] direccion, byte[] red, int prefijo)
    {
        if (direccion.Length != red.Length) return false;
        var completos = prefijo / 8;
        for (var i = 0; i < completos; i++)
            if (direccion[i] != red[i]) return false;
        var resto = prefijo % 8;
        if (resto == 0) return true;
        var mascara = (byte)(0xFF << (8 - resto));
        return (direccion[completos] & mascara) == (red[completos] & mascara);
    }
}
