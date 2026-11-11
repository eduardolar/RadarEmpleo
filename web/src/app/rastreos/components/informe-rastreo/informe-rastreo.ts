import { Component, input } from '@angular/core';
import { SeccionPlegable } from '../../../shared/components/seccion-plegable/seccion-plegable';
import { TextoMarkdown } from '../../../shared/components/texto-markdown/texto-markdown';
import { senalPantallaGrande } from '../../../shared/utils/pantalla';
import { RastreoDetalle } from '../../interfaces/rastreo.interface';

/**
 * El informe del rastreo tal como lo escribió el equipo. Donde caben dos columnas (≥ 1024 px) va
 * abierto a la izquierda; más estrecho, plegado en una línea encima de las ofertas para no empujarlas
 */
@Component({
  imports: [SeccionPlegable, TextoMarkdown],
  selector: 'app-informe-rastreo',
  templateUrl: './informe-rastreo.html',
  host: { class: 'block' },
})
export class InformeRastreo {
  readonly rastreo = input.required<RastreoDetalle>();

  protected readonly grande = senalPantallaGrande();
}
