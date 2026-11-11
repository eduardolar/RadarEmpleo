import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * «Volver a…» de las páginas interiores (ficha, importación, perfil). En el móvil hace falta: la única
 * otra salida sería la pestaña de abajo
 */
@Component({
  imports: [RouterLink],
  selector: 'app-enlace-volver',
  templateUrl: './enlace-volver.html',
  host: { class: 'block' },
})
export class EnlaceVolver {
  readonly ruta = input.required<string>();
  readonly texto = input.required<string>();
}
