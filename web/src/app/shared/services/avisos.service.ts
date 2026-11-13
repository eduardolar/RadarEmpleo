import { inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AvisoDeshacer, DatosAvisoDeshacer } from '../components/aviso-deshacer/aviso-deshacer';

/** Lo que dura a la vista un aviso con «Deshacer»: da tiempo a ver el cambio y arrepentirse */
export const DURACION_DESHACER_MS = 8000;

/** Botón opcional de un aviso: «Añadir nota», «Ver ficha»... */
export interface AccionAviso {
  texto: string;
  alPulsar: () => void;
}

/** Avisos cortos abajo de la pantalla (snackbar de Material). El snackbar ya los anuncia al lector de pantalla */
@Injectable({ providedIn: 'root' })
export class AvisosService {
  private readonly snackBar = inject(MatSnackBar);

  /** Con acción dura más: da tiempo a leerlo y a pulsar el botón */
  exito(mensaje: string, accion?: AccionAviso): void {
    if (!accion) {
      this.snackBar.open(mensaje, undefined, { duration: 3000 });
      return;
    }
    this.snackBar
      .open(mensaje, accion.texto, { duration: 6000 })
      .onAction()
      .subscribe(() => accion.alPulsar());
  }

  /**
   * Aviso de un cambio con el botón «Deshacer» (y Ctrl+Z mientras se ve) y, si se pasa, otra acción.
   * Si `deshacer` devuelve false, el aviso sigue abierto y pide esperar
   */
  deshacible(mensaje: string, deshacer: () => boolean, otra?: AccionAviso): void {
    this.snackBar.openFromComponent<AvisoDeshacer, DatosAvisoDeshacer>(AvisoDeshacer, {
      data: { mensaje, deshacer, otra },
      duration: DURACION_DESHACER_MS,
    });
  }

  /** Los errores se quedan más tiempo y se pueden cerrar: suelen ser largos */
  error(mensaje: string): void {
    this.snackBar.open(mensaje, 'Cerrar', {
      duration: 8000,
      panelClass: 'aviso-error',
      politeness: 'assertive',
    });
  }
}
