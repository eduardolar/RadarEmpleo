import { EstadoCandidatura } from '../interfaces/candidatura.interface';

/** Orden de las columnas del tablero: los cinco primeros son el camino normal */
export const ESTADOS_CANDIDATURA: readonly EstadoCandidatura[] = [
  'Detectada',
  'Preparada',
  'Enviada',
  'Entrevista',
  'Oferta',
  'Descartada',
  'Cerrada',
];

export function esEstadoFinal(estado: EstadoCandidatura): boolean {
  return estado === 'Descartada' || estado === 'Cerrada';
}

/**
 * Copia de ReglasEstado.PuedePasar del backend, SOLO para decidir en el tablero dónde se puede
 * soltar una tarjeta y qué ofrece «Mover a…». La última palabra la tiene la API (responde 409).
 * - Hacia delante en el camino normal, saltando pasos si hace falta.
 * - A Descartada o Cerrada desde cualquier otro.
 * - Desde Descartada o Cerrada, solo «Reabrir» (volver a Detectada).
 */
export function puedePasar(de: EstadoCandidatura, a: EstadoCandidatura): boolean {
  if (de === a) {
    return false;
  }
  if (esEstadoFinal(a)) {
    return true;
  }
  if (esEstadoFinal(de)) {
    return a === 'Detectada';
  }
  return ESTADOS_CANDIDATURA.indexOf(a) > ESTADOS_CANDIDATURA.indexOf(de);
}

/** Estados a los que se puede mover una candidatura que está en `de`, en el orden del tablero */
export function estadosPosibles(de: EstadoCandidatura): EstadoCandidatura[] {
  return ESTADOS_CANDIDATURA.filter((a) => puedePasar(de, a));
}
