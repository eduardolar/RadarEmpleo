import { Component, computed, input } from '@angular/core';
import { EstadoCandidatura } from '../../../candidaturas/interfaces/candidatura.interface';
import { anchoBarra } from '../../utils/barras';

/** Una fila de la lista: la etiqueta, la cifra y, si la barra va en el color de un estado, cuál */
export interface FilaBarra {
  etiqueta: string;
  valor: number;
  estado?: EstadoCandidatura;
}

/**
 * Gráfico de barras accesible: una lista en la que cada fila dice su etiqueta y su cifra visible, y la
 * barra solo acompaña (aria-hidden). El lector lee «Enviada, 12» y nada depende del color.
 * - `estado`: cada barra en el color de su estado (data-estado de la fila).
 * - `enviada`: todas en el color de Enviada.
 * - `neutro`: todas en gris, con borde.
 */
@Component({
  selector: 'app-lista-barras',
  templateUrl: './lista-barras.html',
  host: { class: 'block' },
})
export class ListaBarras {
  readonly filas = input.required<readonly FilaBarra[]>();
  readonly color = input<'estado' | 'enviada' | 'neutro'>('neutro');
  /** Nombre de la lista para el lector de pantalla */
  readonly etiqueta = input.required<string>();

  protected readonly conAncho = computed(() => {
    const filas = this.filas();
    const maximo = Math.max(0, ...filas.map((f) => f.valor));
    return filas.map((fila) => ({ ...fila, ancho: anchoBarra(fila.valor, maximo) }));
  });
}
