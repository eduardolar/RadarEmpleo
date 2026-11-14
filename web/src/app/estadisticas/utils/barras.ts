import { diaCorto } from '../../shared/utils/fechas-madrid';

/**
 * Ancho de una barra en % respecto a la mayor de la lista. Con 0 no hay barra; con algo, al menos un 2 %
 * para que se vea que no es cero. Si todas son 0, todas a 0
 */
export function anchoBarra(valor: number, maximo: number): number {
  if (valor <= 0 || maximo <= 0) {
    return 0;
  }
  return Math.max(2, Math.round((100 * valor) / maximo));
}

/** «Semana del 2 nov» para el lunes «2026-11-02» */
export function etiquetaSemana(lunes: string): string {
  // diaCorto da «lun 2 nov»: sobra el día de la semana, que siempre es lunes
  return `Semana del ${diaCorto(lunes).split(' ').slice(1).join(' ')}`;
}

/** «el mismo día», «1 día» o «9 días» (con decimal y coma: «6,5 días») */
export function textoDias(dias: number): string {
  if (dias === 0) {
    return 'el mismo día';
  }
  const cifra = dias.toLocaleString('es-ES', { maximumFractionDigits: 1 });
  return dias === 1 ? '1 día' : `${cifra} días`;
}
