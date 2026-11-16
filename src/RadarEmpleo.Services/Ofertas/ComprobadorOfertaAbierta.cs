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
    /// <summary>Comprueba la oferta de <paramref name="enlace"/>. Solo lanza si se cancela.</summary>
    public async Task<OfertaComprobada> ComprobarAsync(string? enlace, CancellationToken cancelacion)
    {
        if (Enlaces.Normalizar(enlace) is not { Length: > 0 } normalizado)
            return new(new(EstadoOferta.NoSeSabe, "La candidatura no tiene un enlace a la oferta."), DescargaOferta.Fallida(FalloDescarga.EnlaceNoValido));

        var pagina = await descargador.DescargarAsync(normalizado, TipoDescarga.Pagina, cancelacion);
        var sistema = SistemasSeleccion.Detectar(normalizado);
        var listado = sistema is null ? null : await descargador.DescargarAsync(sistema.UrlListado, TipoDescarga.ListadoJson, cancelacion);
        return new(ReglasOfertaAbierta.Decidir(sistema, pagina, listado), pagina);
    }
}
