import { BreakpointObserver } from '@angular/cdk/layout';
import { inject, Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';

// El mismo corte que «md» de Tailwind: a partir de aquí hay columnas, arrastre y visor de PDF
export const ESCRITORIO = '(min-width: 768px)';

// El mismo corte que «lg» de Tailwind: con el menú lateral, desde aquí caben dos columnas de contenido
export const PANTALLA_GRANDE = '(min-width: 1024px)';

/** true en pantallas de escritorio. Se llama al crear el componente (necesita el inyector) */
export function senalEscritorio(): Signal<boolean> {
  return senalMedia(ESCRITORIO);
}

/** true donde caben dos columnas junto al menú lateral. Se llama al crear el componente */
export function senalPantallaGrande(): Signal<boolean> {
  return senalMedia(PANTALLA_GRANDE);
}

function senalMedia(consulta: string): Signal<boolean> {
  const breakpoints = inject(BreakpointObserver);
  return toSignal(breakpoints.observe(consulta).pipe(map((estado) => estado.matches)), {
    initialValue: breakpoints.isMatched(consulta),
  });
}
