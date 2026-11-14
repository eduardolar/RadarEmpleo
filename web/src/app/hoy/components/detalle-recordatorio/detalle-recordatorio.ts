import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  output,
  untracked,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { CandidaturasService } from '../../../candidaturas/services/candidaturas.service';
import { ETIQUETAS_ESTADO } from '../../../candidaturas/utils/etiquetas';
import { esEstadoFinal } from '../../../candidaturas/utils/transiciones-permitidas';
import { InsigniaTipo } from '../../../recordatorios/components/insignia-tipo/insignia-tipo';
import { FechaMadridPipe } from '../../../shared/pipes/fecha-madrid.pipe';
import { RecordatorioAgenda } from '../../interfaces/panel-hoy.interface';

/**
 * El detalle de un recordatorio de Hoy, para el panel de al lado: qué es y cuándo toca y, si es de una
 * candidatura, sus notas, el enlace a la oferta y el historial de estados, que salen de la ficha
 * (`CandidaturasService.detalle`). No cambia nada: avisa a la página para volver al resumen, marcarlo
 * como hecho, editarlo o apuntar otro para la misma candidatura.
 */
@Component({
  imports: [RouterLink, MatButtonModule, MatProgressBarModule, FechaMadridPipe, InsigniaTipo],
  selector: 'app-detalle-recordatorio',
  templateUrl: './detalle-recordatorio.html',
  host: { class: 'block' },
})
export class DetalleRecordatorio {
  private readonly candidaturasService = inject(CandidaturasService);
  private readonly titulo = viewChild.required<ElementRef<HTMLElement>>('titulo');

  readonly recordatorio = input.required<RecordatorioAgenda>();

  readonly volver = output<void>();
  readonly editar = output<void>();
  /** «Hecho», sin salir del panel: lo manda la página, con su «Deshacer» */
  readonly hecho = output<void>();
  /** Apuntar otro recordatorio para la misma candidatura, sin pasar por su ficha */
  readonly anadir = output<void>();

  protected readonly etiquetasEstado = ETIQUETAS_ESTADO;

  /** Su candidatura sigue en marcha (ni descartada ni cerrada): se le pueden apuntar más recordatorios */
  protected readonly activa = computed(() => {
    const estado = this.recordatorio().candidatura?.estado;
    return estado !== undefined && !esEstadoFinal(estado);
  });

  private readonly candidaturaId = computed(() => this.recordatorio().candidatura?.id ?? null);

  /** La ficha de su candidatura, cuando ya ha llegado (la del servicio puede ser todavía la de otra) */
  protected readonly ficha = computed(() => {
    const detalle = this.candidaturasService.detalle();
    return detalle !== null && detalle.id === this.candidaturaId() ? detalle : null;
  });

  /** El historial, de lo más reciente a lo más antiguo */
  protected readonly historial = computed(() => [...(this.ficha()?.historial ?? [])].reverse());

  protected readonly cargando = computed(
    () => this.ficha() === null && this.candidaturasService.cargandoDetalle(),
  );

  protected readonly error = computed(() =>
    this.ficha() === null ? (this.candidaturasService.errorDetalle()?.mensaje ?? null) : null,
  );

  constructor() {
    // Solo al cambiar de candidatura: pasar a otro recordatorio de la misma no vuelve a pedirla
    effect(() => {
      const id = this.candidaturaId();
      if (id !== null) {
        untracked(() => this.candidaturasService.cargarDetalle(id));
      }
    });
  }

  /** Lleva el foco al encabezado, para que el lector de pantalla empiece por lo que se acaba de abrir */
  enfocarTitulo(): void {
    this.titulo().nativeElement.focus();
  }

  protected reintentar(): void {
    const id = this.candidaturaId();
    if (id !== null) {
      this.candidaturasService.cargarDetalle(id);
      // El botón desaparece y el foco caería en <body>
      this.enfocarTitulo();
    }
  }
}
