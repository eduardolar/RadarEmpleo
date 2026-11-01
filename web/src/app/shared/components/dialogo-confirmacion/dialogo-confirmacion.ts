import { hasModifierKey } from '@angular/cdk/keycodes';
import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialog,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { filter, map, merge, Observable, tap } from 'rxjs';

export interface DatosConfirmacion {
  titulo: string;
  mensaje: string;
  /** Texto del botón que confirma («Borrar», «Descartar»...) */
  confirmar: string;
}

const ID_MENSAJE = 'dialogo-confirmacion-mensaje';

// El segundo clic de un doble clic sobre el botón que abre un diálogo cae ya en el fondo oscuro
const MARGEN_DOBLE_CLIC_MS = 500;

/** Pregunta antes de hacer algo que no se puede deshacer (en vez de window.confirm) */
@Component({
  imports: [MatDialogModule, MatButtonModule],
  selector: 'app-dialogo-confirmacion',
  templateUrl: './dialogo-confirmacion.html',
})
export class DialogoConfirmacion {
  protected readonly datos = inject<DatosConfirmacion>(MAT_DIALOG_DATA);
  protected readonly idMensaje = ID_MENSAJE;
}

/** Abre la confirmación y emite true solo si se pulsa el botón de confirmar */
export function confirmar(dialog: MatDialog, datos: DatosConfirmacion): Observable<boolean> {
  return dialog
    .open<DialogoConfirmacion, DatosConfirmacion, boolean>(DialogoConfirmacion, {
      data: datos,
      width: '28rem',
      maxWidth: '95vw',
      role: 'alertdialog',
      // Un alertdialog tiene que leer su mensaje al abrirse, no solo el título
      ariaDescribedBy: ID_MENSAJE,
    })
    .afterClosed()
    .pipe(map((resultado) => resultado === true));
}

/** La misma pregunta en todos los sitios donde se puede perder lo escrito en un formulario */
export function confirmarSalirSinGuardar(dialog: MatDialog): Observable<boolean> {
  return confirmar(dialog, {
    titulo: '¿Salir sin guardar?',
    mensaje: 'Lo que has escrito en el formulario se perderá.',
    confirmar: 'Salir sin guardar',
  });
}

/**
 * Para los diálogos abiertos con disableClose: emite al pulsar Esc o hacer clic fuera, para decidir
 * si se cierran. Hay que llamarla al crear el diálogo: el margen del doble clic cuenta desde ahí.
 */
export function intentosDeCerrar(
  dialogRef: Pick<MatDialogRef<unknown>, 'backdropClick' | 'keydownEvents'>,
): Observable<Event> {
  const abierto = Date.now();
  return merge(
    dialogRef.backdropClick().pipe(filter(() => Date.now() - abierto >= MARGEN_DOBLE_CLIC_MS)),
    dialogRef
      .keydownEvents()
      .pipe(filter((evento) => evento.key === 'Escape' && !hasModifierKey(evento))),
  ).pipe(tap((evento) => evento.preventDefault()));
}
