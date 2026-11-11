using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Tests.Api;

/// <summary><c>GET /api/perfil</c> contra la API real con el perfil inventado de <see cref="FabricaApi"/>.</summary>
public sealed class PerfilApiTests : IDisposable
{
    private readonly FabricaApi _fabrica = new();
    private readonly HttpClient _http;

    public PerfilApiTests() => _http = _fabrica.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _fabrica.Dispose();
    }

    [Fact]
    public async Task Obtener_DevuelveElPerfilPartidoEnSecciones()
    {
        var perfil = await _http.GetFromJsonAsync<JsonElement>("/api/perfil");

        Assert.Equal("Perfil de búsqueda (ejemplo inventado)", perfil.GetProperty("titulo").GetString());
        Assert.True(perfil.GetProperty("fechaModificacion").GetDateTime() > DateTime.MinValue);
        Assert.StartsWith("Todo lo que hay aquí", perfil.GetProperty("introduccion").GetString());
        var secciones = perfil.GetProperty("secciones").EnumerateArray().ToList();
        Assert.Equal(3, secciones.Count);
        Assert.Equal("Filtros de búsqueda", secciones[0].GetProperty("titulo").GetString());
        Assert.Contains("Málaga o remoto", secciones[0].GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Obtener_NoCambiaElPerfil()
    {
        var ruta = Path.Combine(_fabrica.CarpetaCurriculum, RutasCurriculum.ArchivoPerfil);
        var antes = File.ReadAllBytes(ruta);

        await _http.GetAsync("/api/perfil");

        Assert.Equal(antes, File.ReadAllBytes(ruta));
    }

    [Fact]
    public async Task Obtener_SinElPerfil_Devuelve409SinLaRuta()
    {
        File.Delete(Path.Combine(_fabrica.CarpetaCurriculum, RutasCurriculum.ArchivoPerfil));

        var respuesta = await _http.GetAsync("/api/perfil");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var detalle = (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();
        Assert.Equal("No se encuentra perfil-busqueda.md en la carpeta Curriculum.", detalle);
        Assert.DoesNotContain(_fabrica.CarpetaCurriculum, detalle);
    }

    [Fact]
    public async Task Modificar_NoHayEndpoint()
    {
        var respuesta = await _http.PutAsJsonAsync("/api/perfil", new { titulo = "Otro" });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, respuesta.StatusCode);
    }
}
