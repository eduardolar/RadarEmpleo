using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadarEmpleo.Services.Ofertas;

/// <summary>Una respuesta programada del descargador falso.</summary>
/// <param name="Fallo">Si se da, la descarga falla así (tiempo, dirección no permitida…) y no se mira lo demás.</param>
/// <param name="UrlFinal">Adónde habría llevado la última redirección (por defecto, el mismo enlace).</param>
public sealed record RespuestaDescargaFalsa(
    int Codigo = 200,
    string TipoContenido = "text/html",
    string Cuerpo = "",
    string? UrlFinal = null,
    FalloDescarga? Fallo = null);

/// <summary>
/// Descargador sin red, para los tests y para la demo con <c>Agentes:Ejecutor=Falso</c> (decisión 9 de la F7b): responde lo
/// programado para cada enlace, con las mismas reglas de tipo de contenido que el de verdad. Un enlace sin programar
/// falla como si no hubiera red.
/// </summary>
public sealed class DescargadorOfertasFalso : IDescargadorOfertas
{
    /// <summary>El archivo de respuestas, junto a las del ejecutor falso.</summary>
    public const string Archivo = "descargas.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly ConcurrentDictionary<string, RespuestaDescargaFalsa> _respuestas = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string Enlace, TipoDescarga Tipo)> _peticiones = new();

    /// <summary>Lo que le han pedido, en orden.</summary>
    public IReadOnlyCollection<(string Enlace, TipoDescarga Tipo)> Peticiones => _peticiones;

    public void Programar(string enlace, RespuestaDescargaFalsa respuesta) => _respuestas[enlace] = respuesta;

    /// <summary>
    /// Lee <c>&lt;carpeta&gt;\descargas.json</c>: un objeto con cada enlace y su <see cref="RespuestaDescargaFalsa"/>. Si
    /// falta o no se entiende, no se programa nada.
    /// </summary>
    public DescargadorOfertasFalso CargarDe(string carpeta)
    {
        var ruta = Path.Combine(carpeta, Archivo);
        if (!File.Exists(ruta)) return this;
        try
        {
            foreach (var (enlace, respuesta) in JsonSerializer.Deserialize<Dictionary<string, RespuestaDescargaFalsa>>(File.ReadAllText(ruta), Json) ?? [])
                Programar(enlace, respuesta);
        }
        catch (JsonException)
        {
            // Un archivo de ejemplo roto no impide arrancar: todas las descargas fallan como sin red.
        }
        return this;
    }

    public Task<DescargaOferta> DescargarAsync(string enlace, TipoDescarga tipo, CancellationToken cancelacion)
    {
        cancelacion.ThrowIfCancellationRequested();
        _peticiones.Enqueue((enlace, tipo));
        if (!_respuestas.TryGetValue(enlace.Trim(), out var respuesta)) return Task.FromResult(DescargaOferta.Fallida(FalloDescarga.ErrorDeRed));

        // Un urlFinal mal escrito en descargas.json no rompe nada: se queda sin URL final.
        var url = Uri.TryCreate(respuesta.UrlFinal ?? enlace.Trim(), UriKind.Absolute, out var absoluta) ? absoluta : null;
        var descarga = respuesta switch
        {
            { Fallo: { } fallo } => DescargaOferta.Fallida(fallo, null, url),
            _ when url is not null && !DescargadorOfertasHttp.HostAdmitido(tipo, url) => DescargaOferta.Fallida(FalloDescarga.DireccionNoPermitida, null, url),
            { Codigo: < 200 or > 299 } => DescargaOferta.Fallida(FalloDescarga.CodigoNoValido, respuesta.Codigo, url),
            _ when !DescargadorOfertasHttp.Admite(tipo, respuesta.TipoContenido) => DescargaOferta.Fallida(FalloDescarga.TipoNoAdmitido, respuesta.Codigo, url),
            _ => new DescargaOferta(null, respuesta.Codigo, url, DescargadorOfertasHttp.ATexto(tipo, respuesta.TipoContenido, respuesta.Cuerpo))
        };
        return Task.FromResult(descarga);
    }
}
