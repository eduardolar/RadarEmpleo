using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Ofertas;

/// <summary>Lo que sale de comprobar la oferta: si sigue abierta y la página descargada, que es la que va al prompt.</summary>
public sealed record OfertaComprobada(ComprobacionOferta Comprobacion, DescargaOferta Pagina);

/// <summary>
/// Comprueba si la oferta sigue publicada antes de lanzar AnalizarEncaje: descarga la página <b>una sola vez</b> (se
/// reutiliza para el prompt) y, si el enlace es de un sistema de selección, su listado público; decide
/// <see cref="ReglasOfertaAbierta"/>.
/// </summary>
public sealed class ComprobadorOfertaAbierta(IDescargadorOfertas descargador)
{
    public const string MensajeSinEnlace = "La candidatura no tiene un enlace a la oferta.";
    public const string MensajeEnlaceNoValido = "El enlace de la oferta no es una dirección válida.";

    /// <summary>Comprueba la oferta de <paramref name="enlace"/>. Solo lanza si se cancela.</summary>
    public async Task<OfertaComprobada> ComprobarAsync(string? enlace, CancellationToken cancelacion)
    {
        var normalizado = Enlaces.Normalizar(enlace);
        if (normalizado is "") return SinDescarga(MensajeSinEnlace);
        if (normalizado is null) return SinDescarga(MensajeEnlaceNoValido);

        // Un enlace «http://» se pide por https: la descarga no admite otra cosa.
        if (normalizado.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) normalizado = "https://" + normalizado[7..];
        if (!Uri.TryCreate(normalizado, UriKind.Absolute, out var url)) return SinDescarga(MensajeEnlaceNoValido);

        var pagina = await descargador.DescargarAsync(normalizado, TipoDescarga.Pagina, cancelacion);
        var sistema = SistemasSeleccion.Detectar(normalizado);
        var listado = sistema is null ? null : await descargador.DescargarAsync(sistema.UrlListado, TipoDescarga.ListadoJson, cancelacion);
        return new(ReglasOfertaAbierta.Decidir(url, sistema, pagina, listado), pagina);
    }

    private static OfertaComprobada SinDescarga(string motivo) =>
        new(new(EstadoOferta.NoSeSabe, motivo), DescargaOferta.Fallida(FalloDescarga.EnlaceNoValido));
}
