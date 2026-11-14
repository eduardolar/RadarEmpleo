import { Component, computed, DestroyRef, ElementRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
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
import { filter, Observable } from 'rxjs';
import {
  Recordatorio,
  TipoRecordatorio,
} from '../../../candidaturas/interfaces/candidatura.interface';
import { ETIQUETAS_TIPO_RECORDATORIO, opciones } from '../../../candidaturas/utils/etiquetas';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import { CandidaturaBreve } from '../../../hoy/interfaces/panel-hoy.interface';
import {
  confirmarSalirSinGuardar,
  intentosDeCerrar,
} from '../../../shared/components/dialogo-confirmacion/dialogo-confirmacion';
import { AvisosService } from '../../../shared/services/avisos.service';
import { FechaActualService } from '../../../shared/services/fecha-actual.service';
import {
  diaCorto,
  diaLargo,
  diaMadrid,
  horaMadrid,
  instanteMadrid,
} from '../../../shared/utils/fechas-madrid';
import { ESCRITORIO } from '../../../shared/utils/pantalla';
import {
  aplicarErroresServidor,
  mensajeError,
  obligatorio,
  textoOpcional,
} from '../../../shared/utils/validadores';
import { GuardarRecordatorio } from '../../interfaces/recordatorio.interface';
import { RecordatoriosService } from '../../services/recordatorios.service';
import {
  CAMPOS_RECORDATORIO,
  HORA_POR_DEFECTO,
  MAXIMO_NOTAS_RECORDATORIO,
  MENSAJE_HORA_INEXISTENTE,
  MAXIMO_TITULO_RECORDATORIO,
} from '../../utils/campos-recordatorio';
import { diasRapidos } from '../../utils/dias-rapidos';
import { tituloPorDefecto } from '../../utils/titulo-por-defecto';

/** Un día completo, como lo da el campo de fecha: «2026-11-17» */
const DIA_COMPLETO = /^\d{4}-\d{2}-\d{2}$/;
/** Una hora completa, como la da el campo de hora: «09:30» */
const HORA_COMPLETA = /^\d{2}:\d{2}$/;

export interface DatosDialogoRecordatorio {
  /** El recordatorio a editar; sin él, es uno nuevo */
  recordatorio?: Recordatorio;
  /** La candidatura a la que pertenece (o va a pertenecer); sin ella, es suelto */
  candidatura?: CandidaturaBreve | null;
  /** Tipo con el que empieza uno nuevo */
  tipoInicial?: TipoRecordatorio;
}

/**
 * Apunta o edita un recordatorio. Uno nuevo viene con el tipo y el título ya puestos: solo hay que
 * elegir el día, con el calendario o con un atajo («Hoy», «Mañana», «En una semana»). Debajo del día
 * sale en qué cae («martes 17 de noviembre») y, si ya ha pasado, se avisa. El título sigue al tipo
 * mientras no se haya escrito otro. El día y la hora son de Madrid. La hora es opcional (sin ella, las
 * 09:00) salvo en una entrevista, que la exige y empieza vacía. Si hay algo escrito, Cancelar, Esc o
 * un clic fuera preguntan antes de cerrar.
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
  selector: 'app-dialogo-recordatorio',
  templateUrl: './dialogo-recordatorio.html',
})
export class DialogoRecordatorio {
  private readonly datos = inject<DatosDialogoRecordatorio>(MAT_DIALOG_DATA);
  private readonly recordatoriosService = inject(RecordatoriosService);
  private readonly avisos = inject(AvisosService);
  private readonly fechaActual = inject(FechaActualService);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly dialogRef =
    inject<MatDialogRef<DialogoRecordatorio, Recordatorio>>(MatDialogRef);

  protected readonly recordatorio = this.datos.recordatorio ?? null;
  protected readonly candidatura = this.datos.candidatura ?? null;
  protected readonly tipos = opciones(ETIQUETAS_TIPO_RECORDATORIO);
  protected readonly campos = CAMPOS_RECORDATORIO;
  protected readonly mensajeError = mensajeError;
  protected readonly guardando = signal(false);

  private readonly tipoInicial: TipoRecordatorio =
    this.recordatorio?.tipo ?? this.datos.tipoInicial ?? 'Otro';

  protected readonly formulario = new FormGroup({
    tipo: new FormControl<TipoRecordatorio>(this.tipoInicial, {
      nonNullable: true,
      validators: [obligatorio],
    }),
    titulo: new FormControl(
      this.recordatorio?.titulo ?? tituloPorDefecto(this.tipoInicial, this.candidatura),
      {
        nonNullable: true,
        validators: [obligatorio, Validators.maxLength(MAXIMO_TITULO_RECORDATORIO)],
      },
    ),
    dia: new FormControl(this.recordatorio ? diaMadrid(this.recordatorio.fechaHora) : '', {
      nonNullable: true,
      validators: [obligatorio],
    }),
    // Una entrevista nueva empieza sin hora: hay que ponerla. Una ya guardada conserva la suya,
    // aunque sean las 09:00 de cuando no era obligatoria
    hora: new FormControl(
      this.recordatorio ? horaMadrid(this.recordatorio.fechaHora) : horaInicial(this.tipoInicial),
      { nonNullable: true, validators: [horaDeEntrevista, horaQueExiste] },
    ),
    notas: new FormControl(this.recordatorio?.notas ?? '', {
      nonNullable: true,
      validators: [Validators.maxLength(MAXIMO_NOTAS_RECORDATORIO)],
    }),
  });

  /** El día de Madrid de hoy: de él salen los atajos y el aviso de «ya ha pasado» */
  private readonly hoy = computed(() => diaMadrid(this.fechaActual.ahora()));
  protected readonly diasRapidos = computed(() => diasRapidos(this.hoy()));

  protected readonly diaElegido = toSignal(this.formulario.controls.dia.valueChanges, {
    initialValue: this.formulario.controls.dia.value,
  });
  protected readonly tipoElegido = toSignal(this.formulario.controls.tipo.valueChanges, {
    initialValue: this.formulario.controls.tipo.value,
  });

  /**
   * Lo que se lee debajo del día: en qué cae («Martes 17 de noviembre», con el año si no es este), para
   * comprobarlo de un vistazo, y el aviso si ya ha pasado. Vacío mientras no haya un día completo
   */
  protected readonly ayudaDia = computed(() => {
    const dia = this.diaElegido();
    if (!DIA_COMPLETO.test(dia)) {
      return '';
    }
    const largo = diaLargo(dia);
    if (largo === '') {
      return '';
    }
    const anio = dia.slice(0, 4);
    const texto =
      largo.charAt(0).toUpperCase() +
      largo.slice(1) +
      (anio === this.hoy().slice(0, 4) ? '' : ` de ${anio}`);
    // En uno ya hecho, que el día haya pasado es lo normal
    return dia < this.hoy() && !this.recordatorio?.hecho
      ? `${texto}: ya ha pasado, saldrá como vencido.`
      : `${texto}.`;
  });

  /** Una entrevista a las 09:00 por no mirar la hora es el error que más cuesta: ahí es obligatoria */
  protected readonly horaObligatoria = computed(() => this.tipoElegido() === 'Entrevista');

  protected readonly ayudaHora = computed(() =>
    this.horaObligatoria() ? 'Obligatoria: es una entrevista.' : 'Si la dejas vacía, a las 09:00.',
  );

  constructor() {
    // Se abre con disableClose: Esc y el clic fuera pasan por aquí
    intentosDeCerrar(this.dialogRef)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.cerrarSinGuardar());

    // En uno nuevo, el título acompaña al tipo hasta que se escribe otro a mano
    const { tipo, titulo, hora, dia } = this.formulario.controls;
    // El validador de la hora mira el tipo, que al crearse el control aún no era su hermano
    hora.updateValueAndValidity();
    tipo.valueChanges.pipe(takeUntilDestroyed()).subscribe((nuevo) => {
      if (!this.recordatorio && titulo.pristine) {
        titulo.setValue(tituloPorDefecto(nuevo, this.candidatura));
      }
      // Y la hora que no se ha tocado: las 09:00 puestas solas no valen para una entrevista, así que
      // se vacía; al volver a otro tipo, vuelven. La que ha escrito el usuario se respeta, y la de
      // uno ya guardado también
      if (!this.recordatorio && hora.pristine) {
        hora.setValue(horaInicial(nuevo));
      }
      // Que la hora sea obligatoria depende del tipo
      hora.updateValueAndValidity();
    });

    // Que la hora exista depende del día: se avisa antes de mandarlo, aunque no se haya tocado la hora
    dia.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      hora.updateValueAndValidity();
      if (hora.hasError('horaInexistente')) {
        hora.markAsTouched();
      }
    });
  }

  protected guardar(): void {
    if (this.guardando()) {
      return;
    }
    if (this.formulario.invalid) {
      this.formulario.markAllAsTouched();
      // El aviso de error ya lo lee el lector de pantalla; el foco, a donde hay que corregir
      this.elemento.nativeElement
        .querySelector<HTMLElement>('input.ng-invalid, textarea.ng-invalid')
        ?.focus();
      return;
    }

    const valor = this.formulario.getRawValue();
    const datos: GuardarRecordatorio = {
      tipo: valor.tipo,
      titulo: valor.titulo.trim(),
      dia: valor.dia,
      hora: textoOpcional(valor.hora),
      notas: textoOpcional(valor.notas),
    };
    const peticion = this.recordatorio
      ? this.recordatoriosService.editar(this.recordatorio, datos)
      : this.recordatoriosService.crear(datos, this.candidatura?.id);

    // Sin cancelar al destruirse: una navegación cierra el diálogo y no debe perder lo que se guarda
    this.guardando.set(true);
    peticion.subscribe({
      next: (guardado) => {
        this.avisos.exito(
          `Recordatorio guardado para el ${diaCorto(guardado.fechaHora)} a las ${horaMadrid(guardado.fechaHora)}.`,
        );
        if (!this.destroyRef.destroyed) {
          this.dialogRef.close(guardado);
        }
      },
      error: (error: ErrorApi) => {
        this.guardando.set(false);
        const sueltos = aplicarErroresServidor(this.formulario, error.errores);
        const sinCampo = Object.keys(error.errores).length === 0 || sueltos.length > 0;
        if (sinCampo || this.destroyRef.destroyed) {
          this.avisos.error(error.mensaje);
        }
      },
      // Otra operación sobre el mismo recordatorio seguía en marcha y esta no se ha mandado
      complete: () => this.guardando.set(false),
    });
  }

  /** Un atajo de día: lo pone en el campo como si se hubiera elegido en el calendario */
  protected ponerDia(dia: string): void {
    const control = this.formulario.controls.dia;
    control.setValue(dia);
    control.markAsDirty();
    control.markAsTouched();
  }

  /** Cancelar, Esc o clic fuera: cierra si no hay nada escrito; si lo hay, pregunta antes */
  protected cerrarSinGuardar(): void {
    // Mientras guarda no se cierra: se perdería la respuesta
    if (this.guardando()) {
      return;
    }
    if (!this.formulario.dirty) {
      this.dialogRef.close();
      return;
    }
    confirmarSalirSinGuardar(this.dialog)
      .pipe(filter(Boolean), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.dialogRef.close());
  }
}

/** La hora con la que empieza uno nuevo: vacía en una entrevista (hay que ponerla) y las 09:00 en lo demás */
function horaInicial(tipo: TipoRecordatorio): string {
  return tipo === 'Entrevista' ? '' : HORA_POR_DEFECTO;
}

/** La hora solo es obligatoria si el tipo (su hermano en el formulario) es «Entrevista», como en la API */
const horaDeEntrevista: ValidatorFn = (control: AbstractControl): ValidationErrors | null =>
  control.parent?.get('tipo')?.value === 'Entrevista' ? obligatorio(control) : null;

/**
 * La hora tiene que existir ese día en Madrid: la noche de marzo en que se adelanta el reloj, de las
 * 02:00 a las 02:59 no hay nada. Lo mismo que comprueba la API, para avisar antes de mandarlo
 */
const horaQueExiste: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const hora: unknown = control.value;
  const dia: unknown = control.parent?.get('dia')?.value;
  if (typeof hora !== 'string' || !HORA_COMPLETA.test(hora)) {
    return null;
  }
  if (typeof dia !== 'string' || !DIA_COMPLETO.test(dia)) {
    return null;
  }
  return instanteMadrid(dia, hora) === null ? { horaInexistente: MENSAJE_HORA_INEXISTENTE } : null;
};

/** Abre el alta o la edición. Emite el recordatorio guardado o undefined si se cancela */
export function abrirDialogoRecordatorio(
  dialog: MatDialog,
  datos: DatosDialogoRecordatorio,
): Observable<Recordatorio | undefined> {
  return dialog
    .open<DialogoRecordatorio, DatosDialogoRecordatorio, Recordatorio>(DialogoRecordatorio, {
      data: datos,
      // Con teclado, el foco va al día: es lo único que falta en uno nuevo y lo que más se cambia en
      // uno apuntado, así que se escribe la fecha y se guarda con Intro. En el móvil se queda en el
      // título del diálogo: en un campo, el teclado taparía medio diálogo
      autoFocus: window.matchMedia?.(ESCRITORIO).matches ? 'input[type="date"]' : 'first-heading',
      width: '34rem',
      maxWidth: '95vw',
      disableClose: true,
    })
    .afterClosed();
}
