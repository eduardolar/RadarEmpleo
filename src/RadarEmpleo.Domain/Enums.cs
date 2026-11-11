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

/// <summary>Qué es un documento vinculado a una candidatura. Se deduce del nombre del archivo (<see cref="ReglasDocumento.TipoDesdeNombre"/>).</summary>
public enum TipoDocumento
{
    CV,
    Carta,
    GuiaEntrevista,
    Correo,
    Otro
}

/// <summary>
/// Cómo llegó un documento a la ficha. En la F5 siempre es <see cref="Manual"/>: <see cref="Registro"/>
/// queda para «Extraer registro» (F7) y <see cref="Preparacion"/> para «Preparar candidatura» (F8).
/// </summary>
public enum OrigenDocumento
{
    Registro,
    Manual,
    Preparacion
}

/// <summary>
/// De dónde sale un rastreo: una sección «## Rastreo …» del registro, un informe
/// <c>rastreos\rastreo_*.md</c> de la tarea de los lunes o el <c>.json</c> que generará la app (F7).
/// </summary>
public enum OrigenRastreo
{
    Registro,
    TareaLunes,
    App
}

/// <summary>Qué ha pasado con una oferta de un rastreo.</summary>
public enum EstadoOfertaRastreo
{
    Pendiente,
    Convertida,
    Ignorada
}
