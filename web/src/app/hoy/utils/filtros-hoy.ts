import { TipoRecordatorio } from '../../candidaturas/interfaces/candidatura.interface';
import {
  diaDeLaSemana,
  diaEncabezado,
  diaMadrid,
  sumarDias,
} from '../../shared/utils/fechas-madrid';
import { RecordatorioAgenda } from '../interfaces/panel-hoy.interface';

/** Una semana de lunes a domingo, con los dos días incluidos: «2026-11-09» a «2026-11-15» */
export interface SemanaHoy {
  desde: string;
  hasta: string;
}

/** Lo que se está filtrando en Hoy; null es «sin filtrar» */
export interface FiltroHoy {
  tipo: TipoRecordatorio | null;
  /** «2026-11-18» */
  dia: string | null;
  /** Esta semana entera (lo que cuenta la cifra de las entrevistas del resumen) */
  semana?: SemanaHoy | null;
}

/** Un chip de los filtros por tipo. `tipo` null es «Todas» */
export interface ChipTipo {
  tipo: TipoRecordatorio | null;
  texto: string;
  total: number;
}

/** Los recordatorios de un mismo día, bajo su encabezado */
export interface GrupoDia {
  /** «2026-11-18» */
  dia: string;
  /** «Miércoles, 18 nov» */
  titulo: string;
  recordatorios: RecordatorioAgenda[];
}

// En el orden en que salen los chips. En plural y cortos: «LinkedIn» y no «Publicaciones en LinkedIn»
const TEXTOS_TIPO: readonly { tipo: TipoRecordatorio; texto: string }[] = [
  { tipo: 'Seguimiento', texto: 'Seguimientos' },
  { tipo: 'Entrevista', texto: 'Entrevistas' },
  { tipo: 'Plazo', texto: 'Plazos' },
  { tipo: 'PublicacionLinkedIn', texto: 'LinkedIn' },
  { tipo: 'Otro', texto: 'Otros' },
];

const DIA = /^\d{4}-\d{2}-\d{2}$/;

/** «1 tarea», «5 tareas»: la palabra de todos los contadores de Hoy */
export function textoTareas(total: number): string {
  return total === 1 ? '1 tarea' : `${total} tareas`;
}

/** El tipo de «?tipo=entrevista», sin mirar mayúsculas; null si no es ninguno */
export function leerTipo(texto: string | null): TipoRecordatorio | null {
  const buscado = texto?.trim().toLowerCase();
  return TEXTOS_TIPO.find(({ tipo }) => tipo.toLowerCase() === buscado)?.tipo ?? null;
}

/** El día de «?dia=2026-11-18»; null si no es un día del calendario */
export function leerDia(texto: string | null): string | null {
  if (texto === null || !DIA.test(texto)) {
    return null;
  }
  const fecha = new Date(`${texto}T00:00:00Z`);
  // «2026-02-31» se convertiría en el 3 de marzo: no es un día
  return !Number.isNaN(fecha.getTime()) && fecha.toISOString().slice(0, 10) === texto
    ? texto
    : null;
}

/** La semana de lunes a domingo en la que cae `dia` («2026-11-13») */
export function semanaDe(dia: string): SemanaHoy {
  const desde = sumarDias(dia, -((diaDeLaSemana(dia) + 6) % 7));
  return { desde, hasta: sumarDias(desde, 6) };
}

/** Los de ese tipo y ese día (o esa semana) de Madrid. Sin filtro devuelve la misma lista */
export function filtrar(
  recordatorios: RecordatorioAgenda[],
  filtro: FiltroHoy,
): RecordatorioAgenda[] {
  const semana = filtro.semana ?? null;
  if (filtro.tipo === null && filtro.dia === null && semana === null) {
    return recordatorios;
  }
  return recordatorios.filter((r) => {
    const dia = diaMadrid(r.fechaHora);
    return (
      (filtro.tipo === null || r.tipo === filtro.tipo) &&
      (filtro.dia === null || dia === filtro.dia) &&
      (semana === null || (dia >= semana.desde && dia <= semana.hasta))
    );
  });
}

/**
 * Los chips de los filtros: «Todas» y los tipos que tienen algo pendiente. El número de cada uno es
 * lo que se vería al pulsarlo, así que cuenta solo lo de `dia` (o lo de `semana`) si se está mirando
 * un día. Un tipo con pendientes otros días sigue saliendo (con 0): los chips no bailan al pulsar el
 * calendario
 */
export function chipsTipo(
  pendientes: RecordatorioAgenda[],
  dia: string | null,
  semana: SemanaHoy | null = null,
): ChipTipo[] {
  const delDia = filtrar(pendientes, { tipo: null, dia, semana });
  const porTipo = TEXTOS_TIPO.filter(({ tipo }) => pendientes.some((r) => r.tipo === tipo)).map(
    ({ tipo, texto }) => ({ tipo, texto, total: delDia.filter((r) => r.tipo === tipo).length }),
  );
  return [{ tipo: null, texto: 'Todas', total: delDia.length }, ...porTipo];
}

/** Parte una lista ya ordenada por fecha en grupos de un mismo día de Madrid */
export function agruparPorDia(recordatorios: RecordatorioAgenda[]): GrupoDia[] {
  const grupos: GrupoDia[] = [];
  for (const recordatorio of recordatorios) {
    const dia = diaMadrid(recordatorio.fechaHora);
    const ultimo = grupos.at(-1);
    if (ultimo?.dia === dia) {
      ultimo.recordatorios.push(recordatorio);
    } else {
      grupos.push({ dia, titulo: diaEncabezado(dia), recordatorios: [recordatorio] });
    }
  }
  return grupos;
}
