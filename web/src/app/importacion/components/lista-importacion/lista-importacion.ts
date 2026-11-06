import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FechaMadridPipe } from '../../../shared/pipes/fecha-madrid.pipe';
import { FilaVistaPrevia } from '../../interfaces/importacion.interface';
import { textoEstadoFila } from '../../utils/resumen-importacion';

/**
 * Filas de la vista previa: con quién, cuándo, en qué estado y lo que haya que saber de cada una. Si
 * ya hay una candidatura en la app (la misma o una parecida), un enlace lleva a su ficha.
 */
@Component({
  imports: [FechaMadridPipe, RouterLink],
  selector: 'app-lista-importacion',
  templateUrl: './lista-importacion.html',
  host: { class: 'block' },
})
export class ListaImportacion {
  readonly filas = input.required<FilaVistaPrevia[]>();

  protected readonly textoEstado = textoEstadoFila;
}
