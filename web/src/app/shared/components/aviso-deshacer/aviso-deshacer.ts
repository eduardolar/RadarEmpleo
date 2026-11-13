import { Component, ElementRef, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MAT_SNACK_BAR_DATA, MatSnackBarModule, MatSnackBarRef } from '@angular/material/snack-bar';
import type { AccionAviso } from '../../services/avisos.service';
import { esCampoDeTexto } from '../../utils/teclado';

export interface DatosAvisoDeshacer {
  mensaje: string;
  /** Devuelve false si ahora no se puede (hay otro cambio de esa candidatura en marcha) */
  deshacer: () => boolean;
  /** Otra acción opcional, después de «Deshacer» */
  otra?: AccionAviso;
}

/**
 * Aviso (snackbar) de un cambio que se puede deshacer. Además del botón, mientras está a la vista
 * Ctrl+Z (Cmd+Z en el Mac) deshace, salvo con el foco en un campo de texto o un desplegable (ahí el
 * atajo es suyo) o con un diálogo u otro panel flotante abierto. Una sola acción por aviso: el segundo
 * Ctrl+Z o el clic que llega detrás no mandan otra petición. El snackbar de Material lo anuncia al
 * lector de pantalla igual que los avisos sencillos.
 */
@Component({
  imports: [MatButtonModule, MatSnackBarModule],
  selector: 'app-aviso-deshacer',
  templateUrl: './aviso-deshacer.html',
  host: { '(document:keydown)': 'alPulsarTecla($event)' },
})
export class AvisoDeshacer {
  protected readonly datos = inject<DatosAvisoDeshacer>(MAT_SNACK_BAR_DATA);
  private readonly referencia = inject<MatSnackBarRef<AvisoDeshacer>>(MatSnackBarRef);
  private readonly dialog = inject(MatDialog);
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Ya se ha pulsado una acción: el aviso se está cerrando y no admite otra */
  private usado = false;

  /** Se ha pedido deshacer con otro cambio de la candidatura en marcha: hay que esperar */
  protected readonly esperando = signal(false);

  protected deshacer(): void {
    if (this.usado) {
      return;
    }
    if (!this.datos.deshacer()) {
      this.esperando.set(true);
      return;
    }
    this.usado = true;
    this.referencia.dismissWithAction();
  }

  protected pulsarOtra(accion: AccionAviso): void {
    if (this.usado) {
      return;
    }
    this.usado = true;
    this.referencia.dismissWithAction();
    accion.alPulsar();
  }

  protected alPulsarTecla(evento: KeyboardEvent): void {
    const atajo =
      (evento.ctrlKey || evento.metaKey) &&
      !evento.shiftKey &&
      !evento.altKey &&
      evento.key.toLowerCase() === 'z';
    if (!atajo || evento.repeat || evento.defaultPrevented || !this.atajoLibre(evento.target)) {
      return;
    }
    evento.preventDefault();
    this.deshacer();
  }

  /** Ni escribiendo, ni con un diálogo abierto, ni desde otro panel flotante (un menú, un desplegable) */
  private atajoLibre(destino: EventTarget | null): boolean {
    if (esCampoDeTexto(destino) || this.dialog.openDialogs.length > 0) {
      return false;
    }
    const enPanel = destino instanceof Element && destino.closest('.cdk-overlay-pane') !== null;
    return !enPanel || this.elemento.nativeElement.contains(destino as Node);
  }
}
