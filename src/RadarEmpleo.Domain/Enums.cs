namespace RadarEmpleo.Domain;

/// <summary>
/// Estado de una candidatura en el tablero. Los cinco primeros forman el camino normal
/// (en este orden); <see cref="Descartada"/> y <see cref="Cerrada"/> son estados finales
/// a los que se puede llegar desde cualquier otro. Las reglas viven en <see cref="ReglasEstado"/>.
/// </summary>
public enum EstadoCandidatura
{
    Detectada,
    Preparada,
    Enviada,
    Entrevista,
    Oferta,
    Descartada,
    Cerrada
}

/// <summary>Modalidad de trabajo que indica la oferta.</summary>
public enum Modalidad
{
    NoIndicada,
    Remoto,
    Hibrido,
    Presencial
}

/// <summary>Valoración de lo bien que encaja la oferta con el perfil.</summary>
public enum Encaje
{
    SinValorar,
    Alto,
    Medio,
    TiroLargo,
    No
}

/// <summary>Motivo por el que se descarta una candidatura (obligatorio al pasar a Descartada).</summary>
public enum MotivoDescarte
{
    Experiencia,
    Ingles,
    Titulacion,
    Zona,
    Modalidad,
    Perfil,
    Salario,
    Cerrada,
    DecisionPropia,
    Otro
}

/// <summary>De dónde viene un dato: alta manual, importación del registro, rastreo, móvil o agente.</summary>
public enum Origen
{
    Manual,
    Registro,
    Rastreo,
    Movil,
    Agente
}

/// <summary>Tipo de recordatorio de la agenda.</summary>
public enum TipoRecordatorio
{
    Seguimiento,
    Entrevista,
    PublicacionLinkedIn,
    Plazo,
    Otro
}
