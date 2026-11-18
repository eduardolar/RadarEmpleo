using System.Text.Json;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Ofertas;

/// <summary>Si la oferta sigue publicada y por qué se sabe (o por qué no), en una frase para la ficha.</summary>
/// <param name="TextoDeLaOferta">El texto de la página es de verdad la oferta (no un muro de inicio de sesión, una portada u
/// otra web): solo entonces va al prompt.</param>
public sealed record ComprobacionOferta(EstadoOferta Estado, string Motivo, bool TextoDeLaOferta = false);

/// <summary>
/// ¿Sigue abierta la oferta? (añadido de la F7b tras revisar AIHawk). Pura: decide con la página ya descargada y, si el
/// enlace es de un sistema de selección, con su listado público. Por orden: la página no existe (404/410) o Greenhouse ha
/// llevado al listado con <c>error=true</c>; el listado, si se ha podido leer, manda (si está el id, abierta; si no, cerrada,
/// salvo en Lever y Ashby si la página se lee bien: pueden ser ofertas ocultas y entonces no se sabe); una página
/// que pide iniciar sesión no dice nada; una página que dice que ya no se admiten candidaturas; y una página que se lee
/// sin decir nada de eso, abierta, salvo que haya acabado en otra web o en una portada.
/// </summary>
public static class ReglasOfertaAbierta
{
    private static readonly string[] FrasesCerrada =
    [
        .. new[]
        {
            "esta oferta ya no esta disponible", "la oferta ya no esta disponible", "esta oferta ya no esta activa",
            "esta oferta ha caducado", "la oferta ha caducado", "esta oferta ha expirado", "oferta cerrada",
            "ya no se aceptan candidaturas", "ya no acepta candidaturas", "ya no admite candidaturas",
            "esta vacante ya no esta disponible", "el proceso de seleccion ha finalizado",
            "no longer accepting applications", "this job is no longer available", "this job is no longer open",
            "this position is no longer available", "this position has been filled", "the job you are looking for is no longer",
            "this job has expired", "job posting has expired", "this posting is closed", "this job is closed"
        }.Select(TextoComparable.Normalizar)
    ];

    private static readonly string[] FrasesSesion =
        [.. new[] { "inicia sesion para ver", "sign in to view", "log in to view", "sign in to apply" }.Select(TextoComparable.Normalizar)];

    private static readonly string[] RutasSesion = ["/authwall", "/login", "/signin", "/sign-in", "/uas/login", "/checkpoint"];

    /// <param name="enlace">El enlace de la oferta, ya normalizado: si la página acaba en otra web, no se sabe.</param>
    /// <param name="sistema">El de <see cref="SistemasSeleccion.Detectar"/> (null si no es de ninguno).</param>
    /// <param name="listado">Su listado público (null si no hay sistema).</param>
    public static ComprobacionOferta Decidir(Uri enlace, OfertaEnSistema? sistema, DescargaOferta pagina, DescargaOferta? listado)
    {
        if (pagina.Codigo is 404 or 410)
            return new(EstadoOferta.Cerrada, $"La página de la oferta ya no existe ({pagina.Codigo}).");
        if (pagina.UrlFinal is { } final && EsDe(final, "greenhouse.io") && final.Query.Contains("error=true", StringComparison.OrdinalIgnoreCase))
            return new(EstadoOferta.Cerrada, "Greenhouse lleva al listado de la empresa: la oferta ya no está.");

        var texto = pagina.Ok ? TextoComparable.Normalizar(pagina.Texto) : "";
        var pideSesion = pagina.Ok && PideSesion(pagina.UrlFinal, texto);
        var diceCerrada = pagina.Ok && !pideSesion && FrasesCerrada.Any(texto.Contains);
        var esLaOferta = pagina.Ok && !pideSesion && !diceCerrada && !OtraPagina(enlace, pagina.UrlFinal);
        if (sistema is not null && listado is { Ok: true, Texto: { } json } && EstaEnElListado(sistema, json) is { } esta)
        {
            if (esta) return new(EstadoOferta.Abierta, $"Sigue en el listado público de {sistema.Sistema}.", esLaOferta);
            // Lever y Ashby tienen ofertas que no salen en el listado («unlisted») y se pueden abrir con el enlace. En
            // Greenhouse y Workable, no estar en un listado legible es estar cerrada.
            if (sistema.Sistema is SistemaSeleccion.Lever or SistemaSeleccion.Ashby && esLaOferta)
                return new(EstadoOferta.NoSeSabe, $"No sale en el listado público de {sistema.Sistema}, pero la página se lee.", true);
            return new(EstadoOferta.Cerrada, $"Ya no está en el listado público de {sistema.Sistema}.");
        }

        if (!pagina.Ok)
            return new(EstadoOferta.NoSeSabe, $"No he podido leer la oferta: {PorQue(pagina)}.");
        if (pideSesion)
            return new(EstadoOferta.NoSeSabe, "La página pide iniciar sesión: no sé si sigue abierta.");
        if (diceCerrada)
            return new(EstadoOferta.Cerrada, "La página dice que la oferta ya no admite candidaturas.");
        if (OtraPagina(enlace, pagina.UrlFinal))
            return new(EstadoOferta.NoSeSabe, "La oferta lleva a otra página: no sé si sigue abierta.");
        return new(EstadoOferta.Abierta, "La página de la oferta se lee y no dice que esté cerrada.", true);
    }

    /// <summary>Si la página acaba en otra web (otro host, sin contar «www.») o en una portada (ruta vacía o «/»).</summary>
    private static bool OtraPagina(Uri enlace, Uri? final)
    {
        if (final is null) return false;
        static string Host(Uri url) => url.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? url.IdnHost[4..] : url.IdnHost;
        return !string.Equals(Host(enlace), Host(final), StringComparison.OrdinalIgnoreCase) || final.AbsolutePath is "" or "/";
    }

    /// <summary>Si <paramref name="url"/> es de <paramref name="dominio"/> o de un subdominio suyo (no «otrogreenhouse.io»).</summary>
    private static bool EsDe(Uri url, string dominio) =>
        url.IdnHost.Equals(dominio, StringComparison.OrdinalIgnoreCase) || url.IdnHost.EndsWith("." + dominio, StringComparison.OrdinalIgnoreCase);

    /// <summary>Si el id está en el listado; null si el JSON no tiene la forma esperada.</summary>
    private static bool? EstaEnElListado(OfertaEnSistema sistema, string json)
    {
        try
        {
            using var documento = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var raiz = documento.RootElement;
            // Lever: un array. Greenhouse, Ashby y Workable: {"jobs": [...]}.
            var lista = raiz.ValueKind == JsonValueKind.Array ? raiz
                : raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty("jobs", out var jobs) && jobs.ValueKind == JsonValueKind.Array ? jobs
                : (JsonElement?)null;
            if (lista is null) return null;
            var campo = sistema.Sistema == SistemaSeleccion.Workable ? "shortcode" : "id";
            return lista.Value.EnumerateArray().Any(o => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(campo, out var id)
                && string.Equals(id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText(), sistema.Id, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool PideSesion(Uri? url, string texto) =>
        (url is not null && RutasSesion.Any(r => url.AbsolutePath.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
        || FrasesSesion.Any(texto.Contains);

    private static string PorQue(DescargaOferta pagina) => pagina.Fallo switch
    {
        FalloDescarga.CodigoNoValido => $"la web ha respondido {pagina.Codigo}",
        FalloDescarga.TiempoAgotado => "tardaba demasiado",
        FalloDescarga.DireccionNoPermitida => "la dirección no está permitida",
        FalloDescarga.EnlaceNoValido => "el enlace no es una dirección https válida",
        FalloDescarga.TipoNoAdmitido => "no es una página de texto",
        FalloDescarga.DemasiadasRedirecciones => "redirige demasiadas veces",
        _ => "no se ha podido conectar"
    };
}
