import { diaMadrid, formatearFecha, sumarDias } from '../../shared/utils/fechas-madrid';
import { EjecucionAgente } from '../interfaces/ejecucion.interface';
import { claveDeTarea } from './ultimas-ejecuciones';

export type FiltroTareas = 'todas' | 'no-han-salido' | 'en-marcha';

export const FILTROS_TAREAS: readonly { valor: FiltroTareas; texto: string }[] = [
  { valor: 'todas', texto: 'Todas' },
  { valor: 'no-han-salido', texto: 'No han salido' },
  { valor: 'en-marcha', texto: 'En marcha' },
];

/** Las tareas de un día de Madrid, con su encabezado («Hoy», «Ayer» o «20 nov 2026») */
export interface TareasDeUnDia {
  dia: string;
  titulo: string;
  tareas: readonly EjecucionAgente[];
}

type ClaveDeTarea = Pick<EjecucionAgente, 'tarea' | 'candidaturaId' | 'rastreoId'>;

/**
 * Los ids de los errores ya resueltos: la misma tarea, con la misma candidatura y rastreo, salió bien
 * después (una con id mayor terminó en Ok). En /agentes se ven atenuados
 */
export function erroresResueltos(
  lista: readonly (ClaveDeTarea & Pick<EjecucionAgente, 'id' | 'estado'>)[],
): ReadonlySet<number> {
  const ultimaBuena = new Map<string, number>();
  for (const e of lista) {
    if (e.estado === 'Ok' && (ultimaBuena.get(claveDeTarea(e)) ?? -Infinity) < e.id) {
      ultimaBuena.set(claveDeTarea(e), e.id);
    }
  }
  return new Set(
    lista
      .filter((e) => e.estado === 'Error' && (ultimaBuena.get(claveDeTarea(e)) ?? -Infinity) > e.id)
      .map((e) => e.id),
  );
}

export function pasaElFiltro(e: Pick<EjecucionAgente, 'estado'>, filtro: FiltroTareas): boolean {
  switch (filtro) {
    case 'no-han-salido':
      return e.estado === 'Error';
    case 'en-marcha':
      return e.estado === 'EnCola' || e.estado === 'EnCurso';
    default:
      return true;
  }
}

/** Agrupa por el día de Madrid en que se lanzaron, sin cambiar el orden en que llegan (lo último, arriba) */
export function agruparPorDia(lista: readonly EjecucionAgente[], hoy: string): TareasDeUnDia[] {
  const ayer = sumarDias(hoy, -1);
  const grupos = new Map<string, EjecucionAgente[]>();
  for (const e of lista) {
    const dia = diaMadrid(e.fechaAlta);
    const grupo = grupos.get(dia);
    if (grupo) {
      grupo.push(e);
    } else {
      grupos.set(dia, [e]);
    }
  }
  return [...grupos].map(([dia, tareas]) => ({
    dia,
    titulo: dia === hoy ? 'Hoy' : dia === ayer ? 'Ayer' : formatearFecha(dia),
    tareas,
  }));
}

/** «Hoy: 3 tareas · 1 no ha salido»: lo lanzado hoy (día de Madrid) y cuántas de esas han fallado */
export function recuentoDeHoy(
  lista: readonly Pick<EjecucionAgente, 'fechaAlta' | 'estado'>[],
  hoy: string,
): string {
  const deHoy = lista.filter((e) => diaMadrid(e.fechaAlta) === hoy);
  if (deHoy.length === 0) {
    return 'Hoy: ninguna tarea';
  }
  const tareas = deHoy.length === 1 ? '1 tarea' : `${deHoy.length} tareas`;
  const fallos = deHoy.filter((e) => e.estado === 'Error').length;
  if (fallos === 0) {
    return `Hoy: ${tareas}`;
  }
  return `Hoy: ${tareas} · ${fallos === 1 ? '1 no ha salido' : `${fallos} no han salido`}`;
}
