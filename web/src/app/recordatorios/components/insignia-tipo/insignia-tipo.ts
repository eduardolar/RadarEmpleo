import { Component, computed, input } from '@angular/core';
import { TipoRecordatorio } from '../../../candidaturas/interfaces/candidatura.interface';
import { ETIQUETAS_TIPO_RECORDATORIO } from '../../../candidaturas/utils/etiquetas';

/**
 * La insignia gris del tipo de recordatorio, con su icono y siempre con texto: la misma en la tarjeta
 * y en el panel de detalle
 */
@Component({
  selector: 'app-insignia-tipo',
  templateUrl: './insignia-tipo.html',
  host: { class: 'insignia bg-surface-container-high text-on-surface' },
})
export class InsigniaTipo {
  readonly tipo = input.required<TipoRecordatorio>();

  protected readonly etiqueta = computed(() => ETIQUETAS_TIPO_RECORDATORIO[this.tipo()]);
}
