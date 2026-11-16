namespace RadarEmpleo.Domain;

/// <summary>
/// Los hosts fijos de los listados públicos de ofertas de los sistemas de selección (decisión 4 de la F7b): la descarga
/// solo admite JSON de estos, también tras cada redirección.
/// </summary>
public static class ListadosPublicos
{
    public const string Greenhouse = "boards-api.greenhouse.io";
    public const string Lever = "api.lever.co";
    public const string Ashby = "api.ashbyhq.com";
    public const string Workable = "apply.workable.com";

    private static readonly HashSet<string> Hosts = new([Greenhouse, Lever, Ashby, Workable], StringComparer.OrdinalIgnoreCase);

    /// <summary>Si <paramref name="url"/> es de uno de los hosts de los listados (el host exacto, no un subdominio).</summary>
    public static bool EsDeUnListado(Uri url) => url.IsAbsoluteUri && Hosts.Contains(url.IdnHost);
}
