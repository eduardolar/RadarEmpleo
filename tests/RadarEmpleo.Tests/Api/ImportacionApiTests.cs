using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Importacion;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// Importación por HTTP contra la API real, con <c>registro-ejemplo.md</c> en la carpeta temporal de
/// <see cref="FabricaApi"/>. El JSON se reenvía tal cual, como hará la página.
/// </summary>
public sealed class ImportacionApiTests : IDisposable
{
    private const string UrlPrevisualizar = "/api/importacion/tabla/previsualizar";
    private const string UrlAplicar = "/api/importacion/lote/aplicar";

    private readonly FabricaApi _fabrica = new();
    private readonly HttpClient _http;

    public ImportacionApiTests() => _http = _fabrica.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _fabrica.Dispose();
    }

    private string RutaRegistro => Path.Combine(_fabrica.CarpetaCurriculum, RutasCurriculum.ArchivoRegistro);

    private string RutaRadar => Path.Combine(_fabrica.CarpetaCurriculum, "radar", "candidaturas.md");

    private Task<HttpResponseMessage> PrevisualizarAsync(HttpClient? http = null) =>
        (http ?? _http).PostAsync(UrlPrevisualizar, null);

    private async Task<JsonObject> VistaPreviaAsync()
    {
        var respuesta = await PrevisualizarAsync();
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return JsonNode.Parse(await respuesta.Content.ReadAsStringAsync())!.AsObject();
    }

    private static List<string> ClavesAccionables(JsonObject vista) => vista["filas"]!.AsArray()
        .Where(f => f!["resultado"]!.GetValue<string>() is "Nueva" or "AvanzaEstado")
        .Select(f => f!["clave"]!.GetValue<string>())
        .ToList();

    /// <summary>Lo que manda la página: el origen y el hash de la vista previa, y las claves.</summary>
    private static JsonObject Cuerpo(JsonObject vista, IEnumerable<string> claves) => new()
    {
        ["origen"] = vista["origen"]!.DeepClone(),
        ["hashRegistro"] = vista["hashRegistro"]!.DeepClone(),
        ["claves"] = new JsonArray(claves.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray())
    };

    private Task<HttpResponseMessage> AplicarAsync(JsonObject vista, IEnumerable<string> claves) => EnviarAsync(Cuerpo(vista, claves));

    private Task<HttpResponseMessage> EnviarAsync(JsonObject cuerpo) =>
        _http.PostAsync(UrlAplicar, new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json"));

    private async Task<int> ContarCandidaturasAsync() =>
        (await _http.GetFromJsonAsync<JsonElement>("/api/candidaturas")).GetArrayLength();

    private static async Task<ResultadoAplicacionDto> LeerResultadoAsync(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ResultadoAplicacionDto>(FabricaApi.Json))!;
    }

    private static async Task<JsonElement> LeerProblemaAsync(HttpResponseMessage respuesta, HttpStatusCode codigo)
    {
        Assert.Equal(codigo, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        return await respuesta.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Previsualizar_RegistroDeEjemplo_Devuelve200ConElContratoYNoEscribeNada()
    {
        var vista = await VistaPreviaAsync();

        Assert.Null(vista["lote"]);
        var resumen = vista["resumen"]!;
        Assert.Equal(8, resumen["nuevas"]!.GetValue<int>());
        Assert.Equal(0, resumen["avanzan"]!.GetValue<int>());
        Assert.Equal(0, resumen["yaExisten"]!.GetValue<int>());
        Assert.Equal(0, resumen["paraRevisar"]!.GetValue<int>());
        Assert.Equal(0, resumen["sinLeer"]!.GetValue<int>());
        Assert.Equal(3, resumen["enviosEnTexto"]!.GetValue<int>());
        Assert.Empty(vista["sinLeer"]!.AsArray());

        var fila = vista["filas"]![0]!;
        Assert.Equal("talleres ficticios|desarrollador .net|2026-08-03", fila["clave"]!.GetValue<string>());
        Assert.Equal("Nueva", fila["resultado"]!.GetValue<string>());
        Assert.Equal("2026-08-03", fila["fecha"]!.GetValue<string>());
        Assert.Equal("Enviada", fila["estadoRegistro"]!.GetValue<string>());
        Assert.Empty(fila["avisos"]!.AsArray());

        Assert.Equal([ReglasImportacion.AvisoFechaAproximada], vista["filas"]![2]!["avisos"]!.AsArray().Select(a => a!.GetValue<string>()));

        var candidaturas = await _http.GetFromJsonAsync<JsonElement>("/api/candidaturas");
        Assert.Equal(0, candidaturas.GetArrayLength());
    }

    [Fact]
    public async Task Previsualizar_RegistroDeEjemplo_ElHashEsElSha256DelArchivo()
    {
        var esperado = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(RutaRegistro)));

        var vista = await VistaPreviaAsync();

        Assert.Equal(esperado, vista["hashRegistro"]!.GetValue<string>());
        Assert.Equal("Tabla", vista["origen"]!.GetValue<string>());
        Assert.Null(vista["ejecucionId"]);
        Assert.Empty(vista["fuera"]!.AsArray());
        Assert.Empty(vista["dudas"]!.AsArray());
    }

    [Fact]
    public async Task Previsualizar_SinCarpeta_Devuelve409ConMensajeClaro()
    {
        Directory.Delete(_fabrica.CarpetaCurriculum, recursive: true);

        var problema = await LeerProblemaAsync(await PrevisualizarAsync(), HttpStatusCode.Conflict);

        Assert.Contains("carpeta Curriculum", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Previsualizar_EnDemoSinCarpeta_Devuelve409()
    {
        using var fabrica = new FabricaApi("Demo");
        using var http = fabrica.CreateClient();
        Directory.Delete(fabrica.CarpetaCurriculum, recursive: true);

        var problema = await LeerProblemaAsync(await PrevisualizarAsync(http), HttpStatusCode.Conflict);

        Assert.Contains("carpeta Curriculum", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Previsualizar_SinRegistro_Devuelve409ConMensajeClaro()
    {
        File.Delete(RutaRegistro);

        var problema = await LeerProblemaAsync(await PrevisualizarAsync(), HttpStatusCode.Conflict);

        Assert.Contains(RutasCurriculum.ArchivoRegistro, problema.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public async Task Previsualizar_RegistroConBom_DaLasMismasFilasYOtroHash(string codificacion)
    {
        var sinBom = await VistaPreviaAsync();
        var texto = await File.ReadAllTextAsync(RutaRegistro);
        var conBom = codificacion == "utf-8-bom" ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) : Encoding.GetEncoding(codificacion);
        await File.WriteAllTextAsync(RutaRegistro, texto, conBom);

        // El hash es el de los bytes (BOM incluido): cambia aunque el texto, y con él las filas, sea el mismo.
        var vista = await VistaPreviaAsync();
        foreach (var campo in new[] { "resumen", "filas", "sinLeer" })
            Assert.Equal(sinBom[campo]!.ToJsonString(), vista[campo]!.ToJsonString());
        Assert.NotEqual(sinBom["hashRegistro"]!.GetValue<string>(), vista["hashRegistro"]!.GetValue<string>());
    }

    [Fact]
    public async Task Previsualizar_RegistroEnWindows1252_Devuelve409QueDiceQueNoEsUtf8()
    {
        // «| 2026-09-01 | Señor | Uno | Enviada |» con la «ñ» de Windows-1252 (0xF1), tras una cabecera válida.
        await File.WriteAllBytesAsync(RutaRegistro,
            [.. "| Fecha | Empresa | Puesto | Estado |\n|---|---|---|---|\n| 2026-09-01 | Se"u8, 0xF1, .. "or | Uno | Enviada |\n"u8]);

        var problema = await LeerProblemaAsync(await PrevisualizarAsync(), HttpStatusCode.Conflict);

        var detalle = problema.GetProperty("detail").GetString();
        Assert.Contains("UTF-8", detalle);
        Assert.Contains(RutasCurriculum.ArchivoRegistro, detalle);
        Assert.DoesNotContain(_fabrica.CarpetaCurriculum, detalle);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF })]
    [InlineData(new byte[] { 0x20, 0x0D, 0x0A, 0x0A, 0x0D, 0x09 })]
    public async Task Previsualizar_RegistroVacioSoloBomOSoloBlancos_Devuelve409DeSinTabla(byte[] bytes)
    {
        await File.WriteAllBytesAsync(RutaRegistro, bytes);

        var problema = await LeerProblemaAsync(await PrevisualizarAsync(), HttpStatusCode.Conflict);

        Assert.Contains("tabla", problema.GetProperty("detail").GetString());
    }

    [FactSoloWindows("solo Windows impide abrir un archivo que otro tiene sin compartir")]
    public async Task Previsualizar_RegistroAbiertoEnExclusiva_Devuelve409DeReintentarYNo500()
    {
        await using var exclusivo = new FileStream(RutaRegistro, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var problema = await LeerProblemaAsync(await PrevisualizarAsync(), HttpStatusCode.Conflict);

        var detalle = problema.GetProperty("detail").GetString();
        Assert.Contains("Vuelve a intentarlo", detalle);
        Assert.DoesNotContain(_fabrica.CarpetaCurriculum, detalle);
    }

    [Fact]
    public async Task Previsualizar_RegistroAbiertoPorUnEditorQueLoComparte_Devuelve200()
    {
        await using var editor = new FileStream(RutaRegistro, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal(8, (await VistaPreviaAsync())["resumen"]!["nuevas"]!.GetValue<int>());
    }

    [Fact]
    public async Task Aplicar_DosVeces_LaPrimeraCreaYLaSegundaDaCero()
    {
        var vista = await VistaPreviaAsync();
        var claves = ClavesAccionables(vista);

        var primera = await LeerResultadoAsync(await AplicarAsync(vista, claves));
        var segunda = await LeerResultadoAsync(await AplicarAsync(vista, claves));

        Assert.Equal(8, primera.Creadas);
        Assert.Equal(0, segunda.Creadas);
        Assert.Equal(8, segunda.SinCambios);
        var nueva = await VistaPreviaAsync();
        Assert.Equal(0, nueva["resumen"]!["nuevas"]!.GetValue<int>());
        Assert.Equal(8, nueva["resumen"]!["yaExisten"]!.GetValue<int>());
    }

    [Fact]
    public async Task Aplicar_FechasReales_SeVenEnElListadoEnUtc()
    {
        var vista = await VistaPreviaAsync();

        await LeerResultadoAsync(await AplicarAsync(vista, ClavesAccionables(vista)));

        var lista = (await _http.GetFromJsonAsync<List<CandidaturaResumenDto>>("/api/candidaturas", FabricaApi.Json))!;
        Assert.Equal(8, lista.Count);
        var fila = Assert.Single(lista, c => c.Puesto == "Desarrollador .NET");
        // 3/8 en Madrid (verano, UTC+2) empieza el 2/8 a las 22:00 UTC.
        Assert.Equal(new DateTime(2026, 8, 2, 22, 0, 0, DateTimeKind.Utc), fila.FechaEnvio);
        Assert.Equal(Origen.Registro, fila.Origen);
        Assert.Equal(EstadoCandidatura.Enviada, fila.Estado);
    }

    /// <summary>
    /// La <see cref="ValidacionException"/> del servicio llega como 400 con su campo. La clave es la de una fila que no está
    /// en el registro: el cliente ya no manda filas, así que no hay forma de colar una.
    /// </summary>
    [Fact]
    public async Task Aplicar_ConLaClaveDeUnaFilaInventada_Devuelve400YNoCreaNada()
    {
        var vista = await VistaPreviaAsync();
        var inventada = ReglasImportacion.Clave("Inventada", "Puesto inventado", new DateOnly(2026, 9, 1));

        var respuesta = await AplicarAsync(vista, [.. ClavesAccionables(vista), inventada]);

        var problema = await LeerProblemaAsync(respuesta, HttpStatusCode.BadRequest);
        Assert.True(problema.GetProperty("errors").TryGetProperty("claves", out _));
        Assert.Equal(0, await ContarCandidaturasAsync());
    }

    [Fact]
    public async Task Aplicar_SinClaves_Devuelve400()
    {
        var cuerpo = Cuerpo(await VistaPreviaAsync(), []);
        cuerpo.Remove("claves");

        var problema = await LeerProblemaAsync(await EnviarAsync(cuerpo), HttpStatusCode.BadRequest);

        Assert.Equal("Faltan las claves.", problema.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(null, "Falta el hash del registro.")]
    [InlineData("abc", "El hash del registro tiene que tener 64 caracteres.")]
    public async Task Aplicar_SinHashOConHashCorto_Devuelve400EnEspanol(string? hash, string mensaje)
    {
        var vista = await VistaPreviaAsync();
        var cuerpo = Cuerpo(vista, ClavesAccionables(vista));
        cuerpo["hashRegistro"] = hash;

        var problema = await LeerProblemaAsync(await EnviarAsync(cuerpo), HttpStatusCode.BadRequest);

        Assert.Equal(mensaje, problema.GetProperty("detail").GetString());
        Assert.True(problema.GetProperty("errors").TryGetProperty("hashRegistro", out _));
        Assert.Equal(0, await ContarCandidaturasAsync());
    }

    [Fact]
    public async Task Aplicar_SinOrigen_Devuelve400()
    {
        var vista = await VistaPreviaAsync();
        var cuerpo = Cuerpo(vista, ClavesAccionables(vista));
        cuerpo.Remove("origen");

        var problema = await LeerProblemaAsync(await EnviarAsync(cuerpo), HttpStatusCode.BadRequest);

        Assert.Equal("Falta el origen.", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Aplicar_MasDeMilClaves_Devuelve400()
    {
        var vista = await VistaPreviaAsync();

        var respuesta = await AplicarAsync(vista, Enumerable.Range(0, 1001).Select(i => $"clave|{i}|2026-09-01"));

        var problema = await LeerProblemaAsync(respuesta, HttpStatusCode.BadRequest);
        Assert.Equal("No puede haber más de 1000 claves.", problema.GetProperty("detail").GetString());
        Assert.True(problema.GetProperty("errors").TryGetProperty("claves", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task Aplicar_OrigenNumerico_Devuelve400(int origen)
    {
        var vista = await VistaPreviaAsync();
        var cuerpo = Cuerpo(vista, ClavesAccionables(vista));
        cuerpo["origen"] = origen;

        var problema = await LeerProblemaAsync(await EnviarAsync(cuerpo), HttpStatusCode.BadRequest);

        Assert.Equal("El valor de «origen» no es válido.", problema.GetProperty("detail").GetString());
        Assert.True(problema.GetProperty("errors").TryGetProperty("origen", out _));
        Assert.Equal(0, await ContarCandidaturasAsync());
    }

    [Fact]
    public async Task Aplicar_TablaConEjecucionId_Devuelve400()
    {
        var vista = await VistaPreviaAsync();
        var cuerpo = Cuerpo(vista, ClavesAccionables(vista));
        cuerpo["ejecucionId"] = 1;

        var problema = await LeerProblemaAsync(await EnviarAsync(cuerpo), HttpStatusCode.BadRequest);

        Assert.True(problema.GetProperty("errors").TryGetProperty("ejecucionId", out _));
        Assert.Equal(0, await ContarCandidaturasAsync());
    }

    [Fact]
    public async Task Aplicar_ConUnHashDistinto_Da409ConSuMensajeYNoCreaNada()
    {
        var vista = await VistaPreviaAsync();
        var cuerpo = Cuerpo(vista, ClavesAccionables(vista));
        cuerpo["hashRegistro"] = new string('0', 64);

        var problema = await LeerProblemaAsync(await EnviarAsync(cuerpo), HttpStatusCode.Conflict);

        Assert.Equal(ImportacionService.MensajeRegistroCambiado, problema.GetProperty("detail").GetString());
        Assert.Equal(0, await ContarCandidaturasAsync());
        Assert.False(File.Exists(RutaRadar));
    }

    [Fact]
    public async Task Aplicar_RegistroEditadoTrasLaVistaPrevia_Da409YAlVolverALeerAplicaLoNuevo()
    {
        var vieja = await VistaPreviaAsync();
        var registro = await File.ReadAllTextAsync(RutaRegistro);
        await File.WriteAllTextAsync(RutaRegistro, registro.Replace("| Remoto | Preparada |", "| Remoto | Enviada |", StringComparison.Ordinal));

        var problema = await LeerProblemaAsync(await AplicarAsync(vieja, ClavesAccionables(vieja)), HttpStatusCode.Conflict);
        Assert.Equal(ImportacionService.MensajeRegistroCambiado, problema.GetProperty("detail").GetString());
        Assert.Equal(0, await ContarCandidaturasAsync());

        var nueva = await VistaPreviaAsync();
        Assert.Equal(8, (await LeerResultadoAsync(await AplicarAsync(nueva, ClavesAccionables(nueva)))).Creadas);
        var lista = (await _http.GetFromJsonAsync<List<CandidaturaResumenDto>>("/api/candidaturas", FabricaApi.Json))!;
        Assert.Equal(EstadoCandidatura.Enviada, Assert.Single(lista, c => c.Puesto == "Programador C#").Estado);
    }

    [Fact]
    public async Task Aplicar_ConUnLoteInventadoEnElCuerpo_LoIgnoraYUsaElDelRegistro()
    {
        var vista = await VistaPreviaAsync();
        var cuerpo = Cuerpo(vista, ClavesAccionables(vista));
        cuerpo["lote"] = new JsonObject
        {
            ["version"] = 1,
            ["origen"] = "Tabla",
            ["filas"] = new JsonArray(new JsonObject
            {
                ["linea"] = 1, ["fecha"] = "2026-09-01", ["fechaAproximada"] = false,
                ["empresa"] = "Inventada", ["puesto"] = "Puesto inventado", ["estado"] = "Enviada"
            })
        };

        var resultado = await LeerResultadoAsync(await EnviarAsync(cuerpo));

        Assert.Equal(8, resultado.Creadas);
        var lista = (await _http.GetFromJsonAsync<List<CandidaturaResumenDto>>("/api/candidaturas", FabricaApi.Json))!;
        Assert.Equal(8, lista.Count);
        Assert.DoesNotContain(lista, c => c.Puesto == "Puesto inventado");
    }

    [Fact]
    public async Task Aplicar_SinRegistro_Devuelve409()
    {
        var vista = await VistaPreviaAsync();
        File.Delete(RutaRegistro);

        var problema = await LeerProblemaAsync(await AplicarAsync(vista, ClavesAccionables(vista)), HttpStatusCode.Conflict);

        Assert.Contains(RutasCurriculum.ArchivoRegistro, problema.GetProperty("detail").GetString());
        Assert.Equal(0, await ContarCandidaturasAsync());
    }

    [Fact]
    public async Task Aplicar_DosALaVez_NoDuplicaNingunaYLaQueLlegaTardeDa200Con0O409()
    {
        var vista = await VistaPreviaAsync();
        var claves = ClavesAccionables(vista);

        var respuestas = await Task.WhenAll(AplicarAsync(vista, claves), AplicarAsync(vista, claves));

        Assert.All(respuestas, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Contains(respuestas, r => r.StatusCode == HttpStatusCode.OK);
        var candidaturas = await _http.GetFromJsonAsync<JsonElement>("/api/candidaturas");
        Assert.Equal(8, candidaturas.GetArrayLength());
    }

    [Fact]
    public async Task Aplicar_DosALaVezConCambioDeEstado_UnSoloCambioYUnSoloSeguimiento()
    {
        var inicial = await VistaPreviaAsync();
        await LeerResultadoAsync(await AplicarAsync(inicial, ClavesAccionables(inicial)));
        var registro = await File.ReadAllTextAsync(RutaRegistro);
        await File.WriteAllTextAsync(RutaRegistro, registro.Replace("| Remoto | Preparada |", "| Remoto | Enviada |", StringComparison.Ordinal));
        var vista = await VistaPreviaAsync();
        var claves = ClavesAccionables(vista);
        Assert.Single(claves);

        var respuestas = await Task.WhenAll(AplicarAsync(vista, claves), AplicarAsync(vista, claves));

        Assert.All(respuestas, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Contains(respuestas, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var conflicto in respuestas.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            var problema = await LeerProblemaAsync(conflicto, HttpStatusCode.Conflict);
            Assert.Contains(problema.GetProperty("detail").GetString(),
                new[] { ImportacionService.MensajeCambiadasEnOtraPestana, ImportacionService.MensajeYaImportadas });
        }
        var lista = (await _http.GetFromJsonAsync<List<CandidaturaResumenDto>>("/api/candidaturas", FabricaApi.Json))!;
        var id = Assert.Single(lista, c => c.Puesto == "Programador C#").Id;
        var ficha = (await _http.GetFromJsonAsync<CandidaturaDetalleDto>($"/api/candidaturas/{id}", FabricaApi.Json))!;
        Assert.Equal(EstadoCandidatura.Enviada, ficha.Estado);
        Assert.Single(ficha.Historial, h => h.A == EstadoCandidatura.Enviada);
        Assert.Single(ficha.Recordatorios);
    }

    [Fact]
    public async Task PrevisualizarYAplicar_NoCambianElRegistroYSoloApareceElRadar()
    {
        var antes = SHA256.HashData(await File.ReadAllBytesAsync(RutaRegistro));
        var archivosAntes = Directory.GetFiles(_fabrica.CarpetaCurriculum, "*", SearchOption.AllDirectories);
        var vista = await VistaPreviaAsync();
        Assert.False(File.Exists(RutaRadar)); // la vista previa no escribe nada

        var resultado = await LeerResultadoAsync(await AplicarAsync(vista, ClavesAccionables(vista)));

        Assert.True(resultado.RadarActualizado);
        Assert.Equal(antes, SHA256.HashData(await File.ReadAllBytesAsync(RutaRegistro)));
        Assert.Equal(archivosAntes.Append(RutaRadar).Order(StringComparer.Ordinal),
            Directory.GetFiles(_fabrica.CarpetaCurriculum, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Aplicar_ElRadarTieneUnaLineaPorCandidatura()
    {
        var vista = await VistaPreviaAsync();

        await LeerResultadoAsync(await AplicarAsync(vista, ClavesAccionables(vista)));

        var lineas = await File.ReadAllLinesAsync(RutaRadar);
        Assert.Contains(lineas, l => l.StartsWith("NO EDITAR: lo genera RadarEmpleo", StringComparison.Ordinal));
        Assert.Equal(8, lineas.Count(l => l.StartsWith("| 20", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Aplicar_SiElRadarNoSePuedeEscribir_Devuelve200ConRadarActualizadoFalse()
    {
        Directory.CreateDirectory(RutaRadar); // una carpeta en su sitio hace fallar la escritura
        var vista = await VistaPreviaAsync();

        var resultado = await LeerResultadoAsync(await AplicarAsync(vista, ClavesAccionables(vista)));

        Assert.Equal(8, resultado.Creadas);
        Assert.False(resultado.RadarActualizado);
    }

    [Fact]
    public async Task AltaYBorrado_PorLaApi_ActualizanElRadar()
    {
        var alta = await _http.PostAsJsonAsync("/api/candidaturas", new GuardarCandidaturaRequest("Acme", "Puesto del radar"), FabricaApi.Json);
        var creada = (await alta.Content.ReadFromJsonAsync<CandidaturaDetalleDto>(FabricaApi.Json))!;
        Assert.Contains("Puesto del radar", await File.ReadAllTextAsync(RutaRadar));

        await _http.DeleteAsync($"/api/candidaturas/{creada.Id}");

        Assert.DoesNotContain("Puesto del radar", await File.ReadAllTextAsync(RutaRadar));
    }
}
