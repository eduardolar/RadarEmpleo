import { Component, DestroyRef, ElementRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialog,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { filter } from 'rxjs';
import {
  confirmarSalirSinGuardar,
  intentosDeCerrar,
} from '../../../shared/components/dialogo-confirmacion/dialogo-confirmacion';
import { mensajeError, obligatorio, textoOpcional } from '../../../shared/utils/validadores';
import {
  CambiarEstado,
  EstadoCandidatura,
  MotivoDescarte,
} from '../../interfaces/candidatura.interface';
import {
  CAMPO_COMENTARIO,
  CAMPO_MOTIVO,
  CAMPO_MOTIVO_TEXTO,
  MAXIMO_COMENTARIO,
  MAXIMO_MOTIVO_TEXTO,
} from '../../utils/campos-candidatura';
import {
  ETIQUETAS_ESTADO,
  ETIQUETAS_MOTIVO_DESCARTE,
  opciones,
  textoAccionEstado,
} from '../../utils/etiquetas';
import { esEstadoFinal } from '../../utils/transiciones-permitidas';

export interface DatosCambioEstado {
  empresa: string;
  puesto: string;
  actual: EstadoCandidatura;
  destino: EstadoCandidatura;
}

/**
 * Confirma un cambio de estado con un comentario opcional. Se usa para Descartar y Cerrar, que sacan
 * la candidatura del proceso (los avances van en un toque). Si el destino es Descartada, pide además
 * el motivo (obligatorio, como en la API; todos a la vista para elegir de un toque) y un texto
 * opcional. Se cierra con la petición lista para mandar o sin nada si se cancela. Si hay algo escrito
 * o elegido, Cancelar, Esc o un clic fuera preguntan antes de cerrar para no perderlo.
 */
@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
  ],
  selector: 'app-dialogo-cambio-estado',
  templateUrl: './dialogo-cambio-estado.html',
})
export class DialogoCambioEstado {
  protected readonly datos = inject<DatosCambioEstado>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<DialogoCambioEstado, CambiarEstado>>(MatDialogRef);
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly descartando = this.datos.destino === 'Descartada';
  /** Descartar o cerrar (no entre ellos): se explica qué pasa y cómo volver */
  protected readonly saleDelProceso =
    esEstadoFinal(this.datos.destino) && !esEstadoFinal(this.datos.actual);
  protected readonly titulo = textoAccionEstado(this.datos.destino, this.datos.actual);
  protected readonly etiquetaActual = ETIQUETAS_ESTADO[this.datos.actual];
  protected readonly etiquetaDestino = ETIQUETAS_ESTADO[this.datos.destino];
  protected readonly motivos = opciones(ETIQUETAS_MOTIVO_DESCARTE);

  protected readonly campos = {
    comentario: CAMPO_COMENTARIO,
    motivo: CAMPO_MOTIVO,
    motivoTexto: CAMPO_MOTIVO_TEXTO,
  };
  protected readonly mensajeError = mensajeError;

  protected readonly formulario = new FormGroup({
    motivoDescarte: new FormControl<MotivoDescarte | null>(
      null,
      this.descartando ? [obligatorio] : [],
    ),
    motivoDescarteTexto: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAXIMO_MOTIVO_TEXTO)],
    }),
    comentario: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAXIMO_COMENTARIO)],
    }),
  });

  constructor() {
    // Se abre con disableClose: Esc y el clic fuera pasan por aquí
    intentosDeCerrar(this.dialogRef)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.cerrarSinGuardar());
  }

  protected cerrarSinGuardar(): void {
    if (!this.formulario.dirty) {
      this.dialogRef.close();
      return;
    }
    confirmarSalirSinGuardar(this.dialog)
      .pipe(filter(Boolean), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.dialogRef.close());
  }

  protected confirmar(): void {
    if (this.formulario.invalid) {
      this.formulario.markAllAsTouched();
      // El aviso de error ya lo lee el lector de pantalla; el foco, a donde hay que corregir
      this.elemento.nativeElement
        .querySelector<HTMLElement>('mat-radio-group.ng-invalid input, textarea.ng-invalid')
        ?.focus();
      return;
    }

    const valor = this.formulario.getRawValue();
    const peticion: CambiarEstado = {
      estado: this.datos.destino,
      comentario: textoOpcional(valor.comentario),
    };
    if (this.descartando) {
      peticion.motivoDescarte = valor.motivoDescarte;
      peticion.motivoDescarteTexto = textoOpcional(valor.motivoDescarteTexto);
    }
    this.dialogRef.close(peticion);
  }
}
