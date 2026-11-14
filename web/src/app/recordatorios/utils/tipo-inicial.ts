import {
  EstadoCandidatura,
  TipoRecordatorio,
} from '../../candidaturas/interfaces/candidatura.interface';

/**
 * El tipo con el que empieza un recordatorio nuevo de una candidatura: en entrevista, lo normal es
 * apuntar cuándo es; con una oferta en la mano, hasta cuándo hay que contestar; si no, cuándo volver a
 * preguntar
 */
export function tipoInicialSegunEstado(estado: EstadoCandidatura): TipoRecordatorio {
  switch (estado) {
    case 'Entrevista':
      return 'Entrevista';
    case 'Oferta':
      return 'Plazo';
    default:
      return 'Seguimiento';
  }
}
