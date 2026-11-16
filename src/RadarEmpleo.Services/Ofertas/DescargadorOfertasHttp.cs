using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using RadarEmpleo.Domain;
using System.Text;

namespace RadarEmpleo.Services.Ofertas;

/// <summary>Límites de una descarga: en total (con las redirecciones), tope de bytes ya descomprimidos y redirecciones.</summary>
public sealed record OpcionesDescarga(TimeSpan TiempoMaximo, int TopeBytes, int MaxRedirecciones)
{
    public static readonly OpcionesDescarga PorDefecto = new(TimeSpan.FromSeconds(15), 2 * 1024 * 1024, 3);
}

/// <summary>
/// La descarga de verdad (apartado 6 del plan), con un <see cref="HttpClient"/> propio: solo <c>https://</c> y el 443, sin
/// proxy, cookies ni credenciales, con un <c>User-Agent</c> genérico, HTTP/2 como mucho (HTTP/3 no pasa por el
/// <c>ConnectCallback</c>) y las redirecciones a mano, cada una comprobada otra vez. Cada conexión pasa por
/// <see cref="ConectorSeguro"/>. La compresión se quita aquí, para que el tope cuente los bytes ya descomprimidos.
/// </summary>
public sealed class DescargadorOfertasHttp : IDescargadorOfertas, IDisposable
{
    /// <summary>Genérico: no dice quién descarga.</summary>
    public const string AgenteUsuario = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

    private static readonly int[] Redirecciones = [301, 302, 303, 307, 308];
    private readonly HttpClient _cliente;
    private readonly ConectorSeguro _conector;
    private readonly OpcionesDescarga _opciones;

    /// <param name="manejador">Para los tests; por defecto, <see cref="CrearManejador"/>.</param>
    public DescargadorOfertasHttp(ConectorSeguro conector, OpcionesDescarga? opciones = null, HttpMessageHandler? manejador = null)
    {
        _conector = conector;
        _opciones = opciones ?? OpcionesDescarga.PorDefecto;
        _cliente = new HttpClient(manejador ?? CrearManejador(conector)) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>El manejador de las conexiones, con todo lo que no se admite apagado.</summary>
    public static SocketsHttpHandler CrearManejador(ConectorSeguro conector) => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        MaxResponseHeadersLength = 64,
        ConnectCallback = (contexto, cancelacion) => conector.ConectarAsync(contexto.DnsEndPoint, cancelacion)
    };

    public async Task<DescargaOferta> DescargarAsync(string enlace, TipoDescarga tipo, CancellationToken cancelacion)
    {
        if (!Uri.TryCreate(enlace.Trim(), UriKind.Absolute, out var url)) return DescargaOferta.Fallida(FalloDescarga.EnlaceNoValido);
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        limite.CancelAfter(_opciones.TiempoMaximo);
        try
        {
            return await DescargarConLimiteAsync(url, tipo, limite.Token);
        }
        catch (OperationCanceledException) when (!cancelacion.IsCancellationRequested)
        {
            return DescargaOferta.Fallida(FalloDescarga.TiempoAgotado);
        }
        catch (Exception error) when (error is DireccionNoPermitidaException || error.InnerException is DireccionNoPermitidaException)
        {
            return DescargaOferta.Fallida(FalloDescarga.DireccionNoPermitida);
        }
        // SocketException: el DNS de verdad no encuentra el nombre.
        catch (Exception error) when (error is HttpRequestException or IOException or SocketException or UriFormatException)
        {
            return DescargaOferta.Fallida(FalloDescarga.ErrorDeRed);
        }
    }

    private async Task<DescargaOferta> DescargarConLimiteAsync(Uri url, TipoDescarga tipo, CancellationToken cancelacion)
    {
        for (var saltos = 0; ; saltos++)
        {
            if (url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length > 0) return DescargaOferta.Fallida(FalloDescarga.EnlaceNoValido, urlFinal: url);
            if (!HostAdmitido(tipo, url)) return DescargaOferta.Fallida(FalloDescarga.DireccionNoPermitida, urlFinal: url);
            // Antes de pedir nada: con un manejador cualquiera (el de los tests) también se rechaza. El ConnectCallback lo
            // vuelve a comprobar con la dirección a la que conecta de verdad.
            await _conector.ComprobarAsync(url.IdnHost, url.Port, cancelacion);

            using var peticion = Peticion(url, tipo);
            using var respuesta = await _cliente.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, cancelacion);
            var codigo = (int)respuesta.StatusCode;
            if (Redirecciones.Contains(codigo))
            {
                if (respuesta.Headers.Location is not { } destino) return DescargaOferta.Fallida(FalloDescarga.CodigoNoValido, codigo, url);
                if (saltos >= _opciones.MaxRedirecciones) return DescargaOferta.Fallida(FalloDescarga.DemasiadasRedirecciones, codigo, url);
                url = destino.IsAbsoluteUri ? destino : new Uri(url, destino);
                continue;
            }
            if (!respuesta.IsSuccessStatusCode) return DescargaOferta.Fallida(FalloDescarga.CodigoNoValido, codigo, url);
            return await LeerAsync(respuesta.Content, codigo, url, tipo, cancelacion);
        }
    }

    private static HttpRequestMessage Peticion(Uri url, TipoDescarga tipo)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        peticion.Headers.UserAgent.ParseAdd(AgenteUsuario);
        peticion.Headers.Accept.ParseAdd(tipo == TipoDescarga.ListadoJson ? "application/json" : "text/html, text/plain;q=0.9");
        peticion.Headers.AcceptLanguage.ParseAdd("es-ES, es;q=0.9, en;q=0.8");
        peticion.Headers.AcceptEncoding.ParseAdd("gzip, deflate, br");
        return peticion;
    }

    private async Task<DescargaOferta> LeerAsync(HttpContent contenido, int codigo, Uri url, TipoDescarga tipo, CancellationToken cancelacion)
    {
        var tipoContenido = contenido.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if (!Admite(tipo, tipoContenido)) return DescargaOferta.Fallida(FalloDescarga.TipoNoAdmitido, codigo, url);

        await using var comprimido = await contenido.ReadAsStreamAsync(cancelacion);
        await using var cuerpo = Descomprimir(comprimido, contenido.Headers.ContentEncoding);
        if (cuerpo is null) return DescargaOferta.Fallida(FalloDescarga.TipoNoAdmitido, codigo, url);
        byte[] bytes;
        bool recortada;
        try
        {
            (bytes, recortada) = await LeerHastaElTopeAsync(cuerpo, cancelacion);
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException)
        {
            // Lo que dice ser gzip, deflate o br y no lo es.
            return DescargaOferta.Fallida(FalloDescarga.ErrorDeRed, codigo, url);
        }
        if (recortada && tipo == TipoDescarga.ListadoJson) return DescargaOferta.Fallida(FalloDescarga.DemasiadoGrande, codigo, url);

        var texto = Codificacion(contenido.Headers.ContentType?.CharSet).GetString(bytes).TrimStart('﻿');
        return new DescargaOferta(null, codigo, url, ATexto(tipo, tipoContenido, texto), recortada);
    }

    /// <summary>Si se admite ese tipo de contenido: JSON solo para los listados (decisión 4 de la F7b).</summary>
    internal static bool Admite(TipoDescarga tipo, string? tipoContenido) =>
        tipo == TipoDescarga.ListadoJson ? tipoContenido == "application/json" : tipoContenido is "text/html" or "text/plain";

    /// <summary>Un listado, solo de los hosts fijos de <see cref="ListadosPublicos"/>; una página, de cualquiera.</summary>
    internal static bool HostAdmitido(TipoDescarga tipo, Uri url) => tipo != TipoDescarga.ListadoJson || ListadosPublicos.EsDeUnListado(url);

    /// <summary>Una página, a texto plano; un listado, tal cual.</summary>
    internal static string ATexto(TipoDescarga tipo, string? tipoContenido, string cuerpo) =>
        tipo == TipoDescarga.ListadoJson ? cuerpo
        : tipoContenido == "text/html" ? HtmlATexto.Convertir(cuerpo) : HtmlATexto.NormalizarEspacios(cuerpo);

    /// <summary>Quita la compresión que diga el servidor; null si es una que no se admite (o más de una).</summary>
    private static Stream? Descomprimir(Stream cuerpo, ICollection<string> codificaciones)
    {
        var lista = codificaciones.Where(c => !c.Equals("identity", StringComparison.OrdinalIgnoreCase)).ToList();
        if (lista.Count == 0) return cuerpo;
        if (lista.Count > 1) return null;
        return lista[0].ToLowerInvariant() switch
        {
            "gzip" or "x-gzip" => new GZipStream(cuerpo, CompressionMode.Decompress),
            "deflate" => new ZLibStream(cuerpo, CompressionMode.Decompress),
            "br" => new BrotliStream(cuerpo, CompressionMode.Decompress),
            _ => null
        };
    }

    /// <summary>Lee como mucho el tope, ya descomprimido; si hay más, lo dice y no sigue leyendo.</summary>
    private async Task<(byte[] Bytes, bool Recortada)> LeerHastaElTopeAsync(Stream cuerpo, CancellationToken cancelacion)
    {
        var tope = _opciones.TopeBytes;
        var bufer = new byte[tope + 1];
        var leidos = 0;
        while (leidos < bufer.Length)
        {
            var n = await cuerpo.ReadAsync(bufer.AsMemory(leidos), cancelacion);
            if (n == 0) break;
            leidos += n;
        }
        return leidos > tope ? (bufer[..tope], true) : (bufer[..leidos], false);
    }

    private static Encoding Codificacion(string? juego)
    {
        var nombre = juego?.Trim().Trim('"');
        if (string.IsNullOrEmpty(nombre)) return Encoding.UTF8;
        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(nombre) ?? Encoding.GetEncoding(nombre);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }

    public void Dispose() => _cliente.Dispose();
}
