namespace RadarEmpleo.Services.Curriculum;

/// <summary>
/// Rutas fijas dentro de la carpeta Curriculum de Eduardo: el registro y el perfil (solo se leen), las
/// carpetas de las que se pueden abrir documentos y el radar (lo único que la app escribe ahí). Va aparte
/// de las opciones de la API porque Services no referencia a Api.
/// </summary>
public sealed class RutasCurriculum
{
    /// <summary>Nombre del registro de candidaturas dentro de la carpeta.</summary>
    public const string ArchivoRegistro = "ofertas-enviadas.md";

    /// <summary>Lo que se le dice a Eduardo cuando no está la carpeta: sin rutas ni nombres de opciones.</summary>
    public const string MensajeSinCarpeta =
        "No encuentro tu carpeta Curriculum. Comprueba que sigue en su sitio (normalmente, en el Escritorio) y vuelve a intentarlo.";

    /// <summary>Nombre del perfil de búsqueda dentro de la carpeta.</summary>
    public const string ArchivoPerfil = "perfil-busqueda.md";

    /// <summary>Carpeta de los CV y cartas de cada semana («Candidaturas\Semana …»).</summary>
    public const string CarpetaCandidaturas = "Candidaturas";

    /// <summary>Carpeta de los CV antiguos.</summary>
    public const string CarpetaArchivo = "Archivo";

    /// <summary>
    /// Únicas subcarpetas de las que se pueden abrir documentos. Además valen los archivos sueltos de la
    /// raíz (solo el primer nivel). Nunca «Documentos personales» (DNI, certificados) ni «Marca personal».
    /// </summary>
    public static readonly IReadOnlyList<string> CarpetasPermitidas = [CarpetaCandidaturas, CarpetaArchivo];

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

    /// <summary>Carpeta <c>radar\</c>, la única en la que escribe la app; null si no hay carpeta configurada.</summary>
    public string? CarpetaRadar => Carpeta is null ? null : Path.Combine(Carpeta, RutaRelativaRadar[0]);

    /// <summary>Ruta del perfil de búsqueda, o null si no hay carpeta configurada.</summary>
    public string? Perfil => Carpeta is null ? null : Path.Combine(Carpeta, ArchivoPerfil);

    /// <summary>Carpeta <c>rastreos\</c> de la tarea de los lunes, o null si no hay carpeta configurada.</summary>
    public string? CarpetaRastreos => Carpeta is null ? null : Path.Combine(Carpeta, CarpetaInformesRastreo);

    /// <summary>Carpeta de los informes de la tarea de los lunes («rastreos\rastreo_AAAA-MM-DD.md»).</summary>
    public const string CarpetaInformesRastreo = "rastreos";
}
