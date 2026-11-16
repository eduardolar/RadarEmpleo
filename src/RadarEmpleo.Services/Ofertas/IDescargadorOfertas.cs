namespace RadarEmpleo.Services.Ofertas;

/// <summary>Qué se descarga, que decide los tipos de contenido que se admiten.</summary>
public enum TipoDescarga
{
    /// <summary>La página de una oferta: <c>text/html</c> o <c>text/plain</c>, pasada a texto plano.</summary>
    Pagina,
    /// <summary>El listado público de un sistema de selección (decisión 4 de la F7b): solo <c>application/json</c>, tal cual.</summary>
    ListadoJson
}

/// <summary>Por qué no se ha podido descargar.</summary>
public enum FalloDescarga
{
    /// <summary>No es una dirección <c>https://</c> sin usuario ni contraseña.</summary>
    EnlaceNoValido,
    /// <summary>Va a la red local, a este PC o a otro puerto que no es el 443 (también tras una redirección).</summary>
    DireccionNoPermitida,
    /// <summary>La respuesta no es un 2xx (403, 404, 410…): el código va en <see cref="DescargaOferta.Codigo"/>.</summary>
    CodigoNoValido,
    DemasiadasRedirecciones,
    TiempoAgotado,
    /// <summary>Un listado que pasa del tope (una página, en cambio, se recorta).</summary>
    DemasiadoGrande,
    /// <summary>Un tipo de contenido o una compresión que no se admiten.</summary>
    TipoNoAdmitido,
    ErrorDeRed
}

/// <summary>El resultado de una descarga.</summary>
/// <param name="Fallo">Null si ha ido bien.</param>
/// <param name="Codigo">El código HTTP de la última respuesta (null si no llegó ninguna).</param>
/// <param name="UrlFinal">La dirección de la última petición, tras las redirecciones.</param>
/// <param name="Texto">Página: el texto plano. Listado: el JSON tal cual. Null si ha fallado.</param>
/// <param name="Recortada">La página pasaba del tope y solo se ha leído el principio.</param>
public sealed record DescargaOferta(FalloDescarga? Fallo, int? Codigo, Uri? UrlFinal, string? Texto, bool Recortada = false)
{
    public bool Ok => Fallo is null;

    public static DescargaOferta Fallida(FalloDescarga fallo, int? codigo = null, Uri? urlFinal = null) => new(fallo, codigo, urlFinal, null);
}

/// <summary>Descarga la oferta antes de lanzar AnalizarEncaje: el agente no abre webs (apartado 6 del plan).</summary>
public interface IDescargadorOfertas
{
    /// <summary>Descarga <paramref name="enlace"/>. Nunca lanza, salvo si se cancela <paramref name="cancelacion"/>.</summary>
    Task<DescargaOferta> DescargarAsync(string enlace, TipoDescarga tipo, CancellationToken cancelacion);
}
