using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// Documentos por HTTP contra la API real, con la carpeta Curriculum temporal de <see cref="FabricaApi"/>
/// (lleva <c>Candidaturas\Semana 2026-11-02 (2 - 8 nov)\CV_Ejemplo_Persona_NET_EmpresaEjemplo.pdf</c>).
/// </summary>
public sealed class DocumentosApiTests : IDisposable
{
    private const string Semana = "Candidaturas/Semana 2026-11-02 (2 - 8 nov)";
    private const string CvEjemplo = Semana + "/CV_Ejemplo_Persona_NET_EmpresaEjemplo.pdf";

    private readonly FabricaApi _fabrica = new();
    private readonly HttpClient _http;

    public DocumentosApiTests() => _http = _fabrica.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _fabrica.Dispose();
    }

    private string Ruta(string relativa) => Path.Combine(_fabrica.CarpetaCurriculum, relativa);

    private async Task<int> CrearCandidaturaAsync(string empresa = "Empresa Ejemplo")
    {
        var respuesta = await _http.PostAsJsonAsync("/api/candidaturas",
            new GuardarCandidaturaRequest(empresa, "Desarrollador .NET", FechaDeteccion: new DateTime(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc)),
            FabricaApi.Json);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!.Id;
    }

    private Task<HttpResponseMessage> VincularAsync(int id, string ruta, TipoDocumento? tipo = null) =>
        _http.PostAsJsonAsync($"/api/candidaturas/{id}/documentos", new VincularDocumentoRequest(ruta, tipo), FabricaApi.Json);

    private async Task<DocumentoDto> VincularBienAsync(int id, string ruta)
    {
        var respuesta = await VincularAsync(id, ruta);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<DocumentoDto>(FabricaApi.Json))!;
    }

    private static async Task<JsonElement> ProblemaAsync(HttpResponseMessage respuesta, HttpStatusCode codigo)
    {
        Assert.Equal(codigo, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        return await respuesta.Content.ReadFromJsonAsync<JsonElement>();
    }

    private void CrearDocx(string relativa)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Ruta(relativa))!);
        File.WriteAllBytes(Ruta(relativa), [0x50, 0x4B, 0x03, 0x04, 1, 2, 3]);
    }

    [Fact]
    public async Task Sugeridos_CvDeEjemplo_LoDevuelveConElContrato()
    {
        var id = await CrearCandidaturaAsync();

        var sugeridos = await _http.GetFromJsonAsync<JsonElement>($"/api/documentos/sugeridos?candidaturaId={id}");

        var primero = Assert.Single(sugeridos.EnumerateArray());
        Assert.Equal(CvEjemplo, primero.GetProperty("rutaRelativa").GetString());
        Assert.Equal("CV_Ejemplo_Persona_NET_EmpresaEjemplo.pdf", primero.GetProperty("nombreArchivo").GetString());
        Assert.Equal(Semana, primero.GetProperty("carpeta").GetString());
        Assert.Equal("CV", primero.GetProperty("tipo").GetString());
        Assert.Equal("pdf", primero.GetProperty("extension").GetString());
        Assert.Equal("Empresa", primero.GetProperty("coincidencia").GetString());
        Assert.False(primero.GetProperty("esAntiguo").GetBoolean());
        Assert.False(primero.GetProperty("yaVinculado").GetBoolean());
        Assert.True(primero.TryGetProperty("fechaModificacion", out _));
    }

    [Fact]
    public async Task Sugeridos_SinCandidaturaId_Devuelve400()
    {
        var problema = await ProblemaAsync(await _http.GetAsync("/api/documentos/sugeridos"), HttpStatusCode.BadRequest);

        Assert.True(problema.GetProperty("errors").TryGetProperty("candidaturaId", out _));
    }

    [Fact]
    public async Task Sugeridos_CandidaturaQueNoExiste_Devuelve404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/documentos/sugeridos?candidaturaId=999")).StatusCode);
    }

    [Fact]
    public async Task VincularYVerLaFicha_ElDocumentoSaleEnElDetalle()
    {
        var id = await CrearCandidaturaAsync();

        var respuesta = await VincularAsync(id, CvEjemplo);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.EndsWith($"/api/candidaturas/{id}", respuesta.Headers.Location?.ToString());
        var documento = (await respuesta.Content.ReadFromJsonAsync<DocumentoDto>(FabricaApi.Json))!;
        Assert.Equal(TipoDocumento.CV, documento.Tipo);
        Assert.True(documento.Vigente);
        Assert.True(documento.Encontrado);

        var ficha = await _http.GetFromJsonAsync<JsonElement>($"/api/candidaturas/{id}");
        var enFicha = Assert.Single(ficha.GetProperty("documentos").EnumerateArray());
        Assert.Equal(documento.Id, enFicha.GetProperty("id").GetInt32());
        Assert.Equal("Manual", enFicha.GetProperty("origen").GetString());
        Assert.Equal(Semana, enFicha.GetProperty("carpeta").GetString());
    }

    [Theory]
    [InlineData(@"..\perfil-busqueda.md")]
    [InlineData("../perfil-busqueda.md")]
    [InlineData(@"Documentos personales\DNI.pdf")]
    [InlineData("Documentos personales/DNI.pdf")]
    [InlineData(@"C:\Users\Ana\Desktop\Curriculum\CV.pdf")]
    [InlineData(@"\\servidor\c\CV.pdf")]
    [InlineData("CV.pdf:secreto")]
    [InlineData("")]
    public async Task Vincular_RutaNoValida_Devuelve400EnRutaRelativaSinRutasAbsolutas(string ruta)
    {
        Directory.CreateDirectory(Ruta("Documentos personales"));
        File.WriteAllText(Ruta(@"Documentos personales\DNI.pdf"), "dni");
        var id = await CrearCandidaturaAsync();

        var respuesta = await VincularAsync(id, ruta);

        var texto = await respuesta.Content.ReadAsStringAsync();
        var problema = await ProblemaAsync(respuesta, HttpStatusCode.BadRequest);
        Assert.True(problema.GetProperty("errors").TryGetProperty("rutaRelativa", out _));
        Assert.DoesNotContain(_fabrica.CarpetaCurriculum.Replace(@"\", @"\\"), texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetTempPath().Replace(@"\", @"\\"), texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Vincular_CandidaturaQueNoExiste_Devuelve404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await VincularAsync(999, CvEjemplo)).StatusCode);
    }

    [Fact]
    public async Task Vincular_ArchivoQueNoEsta_Devuelve409()
    {
        var id = await CrearCandidaturaAsync();

        var problema = await ProblemaAsync(await VincularAsync(id, Semana + "/CV_No_Existe.pdf"), HttpStatusCode.Conflict);

        Assert.Equal("No encuentro el archivo. Puede que se haya movido.", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Vincular_YaVinculado_Devuelve409()
    {
        var id = await CrearCandidaturaAsync();
        await VincularBienAsync(id, CvEjemplo);

        await ProblemaAsync(await VincularAsync(id, CvEjemplo), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Vincular_TipoComoNumero_Devuelve400()
    {
        var id = await CrearCandidaturaAsync();

        var respuesta = await _http.PostAsync($"/api/candidaturas/{id}/documentos",
            new StringContent($$"""{"rutaRelativa":"{{CvEjemplo}}","tipo":3}""", System.Text.Encoding.UTF8, "application/json"));

        await ProblemaAsync(respuesta, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Archivo_Pdf_SaleInlineConLasCabecerasDeSeguridadYSinSandbox()
    {
        var id = await CrearCandidaturaAsync();
        var documento = await VincularBienAsync(id, CvEjemplo);

        var respuesta = await _http.GetAsync($"/api/documentos/{documento.Id}/archivo");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/pdf", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline; filename*=UTF-8''CV_Ejemplo_Persona_NET_EmpresaEjemplo.pdf",
            string.Join(",", respuesta.Content.Headers.GetValues("Content-Disposition")));
        Assert.Equal("nosniff", Cabecera(respuesta, "X-Content-Type-Options"));
        Assert.Equal("frame-ancestors 'self'", Cabecera(respuesta, "Content-Security-Policy"));
        Assert.DoesNotContain("sandbox", Cabecera(respuesta, "Content-Security-Policy"));
        Assert.Equal("SAMEORIGIN", Cabecera(respuesta, "X-Frame-Options"));
        Assert.Equal("same-origin", Cabecera(respuesta, "Cross-Origin-Resource-Policy"));
        Assert.Equal("no-store", Cabecera(respuesta, "Cache-Control"));
        Assert.Equal("bytes", Cabecera(respuesta, "Accept-Ranges"));
        Assert.Equal(await File.ReadAllBytesAsync(Ruta(CvEjemplo)), await respuesta.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Archivo_ConRango_Devuelve206ConEseTrozo()
    {
        var id = await CrearCandidaturaAsync();
        var documento = await VincularBienAsync(id, CvEjemplo);
        using var peticion = new HttpRequestMessage(HttpMethod.Get, $"/api/documentos/{documento.Id}/archivo");
        peticion.Headers.Range = new RangeHeaderValue(0, 7);

        var respuesta = await _http.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.PartialContent, respuesta.StatusCode);
        Assert.Equal("%PDF-1.4"u8.ToArray(), await respuesta.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Archivo_Docx_SaleComoAttachment()
    {
        const string docx = Semana + "/CV_Ejemplo_Persona_NET_EmpresaEjemplo_Word.docx";
        CrearDocx(docx);
        var id = await CrearCandidaturaAsync();
        var documento = await VincularBienAsync(id, docx);

        var respuesta = await _http.GetAsync($"/api/documentos/{documento.Id}/archivo");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", respuesta.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("CV_Ejemplo_Persona_NET_EmpresaEjemplo_Word.docx", respuesta.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", Cabecera(respuesta, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task Archivo_NombreConTildesYEspacios_VaCodificadoEnFilenameStar()
    {
        const string pdf = Semana + "/CV Pérez Ñandú.pdf";
        File.Copy(Ruta(CvEjemplo), Ruta(pdf));
        var id = await CrearCandidaturaAsync();
        var documento = await VincularBienAsync(id, pdf);

        var respuesta = await _http.GetAsync($"/api/documentos/{documento.Id}/archivo");

        Assert.Equal("CV Pérez Ñandú.pdf", respuesta.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Contains("filename*=UTF-8''CV%20P%C3%A9rez%20%C3%91and%C3%BA.pdf",
            string.Join(",", respuesta.Content.Headers.GetValues("Content-Disposition")));
    }

    [Fact]
    public async Task Archivo_QueNoExiste_Devuelve404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/documentos/999/archivo")).StatusCode);
    }

    [Fact]
    public async Task Archivo_BorradoDelDisco_Devuelve404Generico()
    {
        var id = await CrearCandidaturaAsync();
        var documento = await VincularBienAsync(id, CvEjemplo);
        File.Delete(Ruta(CvEjemplo));

        var respuesta = await _http.GetAsync($"/api/documentos/{documento.Id}/archivo");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.DoesNotContain("CV_Ejemplo", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Quitar_Devuelve204YElArchivoSigue()
    {
        var id = await CrearCandidaturaAsync();
        var documento = await VincularBienAsync(id, CvEjemplo);

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/documentos/{documento.Id}")).StatusCode);

        Assert.True(File.Exists(Ruta(CvEjemplo)));
        var ficha = await _http.GetFromJsonAsync<JsonElement>($"/api/candidaturas/{id}");
        Assert.Empty(ficha.GetProperty("documentos").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync($"/api/documentos/{documento.Id}")).StatusCode);
    }

    [Fact]
    public async Task Sugeridos_SinCarpeta_Devuelve409()
    {
        var id = await CrearCandidaturaAsync();
        Directory.Delete(_fabrica.CarpetaCurriculum, recursive: true);

        var problema = await ProblemaAsync(await _http.GetAsync($"/api/documentos/sugeridos?candidaturaId={id}"), HttpStatusCode.Conflict);

        Assert.Contains("carpeta Curriculum", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Ficha_ConCarpeta_HayCarpetaCurriculumEsTrue()
    {
        var id = await CrearCandidaturaAsync();

        var ficha = await _http.GetFromJsonAsync<JsonElement>($"/api/candidaturas/{id}");

        Assert.True(ficha.GetProperty("hayCarpetaCurriculum").GetBoolean());
    }

    [Fact]
    public async Task Sugeridos_LlevanPosteriorAlEnvio()
    {
        var id = await CrearCandidaturaAsync();

        var sugeridos = await _http.GetFromJsonAsync<JsonElement>($"/api/documentos/sugeridos?candidaturaId={id}");

        Assert.False(sugeridos[0].GetProperty("posteriorAlEnvio").GetBoolean());
    }

    [Fact]
    public async Task Vincular_DniEnLaRaiz_Devuelve400()
    {
        File.WriteAllText(Ruta("DNI.pdf"), "dni");
        var id = await CrearCandidaturaAsync();

        var problema = await ProblemaAsync(await VincularAsync(id, "DNI.pdf"), HttpStatusCode.BadRequest);

        Assert.True(problema.GetProperty("errors").TryGetProperty("rutaRelativa", out _));
    }

    [Fact]
    public async Task EnlaceDuro_AlVincularDa400YSiSeCambiaDespuesElArchivoDa404()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Ruta("Documentos personales"));
        var dni = Ruta(@"Documentos personales\DNI.pdf");
        File.WriteAllText(dni, "dni");
        var id = await CrearCandidaturaAsync();
        RutaSeguraTests.Mklink("/H", Ruta(Semana + "/CV_Falso.pdf"), dni);

        await ProblemaAsync(await VincularAsync(id, Semana + "/CV_Falso.pdf"), HttpStatusCode.BadRequest);

        // Ahora uno bueno, que después se cambia por otro nombre del DNI.
        var documento = await VincularBienAsync(id, CvEjemplo);
        File.Delete(Ruta(CvEjemplo));
        RutaSeguraTests.Mklink("/H", Ruta(CvEjemplo), dni);
        var respuesta = await _http.GetAsync($"/api/documentos/{documento.Id}/archivo");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.DoesNotContain("dni", await respuesta.Content.ReadAsStringAsync());
    }

    private static string Cabecera(HttpResponseMessage respuesta, string nombre) =>
        respuesta.Headers.TryGetValues(nombre, out var valores) || respuesta.Content.Headers.TryGetValues(nombre, out valores)
            ? string.Join(",", valores)
            : "";
}
