import { Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { EstadoCandidatura } from '../../interfaces/candidatura.interface';
import { ETIQUETAS_ESTADO } from '../../utils/etiquetas';
import { estadosPosibles } from '../../utils/transiciones-permitidas';

/**
 * Botón «Mover a…» con los estados a los que se puede pasar. Es la forma de cambiar de estado en el
 * móvil y, en el PC, la alternativa con teclado al arrastrar.
 */
@Component({
  imports: [MatButtonModule, MatMenuModule],
  selector: 'app-menu-mover',
  templateUrl: './menu-mover.html',
  host: { class: 'shrink-0' },
})
export class MenuMover {
  readonly estado = input.required<EstadoCandidatura>();
  /** Para el aria-label, que empieza por el texto visible: «Mover a… (Desarrollador Angular)» */
  readonly descripcion = input.required<string>();
  readonly elegir = output<EstadoCandidatura>();

  protected readonly destinos = computed(() =>
    estadosPosibles(this.estado()).map((estado) => ({
      estado,
      etiqueta: estado === 'Detectada' ? 'Reabrir' : ETIQUETAS_ESTADO[estado],
    })),
  );
}
