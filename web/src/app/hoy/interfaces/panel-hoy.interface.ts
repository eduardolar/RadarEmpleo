import {
  EstadoCandidatura,
  Recordatorio,
} from '../../candidaturas/interfaces/candidatura.interface';

// Tipos del contrato de GET /api/panel/hoy (RadarEmpleo.Services/Contratos/Recordatorios.cs)

/** Lo justo de una candidatura para enseñarla junto a su recordatorio */
export interface CandidaturaBreve {
  id: number;
  empresa: string;
  puesto: string;
  estado: EstadoCandidatura;
  /** La URL de la oferta, para abrirla desde la tarjeta; null si no la tiene */
  enlace: string | null;
}

/** Un recordatorio en Hoy, con su candidatura si la tiene */
export interface RecordatorioAgenda extends Recordatorio {
  candidatura: CandidaturaBreve | null;
  /**
   * Solo en «Hechos hoy»: si es un seguimiento que se dio por hecho solo al mover la candidatura (a
   * Entrevista u Oferta), el estado al que pasó; null si lo marcó el usuario
   */
  cerradoAlPasarA?: EstadoCandidatura | null;
}

/** Candidatura enviada que sigue sin respuesta y sin seguimiento pendiente */
export interface EnviadaSinRespuesta {
  id: number;
  empresa: string;
  puesto: string;
  fechaEnvio: string;
  diasDesdeEnvio: number;
}

/** Enviadas que han tenido respuesta (algún paso a Entrevista u Oferta, aunque luego se descartaran) */
export interface TasaRespuesta {
  /** Candidaturas con fecha de envío */
  enviadas: number;
  conRespuesta: number;
  /** Redondeado a entero; null si no hay ninguna enviada */
  porcentaje: number | null;
}

/** Un día de la semana del resumen («2026-11-13») con sus recordatorios pendientes */
export interface DiaSemana {
  dia: string;
  recordatorios: number;
}

/** Las cifras que acompañan a Hoy. La semana va de lunes a domingo en hora de Madrid */
export interface ResumenHoy {
  /** Las que no están descartadas ni cerradas */
  candidaturasActivas: number;
  /** Recordatorios de tipo Entrevista de esta semana que siguen pendientes (también los vencidos) */
  entrevistasSemana: number;
  respuesta: TasaRespuesta;
  /** Los siete días de esta semana, de lunes a domingo */
  semana: DiaSemana[];
  /** Día («2026-11-23») del primer pendiente posterior a esta semana; null si no hay ninguno */
  siguienteDia: string | null;
}

export interface PanelHoy {
  /** Día de Madrid según el servidor, como «2026-11-13»: aquí no se calcula */
  hoy: string;
  /** Pendientes de hoy y vencidos, por fecha */
  paraHoy: RecordatorioAgenda[];
  /** Pendientes de los 7 días siguientes */
  proximos: RecordatorioAgenda[];
  masAdelante: RecordatorioAgenda[];
  /** Marcados como hechos hoy, el más reciente primero */
  hechosHoy: RecordatorioAgenda[];
  /** Enviadas hace entre 7 y 30 días, de la más reciente a la más antigua */
  sinRespuesta: EnviadaSinRespuesta[];
  /** Cuántas enviadas llevan más de 30 días sin respuesta */
  sinRespuestaAntiguas: number;
  resumen: ResumenHoy;
}
