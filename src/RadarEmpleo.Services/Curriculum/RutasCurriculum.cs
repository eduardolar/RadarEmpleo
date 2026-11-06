namespace RadarEmpleo.Services.Curriculum;

/// <summary>
/// Rutas fijas dentro de la carpeta Curriculum de Eduardo: el registro (solo se lee) y el radar (lo
/// único que la app escribe ahí). Va aparte de las opciones de la API porque Services no referencia a Api.
/// </summary>
public sealed class RutasCurriculum
{
    /// <summary>Nombre del registro de candidaturas dentro de la carpeta.</summary>
    public const string ArchivoRegistro = "ofertas-enviadas.md";

    private static readonly string[] RutaRelativaRadar = ["radar", "candidaturas.md"];

    /// <param name="carpeta">Carpeta configurada. Admite variables de entorno; vacía significa sin carpeta.</param>
    public RutasCurriculum(string? carpeta)
    {
        Carpeta = string.IsNullOrWhiteSpace(carpeta)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(carpeta));
    }

    /// <summary>Carpeta configurada, o null si no hay ninguna.</summary>
    public string? Carpeta { get; }

    /// <summary>¿Está configurada y existe de verdad en el disco?</summary>
    public bool HayCarpeta => Carpeta is not null && Directory.Exists(Carpeta);

    /// <summary>Ruta del registro, o null si no hay carpeta configurada.</summary>
    public string? Registro => Carpeta is null ? null : Path.Combine(Carpeta, ArchivoRegistro);

    /// <summary>Ruta de <c>radar\candidaturas.md</c>, o null si no hay carpeta configurada.</summary>
    public string? Radar => Carpeta is null ? null : Path.Combine([Carpeta, .. RutaRelativaRadar]);
}
