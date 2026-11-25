import { Component, input, output } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { RouterLink } from '@angular/router';
import { FechaMadridPipe } from '../../../shared/pipes/fecha-madrid.pipe';
import { FilaVistaPrevia } from '../../interfaces/importacion.interface';
import { textoEstadoFila } from '../../utils/resumen-importacion';

/**
 * Filas de la vista previa: con quién, cuándo, en qué estado y lo que haya que saber de cada una. Si
 * ya hay una candidatura en la app (la misma o una parecida), un enlace lleva a su ficha. Con `elegidas`
 * (las que se pueden aplicar), cada fila lleva su casilla.
 */
@Component({
  imports: [FechaMadridPipe, MatCheckboxModule, RouterLink],
  selector: 'app-lista-importacion',
  templateUrl: './lista-importacion.html',
  host: { class: 'block' },
})
export class ListaImportacion {
  readonly filas = input.required<FilaVistaPrevia[]>();
  /** Las claves marcadas; null en los grupos que no se aplican: sin casillas */
  readonly elegidas = input<ReadonlySet<string> | null>(null);
  /** Mientras guarda no se cambia lo elegido */
  readonly bloqueada = input(false);
  readonly elegir = output<{ clave: string; marcada: boolean }>();

  protected readonly textoEstado = textoEstadoFila;

  protected etiquetaCasilla(fila: FilaVistaPrevia): string {
    const accion = fila.resultado === 'Nueva' ? 'Añadir' : 'Actualizar';
    return `${accion}: ${fila.puesto} en ${fila.empresa}`;
  }
}
