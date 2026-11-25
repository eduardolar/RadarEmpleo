import { EjecucionAgente } from '../interfaces/ejecucion.interface';

/** Lo que hace que dos ejecuciones sean «la misma tarea»: la tarea, la candidatura y el rastreo */
export function claveDeTarea(
  e: Pick<EjecucionAgente, 'tarea' | 'candidaturaId' | 'rastreoId'>,
): string {
  return `${e.tarea}:${e.candidaturaId ?? ''}:${e.rastreoId ?? ''}`;
}

/**
 * Los ids de la última ejecución (la de id mayor) de cada tarea, candidatura y rastreo. En /agentes solo esas
 * ofrecen «Reintentar»: reintentar una vieja dejaría dos iguales en la lista
 */
export function ultimasDeCadaTarea(
  lista: readonly Pick<EjecucionAgente, 'id' | 'tarea' | 'candidaturaId' | 'rastreoId'>[],
): ReadonlySet<number> {
  const ultimas = new Map<string, number>();
  for (const e of lista) {
    const clave = claveDeTarea(e);
    if ((ultimas.get(clave) ?? -Infinity) < e.id) {
      ultimas.set(clave, e.id);
    }
  }
  return new Set(ultimas.values());
}
