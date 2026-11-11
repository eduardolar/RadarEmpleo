namespace RadarEmpleo.Domain;

/// <summary>
/// Un informe de rastreo leído de la carpeta Curriculum: una sección «## Rastreo …» del registro o un
/// <c>rastreos\rastreo_*.md</c> de la tarea de los lunes. Guarda una copia del Markdown para enseñarlo
/// aunque el archivo cambie, y el hash de ese texto para saber si hay que volver a leerlo.
/// </summary>
public class Rastreo
{
    public const int LargoClave = 500;
    public const int LargoTitulo = 500;
    public const int LargoRutaInforme = 500;
    public const int LargoHash = 64;

    public int Id { get; set; }
    /// <summary>Identifica el informe entre escaneos: «registro:&lt;título normalizado&gt;» o «rastreos/rastreo_2026-11-09.md». Única.</summary>
    public string Clave { get; set; } = "";
    public OrigenRastreo Origen { get; set; }
    public string Titulo { get; set; } = "";
    /// <summary>Día del rastreo (de Madrid). Se fija al leerlo la primera vez y no cambia en los siguientes escaneos.</summary>
    public DateOnly Fecha { get; set; }
    /// <summary>Archivo del que sale, relativo a Curriculum y con «/» («ofertas-enviadas.md» o «rastreos/…»).</summary>
    public string RutaInforme { get; set; } = "";
    /// <summary>SHA-256 en hexadecimal de <see cref="TextoInforme"/>.</summary>
    public string Hash { get; set; } = "";
    /// <summary>Copia del Markdown del informe (sin el título).</summary>
    public string TextoInforme { get; set; } = "";
    /// <summary>Última vez que se leyó con cambios (UTC).</summary>
    public DateTime FechaEscaneo { get; set; }
    /// <summary>Ejecución de agente que lo generó (F7). Sin clave foránea hasta que exista esa tabla.</summary>
    public int? EjecucionAgenteId { get; set; }

    public ICollection<OfertaRastreo> Ofertas { get; set; } = new List<OfertaRastreo>();
}

/// <summary>
/// Una oferta de un rastreo. El enlace (normalizado con <see cref="Enlaces.Normalizar"/>) es único en
/// toda la tabla: la misma oferta en dos rastreos solo se guarda una vez. Su estado solo cambia por
/// <see cref="Convertir"/> e <see cref="Ignorar"/>; volver a escanear nunca lo toca.
/// </summary>
public class OfertaRastreo
{
    public const int LargoEmpresa = 200;
    public const int LargoPuesto = 300;
    public const int LargoEnlace = 2000;
    public const int LargoPega = 2000;

    public int Id { get; set; }
    public int RastreoId { get; set; }
    public Rastreo Rastreo { get; set; } = null!;
    /// <summary>Línea del archivo en la que estaba al leerla (empezando en 1).</summary>
    public int Linea { get; set; }
    /// <summary>La línea tal cual, con su Markdown.</summary>
    public string TextoOriginal { get; set; } = "";
    public string Empresa { get; set; } = "";
    public string Puesto { get; set; } = "";
    public string Enlace { get; set; } = "";
    public Encaje Encaje { get; set; } = Encaje.SinValorar;
    public string? Pega { get; set; }
    public EstadoOfertaRastreo Estado { get; set; } = EstadoOfertaRastreo.Pendiente;
    /// <summary>Candidatura del tablero a la que se pasó. Queda en null si se borra la candidatura.</summary>
    public int? CandidaturaId { get; set; }
    public Candidatura? Candidatura { get; set; }
    /// <summary>Cuándo entró en su estado actual (UTC).</summary>
    public DateTime FechaEstado { get; set; }

    /// <summary>
    /// Estado que se enseña: una oferta convertida cuya candidatura se ha borrado vuelve a estar pendiente
    /// (se puede pasar al tablero otra vez).
    /// </summary>
    public static EstadoOfertaRastreo EstadoVisible(EstadoOfertaRastreo estado, int? candidaturaId) =>
        estado == EstadoOfertaRastreo.Convertida && candidaturaId is null ? EstadoOfertaRastreo.Pendiente : estado;

    /// <summary>La deja enlazada a su candidatura del tablero. Vale desde Pendiente y desde Ignorada.</summary>
    public void Convertir(int candidaturaId, DateTime ahoraUtc)
    {
        Estado = EstadoOfertaRastreo.Convertida;
        CandidaturaId = candidaturaId;
        FechaEstado = ahoraUtc;
    }

    /// <summary>«No me interesa». Si ya estaba ignorada no cambia nada.</summary>
    /// <exception cref="ReglaNegocioException">Ya está en el tablero.</exception>
    public void Ignorar(DateTime ahoraUtc)
    {
        if (EstadoVisible(Estado, CandidaturaId) == EstadoOfertaRastreo.Convertida)
            throw new ReglaNegocioException("Esta oferta ya está en el tablero. Si ya no te interesa, descártala desde su ficha.");
        if (Estado == EstadoOfertaRastreo.Ignorada) return;

        Estado = EstadoOfertaRastreo.Ignorada;
        CandidaturaId = null;
        FechaEstado = ahoraUtc;
    }
}
