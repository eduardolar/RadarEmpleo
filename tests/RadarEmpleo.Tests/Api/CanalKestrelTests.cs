using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RadarEmpleo.Api.Seguridad;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// La API con Kestrel de verdad (<c>WebApplicationFactory.UseKestrel</c>) y los endpoints «Local» y «Remoto» en dos
/// puertos libres (nunca 5180/5181 ni los de la demo): el canal se reconoce por el NOMBRE del endpoint, con el
/// clasificador real.
/// </summary>
public sealed class CanalKestrelTests : IDisposable
{
    private static readonly int[] Prohibidos = [4300, 5180, 5181, 5280, 5281];

    private readonly int _puertoLocal;
    private readonly int _puertoRemoto;
    private readonly FabricaApi _fabrica;

    public CanalKestrelTests()
    {
        _puertoLocal = PuertoLibre();
        // Dos puertos distintos aunque el sistema devuelva el mismo dos veces seguidas.
        do _puertoRemoto = PuertoLibre(); while (_puertoRemoto == _puertoLocal);
        _fabrica = new FabricaApi(canal: null, agentes: true, configuracion: new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Local:Url"] = $"http://localhost:{_puertoLocal}",
            ["Kestrel:Endpoints:Remoto:Url"] = $"http://localhost:{_puertoRemoto}"
        });
        _fabrica.UseKestrel();
        _fabrica.StartServer();
    }

    public void Dispose() => _fabrica.Dispose();

    private static int PuertoLibre()
    {
        while (true)
        {
            using var escucha = new TcpListener(IPAddress.Loopback, 0);
            escucha.Start();
            var puerto = ((IPEndPoint)escucha.LocalEndpoint).Port;
            if (!Prohibidos.Contains(puerto)) return puerto;
        }
    }

    private static HttpClient Cliente(int puerto) => new() { BaseAddress = new Uri($"http://localhost:{puerto}") };

    [Fact]
    public async Task PorElLocal_LosAgentesResponden()
    {
        using var http = Cliente(_puertoLocal);

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/agentes/estado")).StatusCode);
        var info = await http.GetFromJsonAsync<JsonElement>("/api/sistema/info");
        Assert.True(info.GetProperty("agentesDisponibles").GetBoolean());
        Assert.True(info.GetProperty("desdeElPc").GetBoolean());
        // Pasa el filtro: 404 porque la ejecución no existe.
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync("/api/agentes/ejecuciones/1/cancelar",
            new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task PorElRemoto_Da403AunqueSeaText()
    {
        using var http = Cliente(_puertoRemoto);

        var estado = await http.GetAsync("/api/agentes/estado");
        Assert.Equal(HttpStatusCode.Forbidden, estado.StatusCode);
        Assert.Equal(DisponibilidadAgentes.MensajeSoloPc,
            (await estado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/api/agentes/ejecuciones/1/cancelar",
            new StringContent("{}", Encoding.UTF8, "text/plain"))).StatusCode);
        var info = await http.GetFromJsonAsync<JsonElement>("/api/sistema/info");
        Assert.False(info.GetProperty("agentesDisponibles").GetBoolean());
        Assert.False(info.GetProperty("desdeElPc").GetBoolean());
        Assert.False(info.GetProperty("agentesHabilitados").GetBoolean());
    }

    /// <summary>
    /// Extraer el registro lanza un agente: por el remoto, 403 sin crear nada (tampoco con otro tipo de contenido); por
    /// el local, 202 con la ejecución. El ejecutor es el falso: no se lanza Claude.
    /// </summary>
    [Fact]
    public async Task ExtraerRegistro_PorElRemotoDa403SinCrearNada_YPorElLocalDa202()
    {
        using var local = Cliente(_puertoLocal);
        using var remoto = Cliente(_puertoRemoto);
        static StringContent Vacio(string tipo = "application/json") => new("{}", Encoding.UTF8, tipo);

        var desdeMovil = await remoto.PostAsync("/api/agentes/extraer-registro", Vacio());
        Assert.Equal(HttpStatusCode.Forbidden, desdeMovil.StatusCode);
        Assert.Equal(DisponibilidadAgentes.MensajeSoloPc,
            (await desdeMovil.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await remoto.PostAsync("/api/agentes/extraer-registro", Vacio("text/plain"))).StatusCode);
        Assert.Equal(0, (await local.GetFromJsonAsync<JsonElement>("/api/agentes/ejecuciones")).GetArrayLength());

        var desdePc = await local.PostAsync("/api/agentes/extraer-registro", Vacio());
        Assert.Equal(HttpStatusCode.Accepted, desdePc.StatusCode);
        var ejecucion = await desdePc.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ExtraerRegistro", ejecucion.GetProperty("tarea").GetString());
        Assert.EndsWith($"/api/agentes/ejecuciones/{ejecucion.GetProperty("id").GetInt32()}", desdePc.Headers.Location!.ToString());
        Assert.Equal(1, (await local.GetFromJsonAsync<JsonElement>("/api/agentes/ejecuciones")).GetArrayLength());
        // La que se ha creado por el local tampoco se ve ni se toca por el remoto.
        Assert.Equal(HttpStatusCode.Forbidden, (await remoto.GetAsync($"/api/agentes/ejecuciones/{ejecucion.GetProperty("id").GetInt32()}")).StatusCode);
    }

    [Fact]
    public async Task ElOrigen_SaleDelEndpoint()
    {
        using var local = Cliente(_puertoLocal);
        using var remoto = Cliente(_puertoRemoto);
        const string alta = """{"empresa":"Acme","puesto":"Desarrollador .NET","origen":"Agente"}""";

        var desdePc = await local.PostAsync("/api/candidaturas", new StringContent(alta, Encoding.UTF8, "application/json"));
        var desdeMovil = await remoto.PostAsync("/api/candidaturas", new StringContent(alta, Encoding.UTF8, "application/json"));

        Assert.Equal("Manual", (await desdePc.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("origen").GetString());
        Assert.Equal("Movil", (await desdeMovil.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("origen").GetString());
    }
}
