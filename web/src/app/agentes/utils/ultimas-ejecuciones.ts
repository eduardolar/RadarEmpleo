import { EjecucionAgente } from '../interfaces/ejecucion.interface';

/**
 * Los ids de la última ejecución (la de id mayor) de cada tarea y candidatura. En /agentes solo esas
 * ofrecen «Reintentar»: reintentar una vieja dejaría dos iguales en la lista
 */
export function ultimasDeCadaTarea(
  lista: readonly Pick<EjecucionAgente, 'id' | 'tarea' | 'candidaturaId'>[],
): ReadonlySet<number> {
  const ultimas = new Map<string, number>();
  for (const e of lista) {
    const clave = `${e.tarea}:${e.candidaturaId ?? ''}`;
    if ((ultimas.get(clave) ?? -Infinity) < e.id) {
      ultimas.set(clave, e.id);
    }
  }
  return new Set(ultimas.values());
}
