using System.Net;
using System.Net.Sockets;
using RadarEmpleo.Services.Ofertas;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="ConectorSeguro"/>: solo conecta con direcciones públicas por el 443 y, si rechaza, no abre nada. Sin DNS ni
/// sockets de verdad: el resolvedor y la apertura son falsos.
/// </summary>
public class ConectorSeguroTests
{
    private readonly List<IPEndPoint> _abiertas = [];

    private ConectorSeguro Conector(ResolvedorDnsFalso resolvedor, params RedLocal[] redes) =>
        new(resolvedor, () => redes, (destino, _) =>
        {
            _abiertas.Add(destino);
            return ValueTask.FromResult<Stream>(new MemoryStream());
        });

    [Fact]
    public async Task Conectar_UnNombrePublico_AbreLaDireccionComprobadaPorEl443()
    {
        var resolvedor = new ResolvedorDnsFalso { ["empleo.ejemplo.es"] = ["81.40.10.20"] };

        await using var _ = await Conector(resolvedor).ConectarAsync(new DnsEndPoint("empleo.ejemplo.es", 443), CancellationToken.None);

        Assert.Equal([new IPEndPoint(IPAddress.Parse("81.40.10.20"), 443)], _abiertas);
        Assert.Equal(["empleo.ejemplo.es"], resolvedor.Consultas);
    }

    [Theory]
    [InlineData("81.40.10.20", "10.0.0.5")]
    [InlineData("2a00:1450:4003:80e::200e", "::1")]
    [InlineData("127.0.0.1")]
    [InlineData("::ffff:192.168.1.1")]
    public async Task Conectar_SiAlgunaDireccionEstaBloqueada_RechazaSinAbrir(params string[] direcciones)
    {
        var resolvedor = new ResolvedorDnsFalso { ["trampa.ejemplo.es"] = direcciones };

        await Assert.ThrowsAsync<DireccionNoPermitidaException>(() =>
            Conector(resolvedor).ConectarAsync(new DnsEndPoint("trampa.ejemplo.es", 443), CancellationToken.None).AsTask());

        Assert.Empty(_abiertas);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("169.254.169.254")]
    public async Task Conectar_UnaIpEscritaTalCual_SeCompruebaSinResolver(string host)
    {
        var resolvedor = new ResolvedorDnsFalso();

        await Assert.ThrowsAsync<DireccionNoPermitidaException>(() =>
            Conector(resolvedor).ConectarAsync(new DnsEndPoint(host, 443), CancellationToken.None).AsTask());

        Assert.Empty(resolvedor.Consultas);
        Assert.Empty(_abiertas);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(8443)]
    [InlineData(22)]
    public async Task Conectar_OtroPuerto_RechazaSinResolverNiAbrir(int puerto)
    {
        var resolvedor = new ResolvedorDnsFalso { ["empleo.ejemplo.es"] = ["81.40.10.20"] };

        await Assert.ThrowsAsync<DireccionNoPermitidaException>(() =>
            Conector(resolvedor).ConectarAsync(new DnsEndPoint("empleo.ejemplo.es", puerto), CancellationToken.None).AsTask());

        Assert.Empty(resolvedor.Consultas);
        Assert.Empty(_abiertas);
    }

    [Fact]
    public async Task Conectar_SinDireccionesOEnLaRedDeEstePc_Rechaza()
    {
        var resolvedor = new ResolvedorDnsFalso { ["vacio.ejemplo.es"] = [], ["vecino.ejemplo.es"] = ["81.40.10.200"] };
        var conector = Conector(resolvedor, new RedLocal(IPAddress.Parse("81.40.10.20"), 24));

        await Assert.ThrowsAsync<DireccionNoPermitidaException>(() => conector.ComprobarAsync("vacio.ejemplo.es", 443, CancellationToken.None));
        await Assert.ThrowsAsync<DireccionNoPermitidaException>(() => conector.ComprobarAsync("vecino.ejemplo.es", 443, CancellationToken.None));
    }

    [Fact]
    public async Task Conectar_SiLaPrimeraNoResponde_PruebaLaSiguiente()
    {
        var resolvedor = new ResolvedorDnsFalso { ["empleo.ejemplo.es"] = ["81.40.10.20", "81.40.10.21"] };
        var intentos = new List<IPAddress>();
        var conector = new ConectorSeguro(resolvedor, () => [], (destino, _) =>
        {
            intentos.Add(destino.Address);
            return intentos.Count == 1 ? throw new SocketException((int)SocketError.ConnectionRefused) : ValueTask.FromResult<Stream>(new MemoryStream());
        });

        await using var _ = await conector.ConectarAsync(new DnsEndPoint("empleo.ejemplo.es", 443), CancellationToken.None);

        Assert.Equal([IPAddress.Parse("81.40.10.20"), IPAddress.Parse("81.40.10.21")], intentos);
    }
}

/// <summary>DNS de mentira: cada nombre con las direcciones que diga el test; los demás, sin direcciones.</summary>
internal sealed class ResolvedorDnsFalso : IResolvedorDns
{
    private readonly Dictionary<string, string[]> _nombres = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Consultas { get; } = [];

    public string[] this[string host]
    {
        set => _nombres[host] = value;
    }

    public Task<IPAddress[]> ResolverAsync(string host, CancellationToken cancelacion)
    {
        Consultas.Add(host);
        return Task.FromResult(_nombres.TryGetValue(host, out var direcciones) ? direcciones.Select(IPAddress.Parse).ToArray() : []);
    }
}
