import { RastreoResumen } from '../interfaces/rastreo.interface';

const DIAS = ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'];

/**
 * «Rastreo del 06/11 (viernes)» para el selector. El año solo sale si no es el actual. Si hay más de
 * uno el mismo día, se numeran por orden de lectura: «Rastreo del 30/10 (viernes) · 2 de 2».
 */
export function etiquetaRastreo(
  rastreo: RastreoResumen,
  todos: readonly RastreoResumen[],
  anioActual: number,
): string {
  const [anio, mes, dia] = rastreo.fecha.split('-').map(Number);
  // En UTC: la fecha es un día del calendario, no un instante; así no la mueve la zona del navegador
  const semana = DIAS[new Date(Date.UTC(anio, mes - 1, dia)).getUTCDay()];
  const anioTexto = anio === anioActual ? '' : `/${anio}`;
  const base = `Rastreo del ${dosCifras(dia)}/${dosCifras(mes)}${anioTexto} (${semana})`;

  const mismoDia = todos.filter((r) => r.fecha === rastreo.fecha).sort((a, b) => a.id - b.id);
  if (mismoDia.length < 2) {
    return base;
  }
  const posicion = mismoDia.findIndex((r) => r.id === rastreo.id) + 1;
  return `${base} · ${posicion} de ${mismoDia.length}`;
}

/**
 * «1 nueva», «4 nuevas»: las ofertas que quedan por mirar, como dice Eduardo. Corto a propósito:
 * va detrás de la etiqueta en el selector y a 360 px «· 4 por mirar» se cortaba.
 */
export function textoNuevas(pendientes: number): string {
  return pendientes === 1 ? '1 nueva' : `${pendientes} nuevas`;
}

function dosCifras(numero: number): string {
  return String(numero).padStart(2, '0');
}
