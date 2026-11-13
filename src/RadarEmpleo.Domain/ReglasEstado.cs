namespace RadarEmpleo.Domain;

/// <summary>
/// Reglas de transición entre estados de una candidatura (lógica pura, sin BD ni reloj):
/// <list type="bullet">
/// <item>Se puede avanzar a cualquier estado POSTERIOR del camino
/// Detectada → Preparada → Enviada → Entrevista → Oferta (saltando pasos si hace falta).</item>
/// <item>Se puede pasar a Descartada o a Cerrada desde cualquier otro estado.
/// Descartada exige un motivo.</item>
/// <item>«Reabrir»: de Descartada o Cerrada solo se puede volver a Detectada.</item>
/// <item>No se puede retroceder de ninguna otra forma ni «pasar» al mismo estado.</item>
/// </list>
/// </summary>
public static class ReglasEstado
{
    /// <summary>Días que pasan entre el envío y el recordatorio de seguimiento automático.</summary>
    public const int DiasSeguimiento = 7;

    /// <summary>Tiempo durante el que se puede deshacer un cambio de estado (<see cref="Candidatura.DeshacerUltimoCambio"/>).</summary>
    public static readonly TimeSpan VentanaDeshacer = TimeSpan.FromMinutes(10);

    /// <summary>Hora de Madrid a la que se programa el seguimiento (las 09:00 del día que toque).</summary>
    public static readonly TimeSpan HoraSeguimiento = TimeSpan.FromHours(9);

    /// <summary>
    /// Cuándo toca el seguimiento de una candidatura enviada en <paramref name="envioUtc"/>: a las 09:00
    /// (hora de Madrid) del día que cae <see cref="DiasSeguimiento"/> días después del envío, contando
    /// días de Madrid. Se devuelve en UTC (08:00 en invierno y 07:00 en verano).
    /// </summary>
    public static DateTime FechaSeguimiento(DateTime envioUtc)
    {
        var diaEnvioMadrid = Fechas.UtcAMadrid(envioUtc).Date;
        return Fechas.MadridAUtc(diaEnvioMadrid.AddDays(DiasSeguimiento) + HoraSeguimiento);
    }

    /// <summary>¿Es un estado final (Descartada o Cerrada)? Desde ellos solo se puede reabrir.</summary>
    public static bool EsFinal(EstadoCandidatura estado) =>
        estado is EstadoCandidatura.Descartada or EstadoCandidatura.Cerrada;

    /// <summary>¿Se permite pasar de <paramref name="de"/> a <paramref name="a"/>?</summary>
    public static bool PuedePasar(EstadoCandidatura de, EstadoCandidatura a)
    {
        if (de == a) return false;

        // Descartada y Cerrada: desde cualquiera.
        if (EsFinal(a)) return true;

        // Desde un estado final, solo «Reabrir» a Detectada.
        if (EsFinal(de)) return a == EstadoCandidatura.Detectada;

        // Dentro del camino normal, solo hacia delante (el orden del enum es el del camino).
        return a > de;
    }

    /// <summary>Estados a los que se puede mover una candidatura que está en <paramref name="de"/>
    /// (para el botón «Mover a…» del front).</summary>
    public static IReadOnlyList<EstadoCandidatura> EstadosPosibles(EstadoCandidatura de) =>
        Enum.GetValues<EstadoCandidatura>().Where(a => PuedePasar(de, a)).ToList();

    /// <summary>
    /// Comprueba la transición y lanza <see cref="ReglaNegocioException"/> con un mensaje en
    /// español si no está permitida o si falta el motivo de descarte. Si el estado o el motivo no son
    /// valores del enum (un número que se haya colado), lanza <see cref="ValidacionException"/>.
    /// </summary>
    public static void Validar(EstadoCandidatura de, EstadoCandidatura a, MotivoDescarte? motivo)
    {
        // Defensa: la API ya rechaza los enums numéricos, pero el dominio no se fía.
        if (!Enum.IsDefined(a))
            throw new ValidacionException($"El estado «{a}» no existe.", "estado");
        if (motivo is { } m && !Enum.IsDefined(m))
            throw new ValidacionException($"El motivo de descarte «{m}» no existe.", "motivoDescarte");

        if (de == a)
            throw new ReglaNegocioException($"La candidatura ya está en «{Texto(a)}».");

        if (!PuedePasar(de, a))
        {
            if (EsFinal(de))
                throw new ReglaNegocioException(
                    $"La candidatura está {Texto(de).ToLowerInvariant()}: solo se puede reabrir (volver a «Detectada»).");

            throw new ReglaNegocioException(
                $"No se puede volver de «{Texto(de)}» a «{Texto(a)}»: los estados solo avanzan.");
        }

        if (a == EstadoCandidatura.Descartada && motivo is null)
            throw new ReglaNegocioException("Para descartar una candidatura hay que indicar el motivo.");
    }

    /// <summary>Nombre del estado para los mensajes al usuario.</summary>
    public static string Texto(EstadoCandidatura estado) => estado.ToString();

    /// <summary>Motivo de descarte en español, como lo enseña el front.</summary>
    public static string Texto(MotivoDescarte motivo) => motivo switch
    {
        MotivoDescarte.Experiencia => "Experiencia",
        MotivoDescarte.Ingles => "Inglés",
        MotivoDescarte.Titulacion => "Titulación",
        MotivoDescarte.Zona => "Zona",
        MotivoDescarte.Modalidad => "Modalidad",
        MotivoDescarte.Perfil => "Perfil",
        MotivoDescarte.Salario => "Salario",
        MotivoDescarte.Cerrada => "Oferta cerrada",
        MotivoDescarte.DecisionPropia => "Decisión propia",
        MotivoDescarte.Otro => "Otro",
        _ => motivo.ToString()
    };
}
