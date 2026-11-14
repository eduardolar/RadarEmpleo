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
  /**
   * Si el título hace de encabezado de la página (2 para un «h2»), para que salga en la lista de
   * encabezados del lector de pantalla junto a las secciones que no se pliegan (un encabezado oculto
   * justo antes del bloque). Sin él, es solo texto
   */
  readonly nivelTitulo = input<2 | 3 | null>(null);
  /** Un id para el título (el <summary>), por si la página tiene que llevarle el foco */
  readonly idTitulo = input<string | null>(null);
}
