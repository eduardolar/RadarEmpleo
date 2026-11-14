import { diaCorto, diaDeLaSemana, sumarDias } from '../../shared/utils/fechas-madrid';

export interface OpcionPosponer {
  texto: string;
  /** El día al que se pasa, como «2026-11-14» */
  dia: string;
  /** «sáb 14 nov», para verlo al lado */
  fecha: string;
}

// Lo vencido y lo de hoy se pospone contando desde hoy
const DESDE_HOY: readonly { texto: string; dias: number }[] = [
  { texto: 'Mañana', dias: 1 },
  { texto: 'Dentro de 3 días', dias: 3 },
  { texto: 'Dentro de una semana', dias: 7 },
];

// Lo que todavía no toca, desde su día: «Mañana» lo adelantaría en vez de posponerlo
const DESDE_SU_DIA: readonly { texto: string; dias: number }[] = [
  { texto: 'Un día después', dias: 1 },
  { texto: '3 días después', dias: 3 },
  { texto: 'Una semana después', dias: 7 },
];

const SABADO = 6;
const DOMINGO = 0;

/**
 * Las opciones del menú «Posponer». Se cuentan desde `hoy` (el día de Madrid que da el panel) o, si el
 * recordatorio es de un día posterior (`dia`), desde ese día. Si el día siguiente cae en fin de semana,
 * la primera opción es «El lunes»: nadie contesta un sábado. No repite días (un viernes, «El lunes» y
 * «Dentro de 3 días» son el mismo).
 */
export function opcionesPosponer(hoy: string, dia: string = hoy): OpcionPosponer[] {
  const desdeSuDia = dia > hoy;
  const base = desdeSuDia ? dia : hoy;
  const opciones: OpcionPosponer[] = [];
  for (const salto of desdeSuDia ? DESDE_SU_DIA : DESDE_HOY) {
    let destino = sumarDias(base, salto.dias);
    let texto = salto.texto;
    if (salto.dias === 1) {
      const semana = diaDeLaSemana(destino);
      if (semana === SABADO || semana === DOMINGO) {
        destino = sumarDias(destino, semana === SABADO ? 2 : 1);
        texto = 'El lunes';
      }
    }
    if (!opciones.some((opcion) => opcion.dia === destino)) {
      opciones.push({ texto, dia: destino, fecha: diaCorto(destino) });
    }
  }
  return opciones;
}
