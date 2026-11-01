import { computed, inject, Injectable } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import { opcionDeUrl } from '../utils/opciones-menu';

/**
 * Sección del menú en la que se está. Sustituye a routerLinkActive, que solo mira la ruta del propio
 * enlace: la ficha cuelga de /candidaturas, pero para quien la usa sigue dentro del Tablero.
 */
@Injectable({ providedIn: 'root' })
export class SeccionActivaService {
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((evento) => evento instanceof NavigationEnd),
      map((evento) => evento.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /** Ruta de la opción activa del menú, o undefined si la URL no es de ninguna */
  readonly ruta = computed(() => opcionDeUrl(this.url())?.ruta);
}
