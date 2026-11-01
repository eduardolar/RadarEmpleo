import { Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FechaActualService } from '../../../shared/services/fecha-actual.service';
import { CandidaturaResumen, EstadoCandidatura } from '../../interfaces/candidatura.interface';
import { diasEnEstado, textoDiasEnEstado } from '../../utils/dias-en-estado';
import { ETIQUETAS_ENCAJE, ETIQUETAS_ESTADO, ETIQUETAS_MODALIDAD } from '../../utils/etiquetas';
import { MenuMover } from '../menu-mover/menu-mover';

/** Tarjeta de una candidatura en el tablero (columna en el PC, lista en el móvil) */
@Component({
  imports: [RouterLink, MenuMover],
  selector: 'app-tarjeta-candidatura',
  templateUrl: './tarjeta-candidatura.html',
  host: { class: 'block', '[attr.data-candidatura]': 'candidatura().id' },
})
export class TarjetaCandidatura {
  private readonly fechaActual = inject(FechaActualService);

  readonly candidatura = input.required<CandidaturaResumen>();
  readonly mover = output<EstadoCandidatura>();

  protected readonly etiquetaEstado = computed(() => ETIQUETAS_ESTADO[this.candidatura().estado]);

  /** «Encaje: Alto», «Encaje: Sin valorar»... y «No encaja» a secas, que ya lo dice todo */
  protected readonly encaje = computed(() => {
    const encaje = this.candidatura().encaje;
    return encaje === 'No' ? ETIQUETAS_ENCAJE.No : `Encaje: ${ETIQUETAS_ENCAJE[encaje]}`;
  });

  protected readonly dias = computed(() =>
    textoDiasEnEstado(diasEnEstado(this.candidatura().fechaEstado, this.fechaActual.ahora())),
  );

  /** «Remoto · Madrid», sin la modalidad si no se indicó */
  protected readonly lugar = computed(() => {
    const c = this.candidatura();
    const modalidad = c.modalidad === 'NoIndicada' ? null : ETIQUETAS_MODALIDAD[c.modalidad];
    return [modalidad, c.ubicacion].filter(Boolean).join(' · ');
  });
}
