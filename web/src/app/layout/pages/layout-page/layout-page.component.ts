import { Component, ElementRef, inject, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, skip } from 'rxjs';
import { BarraInferior } from '../../components/barra-inferior/barra-inferior';
import { MenuLateral } from '../../components/menu-lateral/menu-lateral';

@Component({
  imports: [RouterOutlet, MenuLateral, BarraInferior],
  selector: 'app-layout-page',
  templateUrl: './layout-page.component.html',
})
export default class LayoutPageComponent {
  private readonly principal = viewChild.required<ElementRef<HTMLElement>>('principal');

  constructor() {
    // Tras cambiar de página el foco se queda en el enlace del menú: se lleva a <main> para que
    // el lector de pantalla y el teclado empiecen por el contenido nuevo. En la primera carga no.
    inject(Router)
      .events.pipe(
        filter((evento) => evento instanceof NavigationEnd),
        skip(1),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.enfocarPrincipal());
  }

  protected saltarAlContenido(evento: Event): void {
    evento.preventDefault();
    this.enfocarPrincipal();
  }

  private enfocarPrincipal(): void {
    // Sin desplazar: del scroll ya se encarga withInMemoryScrolling
    this.principal().nativeElement.focus({ preventScroll: true });
  }
}
