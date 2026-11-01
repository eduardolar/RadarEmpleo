// La API manda las fechas en UTC; aquí se enseñan siempre en hora de Madrid, esté donde esté el
// navegador (el móvil de viaje, la demo en un servidor de otro país...).
const ZONA_MADRID = 'Europe/Madrid';

export type FormatoFecha = 'dia' | 'dia-hora';

// Crear un Intl.DateTimeFormat es caro: se crean una vez y se reutilizan
const FORMATOS: Record<FormatoFecha, Intl.DateTimeFormat> = {
  dia: new Intl.DateTimeFormat('es-ES', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: ZONA_MADRID,
  }),
  'dia-hora': new Intl.DateTimeFormat('es-ES', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: ZONA_MADRID,
  }),
};

const PARTES_DIA = new Intl.DateTimeFormat('es-ES', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  timeZone: ZONA_MADRID,
});

const MS_POR_DIA = 24 * 60 * 60 * 1000;

/** «30 oct 2026» o «30 oct 2026, 10:05» en hora de Madrid. Cadena vacía si no hay fecha válida */
export function formatearFecha(
  fecha: string | Date | null | undefined,
  formato: FormatoFecha = 'dia',
): string {
  if (fecha === null || fecha === undefined) {
    return '';
  }
  const instante = new Date(fecha);
  return Number.isNaN(instante.getTime()) ? '' : FORMATOS[formato].format(instante);
}

/** Día de Madrid en que cae un instante, como «2026-10-30» */
export function diaMadrid(fecha: string | Date): string {
  const partes = PARTES_DIA.formatToParts(new Date(fecha));
  const parte = (tipo: Intl.DateTimeFormatPartTypes) =>
    partes.find((p) => p.type === tipo)?.value ?? '';
  return `${parte('year')}-${parte('month')}-${parte('day')}`;
}

/**
 * Días naturales de Madrid que van de `desde` a `hasta` (0 si es el mismo día). Se cuentan días
 * del calendario, no bloques de 24 h: de las 23:00 a las 01:00 del día siguiente ya es 1 día.
 */
export function diasNaturalesEntre(desde: string | Date, hasta: string | Date): number {
  const [a, b] = [diaMadrid(desde), diaMadrid(hasta)].map((dia) => Date.parse(`${dia}T00:00:00Z`));
  return Math.round((b - a) / MS_POR_DIA);
}
