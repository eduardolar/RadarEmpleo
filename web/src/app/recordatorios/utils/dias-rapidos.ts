import { sumarDias } from '../../shared/utils/fechas-madrid';

export interface DiaRapido {
  texto: string;
  /** El día que pone, como «2026-11-14» */
  dia: string;
}

const SALTOS: readonly { texto: string; dias: number }[] = [
  { texto: 'Hoy', dias: 0 },
  { texto: 'Mañana', dias: 1 },
  { texto: 'En una semana', dias: 7 },
];

/** Los atajos del campo «Día» del diálogo, contados desde `hoy` (el día de Madrid): un toque en vez del calendario */
export function diasRapidos(hoy: string): DiaRapido[] {
  return SALTOS.map(({ texto, dias }) => ({ texto, dia: sumarDias(hoy, dias) }));
}
