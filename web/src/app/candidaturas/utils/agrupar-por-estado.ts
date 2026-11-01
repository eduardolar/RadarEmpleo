import { CandidaturaResumen, EstadoCandidatura } from '../interfaces/candidatura.interface';
import { ESTADOS_CANDIDATURA } from './transiciones-permitidas';

export interface ColumnaTablero {
  estado: EstadoCandidatura;
  candidaturas: CandidaturaResumen[];
}

/**
 * Reparte las candidaturas en una columna por estado, en el orden del tablero (también las
 * columnas vacías). Dentro de cada columna se respeta el orden en que llegan.
 */
export function agruparPorEstado(candidaturas: readonly CandidaturaResumen[]): ColumnaTablero[] {
  const columnas = new Map<EstadoCandidatura, CandidaturaResumen[]>(
    ESTADOS_CANDIDATURA.map((estado) => [estado, []]),
  );
  for (const candidatura of candidaturas) {
    columnas.get(candidatura.estado)?.push(candidatura);
  }
  return ESTADOS_CANDIDATURA.map((estado) => ({ estado, candidaturas: columnas.get(estado)! }));
}
