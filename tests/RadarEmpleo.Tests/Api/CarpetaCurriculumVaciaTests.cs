using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// La API arrancada sin carpeta Curriculum: con la opción vacía y con una ruta que no existe. Todo lo que
/// la necesita da 409, lo demás sigue funcionando y nunca se usa la ruta de appsettings.Local.json.
/// </summary>
public sealed class CarpetaCurriculumVaciaTests
{
    private const string Previsualizar = "/api/importacion/tabla/previsualizar";

    private static string RutaQueNoExiste() =>
        Path.Combine(Path.GetTempPath(), $"radar-tests-no-existe-{Guid.NewGuid():N}", "Curriculum");

    private static async Task<JsonElement> ConflictoAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var problema = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        // Sin jerga: nada de «opción CarpetaCurriculum» ni de «configuración».
        Assert.Equal(RutasCurriculum.MensajeSinCarpeta, problema.GetProperty("detail").GetString());
        Assert.DoesNotContain("configuración", RutasCurriculum.MensajeSinCarpeta);
        return problema;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SinCarpeta_LoQueLaNecesitaDa409YLoDemasFunciona(bool vacia)
    {
        var carpeta = vacia ? "" : RutaQueNoExiste();
        using var fabrica = new FabricaApi(carpetaCurriculum: carpeta);
        using var http = fabrica.CreateClient();

        await ConflictoAsync(await http.PostAsync(Previsualizar, null));

        var creada = await http.PostAsJsonAsync("/api/candidaturas", new GuardarCandidaturaRequest("Empresa Ejemplo", "Desarrollador .NET"), FabricaApi.Json);
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var id = (await creada.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!.Id;

        await ConflictoAsync(await http.GetAsync($"/api/documentos/sugeridos?candidaturaId={id}"));
        await ConflictoAsync(await http.PostAsJsonAsync($"/api/candidaturas/{id}/documentos",
            new VincularDocumentoRequest("Candidaturas/Semana 2026-11-02 (2 - 8 nov)/CV.pdf"), FabricaApi.Json));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/documentos/1/archivo")).StatusCode);

        await ConflictoAsync(await http.GetAsync("/api/perfil"));
        await ConflictoAsync(await http.PostAsJsonAsync("/api/rastreos/escanear", new { }));
        Assert.Empty((await http.GetFromJsonAsync<JsonElement>("/api/rastreos")).EnumerateArray());

        var ficha = await http.GetFromJsonAsync<JsonElement>($"/api/candidaturas/{id}");
        Assert.Empty(ficha.GetProperty("documentos").EnumerateArray());
        Assert.False(ficha.GetProperty("hayCarpetaCurriculum").GetBoolean());

        if (!vacia) Assert.False(Directory.Exists(Path.GetDirectoryName(carpeta)), "el radar no debe crear la carpeta");
    }

    [Fact]
    public void OpcionVacia_LaCarpetaInyectadaEsNull()
    {
        using var fabrica = new FabricaApi(carpetaCurriculum: "");

        var rutas = fabrica.Services.GetRequiredService<RutasCurriculum>();

        Assert.Null(rutas.Carpeta);
        Assert.False(rutas.HayCarpeta);
        Assert.Null(rutas.Radar);
    }

    [Fact]
    public void RutaQueNoExiste_LaCarpetaInyectadaEsEsaYNoLaDelLocal()
    {
        var carpeta = RutaQueNoExiste();
        using var fabrica = new FabricaApi(carpetaCurriculum: carpeta);

        var rutas = fabrica.Services.GetRequiredService<RutasCurriculum>();

        Assert.Equal(carpeta, rutas.Carpeta);
        Assert.False(rutas.HayCarpeta);
    }

    [Fact]
    public async Task SinCarpeta_ArrancaYLimpiarTemporalesNoHaceNada()
    {
        using var fabrica = new FabricaApi(carpetaCurriculum: "");
        using var http = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
        Assert.Equal(0, fabrica.Services.GetRequiredService<IExportadorRadar>().LimpiarTemporalesHuerfanos());
    }
}
