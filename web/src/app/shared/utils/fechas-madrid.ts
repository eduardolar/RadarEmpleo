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

const PARTES_HORA = new Intl.DateTimeFormat('es-ES', {
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
  timeZone: ZONA_MADRID,
});

const PARTES_DIA_CORTO = new Intl.DateTimeFormat('es-ES', {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
  timeZone: ZONA_MADRID,
});

const PARTES_DIA_LARGO = new Intl.DateTimeFormat('es-ES', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  timeZone: ZONA_MADRID,
});

const MS_POR_DIA = 24 * 60 * 60 * 1000;

const SOLO_DIA = /^\d{4}-\d{2}-\d{2}$/;

function parte(partes: Intl.DateTimeFormatPart[], tipo: Intl.DateTimeFormatPartTypes): string {
  return partes.find((p) => p.type === tipo)?.value ?? '';
}

/** Un día suelto «2026-11-14» se toma a mediodía: es el mismo día en Madrid todo el año */
function aInstante(fecha: string | Date): Date {
  return new Date(typeof fecha === 'string' && SOLO_DIA.test(fecha) ? `${fecha}T12:00:00Z` : fecha);
}

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
  return `${parte(partes, 'year')}-${parte(partes, 'month')}-${parte(partes, 'day')}`;
}

/** Hora de Madrid de un instante, como «15:00» (la que entiende un campo de hora) */
export function horaMadrid(fecha: string | Date): string {
  const partes = PARTES_HORA.formatToParts(new Date(fecha));
  return `${parte(partes, 'hour')}:${parte(partes, 'minute')}`;
}

/**
 * «vie 14 nov»: el día de Madrid de un instante o un día suelto «2026-11-14». Sin año: es para fechas
 * cercanas (lo de Hoy)
 */
export function diaCorto(fecha: string | Date): string {
  const instante = aInstante(fecha);
  if (Number.isNaN(instante.getTime())) {
    return '';
  }
  const partes = PARTES_DIA_CORTO.formatToParts(instante);
  return `${parte(partes, 'weekday')} ${parte(partes, 'day')} ${parte(partes, 'month')}`;
}

/**
 * «miércoles 18 de noviembre»: como `diaCorto`, pero con las palabras enteras, para leerlo en voz alta
 * (los lectores de pantalla no siempre entienden «mié» o «nov»)
 */
export function diaLargo(fecha: string | Date): string {
  const instante = aInstante(fecha);
  if (Number.isNaN(instante.getTime())) {
    return '';
  }
  const partes = PARTES_DIA_LARGO.formatToParts(instante);
  return `${parte(partes, 'weekday')} ${parte(partes, 'day')} de ${parte(partes, 'month')}`;
}

/**
 * «Miércoles, 18 nov»: el día suelto «2026-11-18» como encabezado de un grupo, con el día de la semana
 * entero y en mayúscula
 */
export function diaEncabezado(dia: string): string {
  const instante = aInstante(dia);
  if (Number.isNaN(instante.getTime())) {
    return '';
  }
  const semana = parte(PARTES_DIA_LARGO.formatToParts(instante), 'weekday');
  const corto = PARTES_DIA_CORTO.formatToParts(instante);
  return `${semana.charAt(0).toUpperCase()}${semana.slice(1)}, ${parte(corto, 'day')} ${parte(corto, 'month')}`;
}

/** Día de la semana de un día suelto «2026-11-13»: 0 es domingo y 6, sábado (como `Date.getDay`) */
export function diaDeLaSemana(dia: string): number {
  return new Date(`${dia}T00:00:00Z`).getUTCDay();
}

/** Suma días de calendario a un día «2026-11-13» (con 1, «2026-11-14»). No depende de la zona del navegador */
export function sumarDias(dia: string, dias: number): string {
  const fecha = new Date(`${dia}T00:00:00Z`);
  fecha.setUTCDate(fecha.getUTCDate() + dias);
  return fecha.toISOString().slice(0, 10);
}

/**
 * Días naturales de Madrid que van de `desde` a `hasta` (0 si es el mismo día). Se cuentan días
 * del calendario, no bloques de 24 h: de las 23:00 a las 01:00 del día siguiente ya es 1 día.
 */
export function diasNaturalesEntre(desde: string | Date, hasta: string | Date): number {
  const [a, b] = [diaMadrid(desde), diaMadrid(hasta)].map((dia) => Date.parse(`${dia}T00:00:00Z`));
  return Math.round((b - a) / MS_POR_DIA);
}

/**
 * El instante en que el reloj de Madrid marca `hora` («02:30») el día `dia` («2027-03-28»); null si
 * esa hora no existe ese día (la noche de marzo en que el reloj salta de las 02:00 a las 03:00). Si
 * pasa dos veces (la de octubre), la segunda, la de invierno, como hace la API
 */
export function instanteMadrid(dia: string, hora: string): Date | null {
  const comoUtc = Date.parse(`${dia}T${hora}:00Z`);
  if (Number.isNaN(comoUtc)) {
    return null;
  }
  // Madrid va una hora (invierno) o dos (verano) por delante de UTC
  for (const desfase of [1, 2]) {
    const instante = new Date(comoUtc - desfase * 60 * 60 * 1000);
    if (diaMadrid(instante) === dia && horaMadrid(instante) === hora) {
      return instante;
    }
  }
  return null;
}

/** Milisegundos que faltan, desde `ahora`, para la próxima medianoche de Madrid */
export function msHastaMedianocheMadrid(ahora: Date): number {
  const manana = sumarDias(diaMadrid(ahora), 1);
  // La medianoche existe siempre: los cambios de hora de Madrid son a las 02:00 y a las 03:00
  return instanteMadrid(manana, '00:00')!.getTime() - ahora.getTime();
}
