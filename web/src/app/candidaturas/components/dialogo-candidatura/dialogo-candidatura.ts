import {
  afterNextRender,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
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
 * Si al editar la candidatura ha cambiado desde que se abrió (409), lo dice aquí mismo con «Recargar la ficha».
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
  /** El 409 al editar: la candidatura ha cambiado desde que se abrió (otra pestaña, un análisis de Claude) */
  protected readonly conflicto = signal<string | null>(null);
  private readonly avisoConflicto = viewChild<ElementRef<HTMLElement>>('avisoConflicto');
  private readonly injector = inject(Injector);

  constructor() {
    // Se abre con disableClose: Esc y el clic fuera pasan por aquí
    intentosDeCerrar(this.dialogRef)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.cerrarSinGuardar());
  }

  protected guardar(datos: GuardarCandidatura): void {
    this.guardando.set(true);
    this.erroresServidor.set({});
    this.conflicto.set(null);

    // Al editar va la versión que se abrió: si ha cambiado desde entonces, la API no pisa nada (409)
    const peticion = this.candidatura
      ? this.candidaturasService.editar(this.candidatura.id, {
          ...datos,
          fechaUltimoCambioLeida: this.candidatura.fechaUltimoCambio,
        })
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
        if (this.destroyRef.destroyed) {
          this.avisos.error(error.mensaje);
        } else if (Object.keys(error.errores).length > 0) {
          this.erroresServidor.set(error.errores);
        } else if (this.candidatura && error.estado === 409) {
          // Se queda abierto con lo escrito, para poder copiarlo antes de recargar
          this.conflicto.set(error.mensaje);
          afterNextRender(() => this.avisoConflicto()?.nativeElement.focus(), {
            injector: this.injector,
          });
        } else {
          this.avisos.error(error.mensaje);
        }
      },
    });
  }

  /** Tras el 409: cierra sin guardar (lo escrito se pierde, el aviso lo dice) y vuelve a leer la ficha */
  protected recargar(): void {
    if (this.candidatura) {
      this.candidaturasService.cargarDetalle(this.candidatura.id);
    }
    this.dialogRef.close();
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
