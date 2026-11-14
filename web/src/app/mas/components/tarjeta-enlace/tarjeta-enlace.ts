import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Tarjeta de «Más»: entera es el enlace a su página. El icono (un svg de 24×24) va por ng-content.
 * Va dentro de un <li>
 */
@Component({
  imports: [RouterLink],
  selector: 'app-tarjeta-enlace',
  templateUrl: './tarjeta-enlace.html',
  host: { class: 'flex' },
})
export class TarjetaEnlace {
  readonly ruta = input.required<string>();
  readonly titulo = input.required<string>();
  readonly descripcion = input.required<string>();
}
