import {
  EstadoCandidatura,
  MotivoDescarte,
} from '../../candidaturas/interfaces/candidatura.interface';

/** Respuesta de GET /api/estadisticas. Las definiciones de cada cifra están en EstadisticasDto (backend) */
export interface Estadisticas {
  /** Todas, también las descartadas y las cerradas */
  total: number;
  /** Los siete estados en el orden del tablero, también con 0 */
  porEstado: TotalPorEstado[];
  /** Las 8 últimas semanas (de lunes a domingo de Madrid), la actual la última */
  enviadasPorSemana: EnviadasSemana[];
  respuesta: Respuesta;
  /** Solo las descartadas ahora, de más a menos */
  motivosDescarte: TotalPorMotivo[];
}

export interface TotalPorEstado {
  estado: EstadoCandidatura;
  total: number;
}

export interface EnviadasSemana {
  /** Día «2026-11-09» */
  lunes: string;
  total: number;
}

export interface Respuesta {
  /** Candidaturas con fecha de envío */
  enviadas: number;
  /** Las que han pasado alguna vez por Entrevista u Oferta */
  conRespuesta: number;
  /** Redondeado; null si no hay enviadas */
  porcentaje: number | null;
  /** Media con un decimal; null si no hay casos */
  diasMedios: number | null;
  /** Sobre cuántas candidaturas se calcula diasMedios */
  casosDias: number;
}

export interface TotalPorMotivo {
  motivo: MotivoDescarte;
  total: number;
}
