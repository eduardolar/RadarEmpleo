import { Component, computed, input } from '@angular/core';
import { IconoMenuNombre } from '../../interfaces/opcion-menu.interface';

// Trazos propios (24x24, trazo editorial de 1,7 px y puntas redondas; ver docs/DISENO.md):
// Hoy, la taza del café de la mañana; Tablero, un corcho con fichas; Apuntar, un lápiz;
// Rastreos, el radar de la marca. «Más» son los tres puntos de Heroicons.
const TRAZOS: Record<IconoMenuNombre, string> = {
  hoy: 'M4.5 10.5h11v3.75a4.75 4.75 0 0 1-4.75 4.75h-1.5A4.75 4.75 0 0 1 4.5 14.25V10.5ZM15.5 11.5h1.25a2.25 2.25 0 0 1 0 4.5H15.5M3.5 21.25h14M8 3.5c-.75.9.75 1.85 0 2.75M12 3.5c-.75.9.75 1.85 0 2.75',
  tablero:
    'M4.5 4.5h15A1.5 1.5 0 0 1 21 6v12a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18V6a1.5 1.5 0 0 1 1.5-1.5ZM6.75 8.25h3.5v3.5h-3.5ZM6.75 14h3.5v2.25h-3.5ZM13.75 8.25h3.5v6h-3.5Z',
  apuntar:
    'M4.5 19.5l1-4.5L15.94 4.56a1.5 1.5 0 0 1 2.12 0l1.38 1.38a1.5 1.5 0 0 1 0 2.12L9 18.5l-4.5 1ZM14 6.5l3.5 3.5M13 20.25h7',
  rastreos:
    'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0ZM16.5 12a4.5 4.5 0 1 1-9 0 4.5 4.5 0 0 1 9 0ZM12 12l5.75-5.75M15.1 15.9a.9.9 0 1 0 1.8 0 .9.9 0 0 0-1.8 0Z',
  mas: 'M6.75 12a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0ZM12.75 12a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0ZM18.75 12a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Z',
};

/** Icono de una sección del menú. El tamaño lo pone quien lo usa (p. ej. class="h-5 w-5") */
@Component({
  selector: 'app-icono-menu',
  templateUrl: './icono-menu.html',
  host: { class: 'block', 'aria-hidden': 'true' },
})
export class IconoMenu {
  readonly icono = input.required<IconoMenuNombre>();

  protected readonly trazo = computed(() => TRAZOS[this.icono()]);
}
