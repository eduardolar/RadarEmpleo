using System.Text.RegularExpressions;

namespace RadarEmpleo.Domain;

/// <summary>Sistemas de selección con un listado público de ofertas en JSON.</summary>
public enum SistemaSeleccion
{
    Greenhouse,
    Lever,
    Ashby,
    Workable
}

/// <summary>Una oferta de un sistema de selección: la cuenta de la empresa y el id de la oferta, tal como van en el enlace.</summary>
public sealed record OfertaEnSistema(SistemaSeleccion Sistema, string Cuenta, string Id)
{
    /// <summary>
    /// El listado público de la cuenta (decisión 4 de la F7b), siempre en el host fijo de cada sistema: lo que viene del
    /// enlace solo entra como un segmento ya validado.
    /// </summary>
    public string UrlListado => Sistema switch
    {
        SistemaSeleccion.Greenhouse => $"https://{ListadosPublicos.Greenhouse}/v1/boards/{Cuenta}/jobs",
        SistemaSeleccion.Lever => $"https://{ListadosPublicos.Lever}/v0/postings/{Cuenta}?mode=json",
        SistemaSeleccion.Ashby => $"https://{ListadosPublicos.Ashby}/posting-api/job-board/{Cuenta}",
        _ => $"https://{ListadosPublicos.Workable}/api/v1/widget/accounts/{Cuenta}"
    };
}

/// <summary>
/// Reconoce por el enlace si una oferta es de Greenhouse, Lever, Ashby o Workable (decisión 6 de la F7b: el detector va en
/// el servidor, junto a <see cref="Enlaces"/>). Puro.
/// </summary>
public static partial class SistemasSeleccion
{
    // Cuenta e id: letras, números, «-» y «_». Nada que pueda cambiar la URL del listado («/», «?», «%», «..»).
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex Segmento();

    [GeneratedRegex("^[0-9]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex Numero();

    // Lever y Ashby: el id de la oferta es un UUID.
    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex Uuid();

    /// <summary>
    /// La oferta del enlace, o null si no es de ninguno de los cuatro: <c>boards.greenhouse.io</c> o
    /// <c>job-boards.greenhouse.io/{cuenta}/jobs/{id}</c>, <c>jobs.lever.co/{cuenta}/{id}</c>,
    /// <c>jobs.ashbyhq.com/{cuenta}/{id}</c> (en estos dos, el id es un UUID) y <c>apply.workable.com/{cuenta}/j/{id}</c> (lo que siga, como <c>/apply</c>,
    /// no cuenta).
    /// </summary>
    public static OfertaEnSistema? Detectar(string? enlace)
    {
        var normalizado = Enlaces.Normalizar(enlace);
        if (string.IsNullOrEmpty(normalizado) || !Uri.TryCreate(normalizado, UriKind.Absolute, out var url)
            || !url.IsDefaultPort || url.UserInfo.Length > 0)
            return null;

        var partes = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var oferta = (url.Host.ToLowerInvariant(), partes) switch
        {
            ("boards.greenhouse.io" or "job-boards.greenhouse.io", [var cuenta, "jobs", var id, ..]) when Numero().IsMatch(id)
                => new OfertaEnSistema(SistemaSeleccion.Greenhouse, cuenta, id),
            ("jobs.lever.co", [var cuenta, var id, ..]) when Uuid().IsMatch(id) => new OfertaEnSistema(SistemaSeleccion.Lever, cuenta, id),
            ("jobs.ashbyhq.com", [var cuenta, var id, ..]) when Uuid().IsMatch(id) => new OfertaEnSistema(SistemaSeleccion.Ashby, cuenta, id),
            ("apply.workable.com", [var cuenta, "j", var id, ..]) => new OfertaEnSistema(SistemaSeleccion.Workable, cuenta, id),
            _ => null
        };
        return oferta is not null && Segmento().IsMatch(oferta.Cuenta) && Segmento().IsMatch(oferta.Id) ? oferta : null;
    }
}
