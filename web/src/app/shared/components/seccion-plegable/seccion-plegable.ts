import { Component, input } from '@angular/core';

/**
 * Bloque que se abre y se pliega con su título (un <details> nativo: funciona con teclado y lector de
 * pantalla sin nada más). Lo de dentro se pone entre las etiquetas, con su propio relleno.
 * - «tarjeta»: fondo claro con borde, para el contenido principal (secciones del perfil, informe).
 * - «suave»: fondo gris, para lo secundario que va al final (ignoradas, grupos de la importación).
 */
@Component({
  selector: 'app-seccion-plegable',
  templateUrl: './seccion-plegable.html',
  host: { class: 'block' },
})
export class SeccionPlegable {
  readonly titulo = input.required<string>();
  /** Cómo empieza; después la abre o la cierra quien la usa */
  readonly abierta = input(false);
  readonly aspecto = input<'tarjeta' | 'suave'>('tarjeta');
}
