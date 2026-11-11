namespace RadarEmpleo.Domain;

/// <summary>
/// Empresa a la que se presenta una candidatura. <see cref="NombreNormalizado"/> es único y sirve
/// para no duplicar la misma empresa escrita de formas distintas («Inditec Sistemas, S.A.» e
/// «inditec sistemas»); ver <see cref="NombresEmpresa.Normalizar"/>.
/// </summary>
public class Empresa
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string NombreNormalizado { get; set; } = "";
    public string? Web { get; set; }
    public string? Notas { get; set; }
    public ICollection<Candidatura> Candidaturas { get; set; } = new List<Candidatura>();
}

/// <summary>
/// Una oferta a la que Eduardo se plantea presentarse o ya se ha presentado. Todas las fechas van en UTC.
/// El estado SOLO cambia a través de <see cref="CambiarEstado"/>, que aplica <see cref="ReglasEstado"/>,
/// apunta el cambio en <see cref="Historial"/> y programa el seguimiento al enviar.
/// </summary>
public class Candidatura
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public Empresa Empresa { get; set; } = null!;
    public string Puesto { get; set; } = "";
    public string? Enlace { get; set; }
    public string? Portal { get; set; }
    public Modalidad Modalidad { get; set; } = Modalidad.NoIndicada;
    public string? ModalidadDetalle { get; set; }
    public string? Ubicacion { get; set; }
    public Encaje Encaje { get; set; } = Encaje.SinValorar;
    /// <summary>Lo que falta o chirría respecto al perfil (la «pega» del análisis de encaje).</summary>
    public string? Pega { get; set; }
    public string? Analisis { get; set; }
    public string? Salario { get; set; }
    public EstadoCandidatura Estado { get; private set; } = EstadoCandidatura.Detectada;
    public DateTime FechaDeteccion { get; set; }
    public DateTime? FechaEnvio { get; private set; }
    /// <summary>
    /// Cuándo entró en su <see cref="Estado"/> actual: en el alta es <see cref="FechaDeteccion"/>, y en cada
    /// <see cref="CambiarEstado"/> pasa a ser ese momento. Editar los datos NO la toca. Es la base de los
    /// «días en el estado».
    /// </summary>
    public DateTime FechaEstado { get; private set; }
    /// <summary>Última modificación de cualquier tipo (datos o estado). Ordena el listado.</summary>
    public DateTime FechaUltimoCambio { get; set; }
    public MotivoDescarte? MotivoDescarte { get; private set; }
    public string? MotivoDescarteTexto { get; private set; }
    public Origen Origen { get; set; } = Origen.Manual;
    /// <summary>Clave de idempotencia de la importación (empresa + puesto normalizados + fecha). Única; null en altas manuales.</summary>
    public string? ClaveImportacion { get; set; }
    /// <summary>Título de la sección «## …» del registro de la que se importó (si viene de ahí).</summary>
    public string? SeccionOrigen { get; set; }

    public ICollection<CambioEstado> Historial { get; set; } = new List<CambioEstado>();
    public ICollection<Nota> Notas { get; set; } = new List<Nota>();
    public ICollection<Recordatorio> Recordatorios { get; set; } = new List<Recordatorio>();
    public ICollection<Documento> Documentos { get; set; } = new List<Documento>();

    /// <summary>
    /// Vincula un archivo de Curriculum a la ficha y lo deja como el vigente de su tipo: el que lo era
    /// deja de serlo. Necesita <see cref="Documentos"/> cargados.
    /// </summary>
    /// <exception cref="ReglaNegocioException">Esa misma ruta ya está vinculada (sin distinguir mayúsculas, como Windows).</exception>
    public Documento VincularDocumento(TipoDocumento tipo, string nombreArchivo, string rutaRelativa,
        OrigenDocumento origen, DateTime ahoraUtc, string? hashSha256 = null)
    {
        if (Documentos.Any(d => string.Equals(d.RutaRelativa, rutaRelativa, StringComparison.OrdinalIgnoreCase)))
            throw new ReglaNegocioException("Ese documento ya está en la ficha.");

        foreach (var anterior in Documentos.Where(d => d.Tipo == tipo && d.Vigente))
            anterior.Vigente = false;

        var documento = new Documento
        {
            Candidatura = this,
            CandidaturaId = Id,
            Tipo = tipo,
            NombreArchivo = nombreArchivo,
            RutaRelativa = rutaRelativa,
            Encontrado = true,
            Origen = origen,
            Vigente = true,
            HashSha256 = hashSha256,
            FechaAlta = ahoraUtc
        };
        Documentos.Add(documento);
        return documento;
    }

    /// <summary>
    /// Quita el documento de la ficha (el archivo no se toca). Si era el vigente, pasa a serlo el más
    /// reciente que quede de su tipo. Devuelve false si no es de esta candidatura. Necesita
    /// <see cref="Documentos"/> cargados.
    /// </summary>
    public bool QuitarDocumento(Documento documento)
    {
        if (!Documentos.Remove(documento)) return false;

        if (documento.Vigente)
        {
            var siguiente = Documentos.Where(d => d.Tipo == documento.Tipo)
                .OrderByDescending(d => d.FechaAlta).ThenByDescending(d => d.Id)
                .FirstOrDefault();
            if (siguiente is not null) siguiente.Vigente = true;
        }
        return true;
    }

    /// <summary>
    /// Crea una candidatura nueva en «Detectada» y apunta en el historial el alta
    /// (un <see cref="CambioEstado"/> sin estado de origen).
    /// </summary>
    public static Candidatura Nueva(Empresa empresa, string puesto, DateTime ahoraUtc, Origen origen, DateTime? fechaDeteccionUtc = null)
    {
        var fechaDeteccion = fechaDeteccionUtc ?? ahoraUtc;
        var candidatura = new Candidatura
        {
            Empresa = empresa,
            EmpresaId = empresa.Id,
            Puesto = puesto,
            Origen = origen,
            FechaDeteccion = fechaDeteccion,
            // Lleva en «Detectada» desde que se detectó, no desde el alta: una oferta importada con
            // fecha de hace días muestra los días reales en el estado.
            FechaEstado = fechaDeteccion,
            FechaUltimoCambio = ahoraUtc
        };
        candidatura.Historial.Add(new CambioEstado
        {
            De = null,
            A = EstadoCandidatura.Detectada,
            Fecha = ahoraUtc,
            Comentario = "Alta",
            Origen = origen
        });
        return candidatura;
    }

    /// <summary>¿Se puede mover desde el estado actual a <paramref name="estado"/>?</summary>
    public bool PuedePasarA(EstadoCandidatura estado) => ReglasEstado.PuedePasar(Estado, estado);

    /// <summary>
    /// Cambia el estado aplicando <see cref="ReglasEstado"/> (lanza <see cref="ReglaNegocioException"/>
    /// si no se puede) y deja constancia en <see cref="Historial"/>. Efectos añadidos:
    /// <list type="bullet">
    /// <item>A «Enviada»: <see cref="FechaEnvio"/> pasa a ser ahora (también al reenviar tras reabrir:
    /// es un envío nuevo) y se programa un recordatorio de seguimiento para las 09:00 de Madrid
    /// <see cref="ReglasEstado.DiasSeguimiento"/> días después (<see cref="ReglasEstado.FechaSeguimiento"/>),
    /// salvo que ya haya uno pendiente o <paramref name="programarSeguimiento"/> sea false (un envío
    /// importado de hace semanas no debe crear un seguimiento ya vencido).</item>
    /// <item>A «Entrevista» u «Oferta» sin fecha de envío (se saltó «Enviada»): se rellena
    /// <see cref="FechaEnvio"/> con ahora, pero sin seguimiento (ya hay respuesta).</item>
    /// <item>A «Descartada»: guarda el motivo. A cualquier otro estado, el motivo se borra.</item>
    /// <item>A «Descartada», «Cerrada» o al reabrir («Detectada»): los seguimientos pendientes se dan
    /// por hechos, porque ya no hay envío vivo al que hacer seguimiento.</item>
    /// </list>
    /// Necesita <see cref="Recordatorios"/> cargados para no duplicar ni dejar seguimientos colgados.
    /// </summary>
    /// <returns>El cambio apuntado en el historial.</returns>
    public CambioEstado CambiarEstado(
        EstadoCandidatura nuevo,
        DateTime ahoraUtc,
        Origen origen,
        MotivoDescarte? motivo = null,
        string? motivoTexto = null,
        string? comentario = null,
        bool programarSeguimiento = true)
    {
        ReglasEstado.Validar(Estado, nuevo, motivo);

        var cambio = new CambioEstado
        {
            De = Estado,
            A = nuevo,
            Fecha = ahoraUtc,
            Comentario = comentario,
            Origen = origen
        };
        Historial.Add(cambio);

        if (nuevo == EstadoCandidatura.Descartada)
        {
            MotivoDescarte = motivo;
            MotivoDescarteTexto = motivoTexto;
        }
        else
        {
            // El motivo solo tiene sentido mientras está descartada (reabrir, o pasar de Descartada a Cerrada).
            MotivoDescarte = null;
            MotivoDescarteTexto = null;
        }

        switch (nuevo)
        {
            case EstadoCandidatura.Enviada:
                FechaEnvio = ahoraUtc;
                if (programarSeguimiento && !SeguimientosPendientes().Any())
                {
                    Recordatorios.Add(new Recordatorio
                    {
                        Tipo = TipoRecordatorio.Seguimiento,
                        Titulo = TituloSeguimiento(),
                        FechaHora = ReglasEstado.FechaSeguimiento(ahoraUtc),
                        Hecho = false
                    });
                }
                break;

            case EstadoCandidatura.Entrevista or EstadoCandidatura.Oferta when FechaEnvio is null:
                FechaEnvio = ahoraUtc;
                break;

            case EstadoCandidatura.Descartada or EstadoCandidatura.Cerrada or EstadoCandidatura.Detectada:
                foreach (var seguimiento in SeguimientosPendientes().ToList())
                    seguimiento.Hecho = true;
                break;
        }

        Estado = nuevo;
        FechaEstado = ahoraUtc;
        FechaUltimoCambio = ahoraUtc;
        return cambio;
    }

    private IEnumerable<Recordatorio> SeguimientosPendientes() =>
        Recordatorios.Where(r => r.Tipo == TipoRecordatorio.Seguimiento && !r.Hecho);

    private string TituloSeguimiento()
    {
        // Empresa puede no estar cargada si alguien construye la candidatura a mano.
        var empresa = Empresa?.Nombre;
        return string.IsNullOrWhiteSpace(empresa)
            ? $"Seguimiento: {Puesto}"
            : $"Seguimiento: {Puesto} en {empresa}";
    }
}

/// <summary>Un apunte del historial de estados. <see cref="De"/> es null en el alta.</summary>
public class CambioEstado
{
    public int Id { get; set; }
    public int CandidaturaId { get; set; }
    public Candidatura Candidatura { get; set; } = null!;
    public EstadoCandidatura? De { get; set; }
    public EstadoCandidatura A { get; set; }
    public DateTime Fecha { get; set; }
    public string? Comentario { get; set; }
    public Origen Origen { get; set; } = Origen.Manual;
}

/// <summary>Nota con fecha sobre una candidatura.</summary>
public class Nota
{
    public int Id { get; set; }
    public int CandidaturaId { get; set; }
    public Candidatura Candidatura { get; set; } = null!;
    public string Texto { get; set; } = "";
    public DateTime Fecha { get; set; }
    public Origen Origen { get; set; } = Origen.Manual;
}

/// <summary>
/// Recordatorio de la agenda (seguimientos, entrevistas, plazos, publicaciones de LinkedIn…).
/// Puede ir ligado a una candidatura o ser suelto (<see cref="CandidaturaId"/> null).
/// </summary>
public class Recordatorio
{
    public int Id { get; set; }
    public int? CandidaturaId { get; set; }
    public Candidatura? Candidatura { get; set; }
    public TipoRecordatorio Tipo { get; set; }
    public string Titulo { get; set; } = "";
    public DateTime FechaHora { get; set; }
    public bool Hecho { get; set; }
    public string? Notas { get; set; }
}
