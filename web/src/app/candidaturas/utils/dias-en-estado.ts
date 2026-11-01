import { diasNaturalesEntre } from '../../shared/utils/fechas-madrid';

/**
 * Días naturales (de Madrid) que lleva la candidatura en su estado. Recibe `fechaEstado` (cuándo
 * entró en el estado actual), no `fechaUltimoCambio`, que también cambia al editar los datos.
 */
export function diasEnEstado(fechaEstado: string, ahora: Date = new Date()): number {
  // Si el reloj del PC va algo atrasado respecto al del servidor, no se enseñan días negativos
  return Math.max(0, diasNaturalesEntre(fechaEstado, ahora));
}

/** «Hoy», «1 día», «5 días» */
export function textoDiasEnEstado(dias: number): string {
  if (dias === 0) {
    return 'Hoy';
  }
  return dias === 1 ? '1 día' : `${dias} días`;
}
