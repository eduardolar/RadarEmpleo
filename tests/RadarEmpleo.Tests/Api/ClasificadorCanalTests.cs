using System.Net;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using RadarEmpleo.Api.Seguridad;

namespace RadarEmpleo.Tests.Api;

/// <summary><see cref="ClasificadorCanalKestrel"/> sin Kestrel: la marca del endpoint, el plan B por puerto y el loopback.</summary>
public class ClasificadorCanalTests
{
    private static readonly ClasificadorCanalKestrel Clasificador = new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Local:Url"] = "http://localhost:6100",
            ["Kestrel:Endpoints:Remoto:Url"] = "http://localhost:6101"
        }).Build());

    private sealed class Elementos : IConnectionItemsFeature
    {
        public IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();
    }

    private static DefaultHttpContext Peticion(CanalPeticion? marca, string ip = "127.0.0.1", int puerto = 0)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        http.Connection.LocalPort = puerto;
        if (marca is { } canal)
        {
            var elementos = new Elementos();
            elementos.Items[ClasificadorCanalKestrel.ClaveCanal] = canal;
            http.Features.Set<IConnectionItemsFeature>(elementos);
        }
        return http;
    }

    [Theory]
    [InlineData("127.0.0.1", CanalPeticion.Local)]
    [InlineData("::1", CanalPeticion.Local)]
    [InlineData("192.168.1.20", CanalPeticion.Desconocido)]
    [InlineData("10.0.0.5", CanalPeticion.Desconocido)]
    public void Local_SoloDesdeLaPropiaMaquina(string ip, CanalPeticion esperado)
    {
        Assert.Equal(esperado, Clasificador.Canal(Peticion(CanalPeticion.Local, ip)));
    }

    [Fact]
    public void Remoto_SigueSiendoRemotoDesdeFuera()
    {
        Assert.Equal(CanalPeticion.Remoto, Clasificador.Canal(Peticion(CanalPeticion.Remoto, "192.168.1.20")));
    }

    [Fact]
    public void SinMarcaConLaCaracteristica_EsDesconocido()
    {
        var http = Peticion(null);
        http.Features.Set<IConnectionItemsFeature>(new Elementos());
        Assert.Equal(CanalPeticion.Desconocido, Clasificador.Canal(http));
    }

    [Theory]
    [InlineData(6100, "127.0.0.1", CanalPeticion.Local)]
    [InlineData(6100, "192.168.1.20", CanalPeticion.Desconocido)]
    [InlineData(6101, "127.0.0.1", CanalPeticion.Remoto)]
    [InlineData(5180, "127.0.0.1", CanalPeticion.Desconocido)]
    public void PlanB_PorElPuertoDeLaConfiguracion(int puerto, string ip, CanalPeticion esperado)
    {
        Assert.Equal(esperado, Clasificador.Canal(Peticion(null, ip, puerto)));
    }

    [Fact]
    public void ElOrigen_SoloManualPorElPc()
    {
        IClasificadorCanal clasificador = Clasificador;
        Assert.Equal(Domain.Origen.Manual, clasificador.Origen(Peticion(CanalPeticion.Local)));
        Assert.Equal(Domain.Origen.Movil, clasificador.Origen(Peticion(CanalPeticion.Local, "192.168.1.20")));
        Assert.Equal(Domain.Origen.Movil, clasificador.Origen(Peticion(CanalPeticion.Remoto)));
    }
}
