import { CambioEstado, Nota } from '../interfaces/candidatura.interface';

export type ElementoLineaTiempo =
  | { tipo: 'estado'; clave: string; fecha: string; cambio: CambioEstado }
  | { tipo: 'nota'; clave: string; fecha: string; nota: Nota };

/**
 * Mezcla el historial de estados y las notas en una sola lista, de lo más reciente a lo más
 * antiguo. Con la misma fecha, el cambio de estado va antes que la nota.
 */
export function lineaDeTiempo(
  historial: readonly CambioEstado[],
  notas: readonly Nota[],
): ElementoLineaTiempo[] {
  const elementos: ElementoLineaTiempo[] = [
    ...historial.map((cambio) => ({
      tipo: 'estado' as const,
      clave: `estado-${cambio.id}`,
      fecha: cambio.fecha,
      cambio,
    })),
    ...notas.map((nota) => ({
      tipo: 'nota' as const,
      clave: `nota-${nota.id}`,
      fecha: nota.fecha,
      nota,
    })),
  ];

  // sort es estable: a igual fecha se mantiene «estados primero»
  return elementos.sort((a, b) => Date.parse(b.fecha) - Date.parse(a.fecha));
}
