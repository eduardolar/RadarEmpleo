import { Component, DestroyRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialog,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { Router } from '@angular/router';
import { filter, Observable } from 'rxjs';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import {
  confirmarSalirSinGuardar,
  intentosDeCerrar,
} from '../../../shared/components/dialogo-confirmacion/dialogo-confirmacion';
import { AvisosService } from '../../../shared/services/avisos.service';
import { CandidaturaDetalle, GuardarCandidatura } from '../../interfaces/candidatura.interface';
import { CandidaturasService } from '../../services/candidaturas.service';
import { FormularioCandidatura } from '../formulario-candidatura/formulario-candidatura';

export interface DatosDialogoCandidatura {
  /** La candidatura a editar; sin ella, es un alta */
  candidatura: CandidaturaDetalle | null;
}

/**
 * Alta o edición de una candidatura en un diálogo. Se cierra con la ficha guardada. En el alta solo
 * pide lo básico (y «Añadir más datos» despliega el resto); al editar enseña todo.
 * Si hay algo escrito, Cancelar, Esc o un clic fuera preguntan antes de cerrar para no perderlo.
 */
@Component({
  imports: [MatDialogModule, MatButtonModule, FormularioCandidatura],
  selector: 'app-dialogo-candidatura',
  templateUrl: './dialogo-candidatura.html',
})
export class DialogoCandidatura {
  private readonly candidaturasService = inject(CandidaturasService);
  private readonly avisos = inject(AvisosService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialogRef =
    inject<MatDialogRef<DialogoCandidatura, CandidaturaDetalle>>(MatDialogRef);
  private readonly formulario = viewChild.required(FormularioCandidatura);

  protected readonly candidatura = inject<DatosDialogoCandidatura>(MAT_DIALOG_DATA).candidatura;
  protected readonly textoGuardar = this.candidatura ? 'Guardar cambios' : 'Crear candidatura';
  protected readonly guardando = signal(false);
  protected readonly erroresServidor = signal<Record<string, string[]>>({});

  constructor() {
    // Se abre con disableClose: Esc y el clic fuera pasan por aquí
    intentosDeCerrar(this.dialogRef)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.cerrarSinGuardar());
  }

  protected guardar(datos: GuardarCandidatura): void {
    this.guardando.set(true);
    this.erroresServidor.set({});

    const peticion = this.candidatura
      ? this.candidaturasService.editar(this.candidatura.id, datos)
      : this.candidaturasService.crear(datos);

    // Sin cancelar al destruirse: una navegación cierra el diálogo y no debe perder lo que se guarda
    peticion.subscribe({
      next: (detalle) => {
        if (this.candidatura) {
          this.avisos.exito('Cambios guardados.');
        } else {
          this.avisos.exito('Candidatura creada en «Detectada».', {
            texto: 'Ver ficha',
            alPulsar: () => void this.router.navigate(['/candidaturas', detalle.id]),
          });
        }
        if (!this.destroyRef.destroyed) {
          this.dialogRef.close(detalle);
        }
      },
      error: (error: ErrorApi) => {
        this.guardando.set(false);
        if (Object.keys(error.errores).length > 0 && !this.destroyRef.destroyed) {
          this.erroresServidor.set(error.errores);
        } else {
          this.avisos.error(error.mensaje);
        }
      },
    });
  }

  /** Cancelar, Esc o clic fuera: cierra si no hay nada escrito; si lo hay, pregunta antes */
  protected cerrarSinGuardar(): void {
    // Mientras guarda no se cierra: se perdería la respuesta
    if (this.guardando()) {
      return;
    }
    if (!this.formulario().tieneCambios()) {
      this.dialogRef.close();
      return;
    }
    confirmarSalirSinGuardar(this.dialog)
      .pipe(filter(Boolean), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.dialogRef.close());
  }
}

/** Abre el alta (sin candidatura) o la edición. Emite la ficha guardada o undefined si se cancela */
export function abrirDialogoCandidatura(
  dialog: MatDialog,
  candidatura: CandidaturaDetalle | null = null,
): Observable<CandidaturaDetalle | undefined> {
  return dialog
    .open<DialogoCandidatura, DatosDialogoCandidatura, CandidaturaDetalle>(DialogoCandidatura, {
      data: { candidatura },
      // Alta: se empieza pegando el enlace. Al editar no se sabe qué campo toca, y en el móvil
      // poner el foco en uno sacaría el teclado tapando medio formulario
      autoFocus: candidatura ? 'first-heading' : 'first-tabbable',
      width: '44rem',
      maxWidth: '95vw',
      disableClose: true,
    })
    .afterClosed();
}
