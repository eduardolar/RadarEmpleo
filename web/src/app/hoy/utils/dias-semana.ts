import { diaLargo } from '../../shared/utils/fechas-madrid';
import { DiaSemana } from '../interfaces/panel-hoy.interface';
import { textoTareas } from './filtros-hoy';

/** Un día del mini calendario de la semana, listo para pintar */
export interface DiaCalendario {
  /** «2026-11-13» */
  dia: string;
  /** «L», «M», «X»… */
  letra: string;
  /** El día del mes, sin ceros: «13» */
  numero: string;
  esHoy: boolean;
  recordatorios: number;
  /** Lo que lee el lector de pantalla: «miércoles 18 de noviembre: 2 tareas» */
  etiqueta: string;
}

// La semana llega de lunes a domingo. El miércoles es «X» para no repetir la «M» del martes
const LETRAS = ['L', 'M', 'X', 'J', 'V', 'S', 'D'];

/** Los días de la semana del resumen (de lunes a domingo) con lo que hace falta para pintarlos */
export function diasCalendario(semana: readonly DiaSemana[], hoy: string): DiaCalendario[] {
  return semana.map(({ dia, recordatorios }, indice) => {
    const esHoy = dia === hoy;
    const tareas = recordatorios === 0 ? 'sin tareas' : textoTareas(recordatorios);
    return {
      dia,
      letra: LETRAS[indice % LETRAS.length],
      numero: String(Number(dia.slice(8))),
      esHoy,
      recordatorios,
      etiqueta: `${diaLargo(dia)}${esHoy ? ', hoy' : ''}: ${tareas}`,
    };
  });
}

/**
 * El día de «Lo siguiente» bajo el calendario: el del primer pendiente pasada esta semana, pero solo
 * si de aquí al domingo ya no queda nada (si queda, lo siguiente ya se ve en el calendario)
 */
export function siguienteFueraDeSemana(
  semana: readonly DiaSemana[],
  hoy: string,
  siguienteDia: string | null,
): string | null {
  const quedaAlgo = semana.some(({ dia, recordatorios }) => dia > hoy && recordatorios > 0);
  // «?? null»: una API anterior a este campo no lo manda
  return quedaAlgo ? null : (siguienteDia ?? null);
}
