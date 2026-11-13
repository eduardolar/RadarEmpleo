import {
  afterNextRender,
  afterRenderEffect,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  numberAttribute,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import {
  FormControl,
  FormGroup,
  FormGroupDirective,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { filter, switchMap } from 'rxjs';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import { PanelDocumentos } from '../../../documentos/components/panel-documentos/panel-documentos';
import { confirmar } from '../../../shared/components/dialogo-confirmacion/dialogo-confirmacion';
import { EnlaceVolver } from '../../../shared/components/enlace-volver/enlace-volver';
import { FechaMadridPipe } from '../../../shared/pipes/fecha-madrid.pipe';
import { AvisosService } from '../../../shared/services/avisos.service';
import { FechaActualService } from '../../../shared/services/fecha-actual.service';
import {
  aplicarErroresServidor,
  mensajeError,
  obligatorio,
} from '../../../shared/utils/validadores';
import { abrirDialogoCandidatura } from '../../components/dialogo-candidatura/dialogo-candidatura';
import { EstadoCandidatura, Nota } from '../../interfaces/candidatura.interface';
import { CambioEstadoService } from '../../services/cambio-estado.service';
import { CandidaturasService } from '../../services/candidaturas.service';
import { CAMPO_NOTA, MAXIMO_NOTA } from '../../utils/campos-candidatura';
import { accionesEstado } from '../../utils/acciones-estado';
import { diasEnEstado, textoDiasEnEstado } from '../../utils/dias-en-estado';
import {
  ETIQUETAS_ENCAJE,
  ETIQUETAS_ESTADO,
  ETIQUETAS_MODALIDAD,
  ETIQUETAS_MOTIVO_DESCARTE,
  ETIQUETAS_ORIGEN,
  ETIQUETAS_TIPO_RECORDATORIO,
} from '../../utils/etiquetas';
import { lineaDeTiempo } from '../../utils/linea-de-tiempo';

@Component({
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatMenuModule,
    MatProgressBarModule,
    FechaMadridPipe,
    PanelDocumentos,
    EnlaceVolver,
  ],
  selector: 'app-ficha-page',
  templateUrl: './ficha-page.component.html',
})
export default class FichaPageComponent {
  private readonly candidaturasService = inject(CandidaturasService);
  private readonly cambioEstado = inject(CambioEstadoService);
  private readonly dialog = inject(MatDialog);
  private readonly avisos = inject(AvisosService);
  private readonly router = inject(Router);
  private readonly titulo = inject(Title);
  private readonly fechaActual = inject(FechaActualService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly inicio = viewChild.required<ElementRef<HTMLElement>>('inicio');
  private readonly estadoActual = viewChild<ElementRef<HTMLElement>>('estadoActual');
  private readonly bloqueAcciones = viewChild<ElementRef<HTMLElement>>('bloqueAcciones');
  private readonly elementoNota = viewChild<ElementRef<HTMLTextAreaElement>>('entradaNota');
  private readonly tituloHistorial = viewChild<ElementRef<HTMLElement>>('tituloHistorial');

  /** Viene de la ruta /candidaturas/:id (withComponentInputBinding) */
  readonly id = input.required({ transform: numberAttribute });

  protected readonly candidatura = this.candidaturasService.detalle;
  protected readonly cargando = this.candidaturasService.cargandoDetalle;
  protected readonly error = this.candidaturasService.errorDetalle;
  protected readonly cambiando = this.cambioEstado.cambiando;
  protected readonly bloqueado = this.cambioEstado.bloqueado;

  protected readonly etiquetas = {
    estado: ETIQUETAS_ESTADO,
    modalidad: ETIQUETAS_MODALIDAD,
    encaje: ETIQUETAS_ENCAJE,
    motivo: ETIQUETAS_MOTIVO_DESCARTE,
    origen: ETIQUETAS_ORIGEN,
    recordatorio: ETIQUETAS_TIPO_RECORDATORIO,
  };

  protected readonly diasEnEstado = computed(() => {
    const c = this.candidatura();
    return c
      ? textoDiasEnEstado(diasEnEstado(c.fechaEstado, this.fechaActual.ahora())).toLowerCase()
      : '';
  });

  private readonly acciones = computed(() => {
    const c = this.candidatura();
    return c ? accionesEstado(c.estado, c.estadosPosibles) : [];
  });
  /** Botones de cambio de estado: el siguiente paso primero y destacado, luego lo habitual */
  protected readonly accionesALaVista = computed(() =>
    this.acciones().filter((a) => !a.enMasOpciones),
  );
  protected readonly masOpciones = computed(() => this.acciones().filter((a) => a.enMasOpciones));

  protected readonly lineaDeTiempo = computed(() => {
    const c = this.candidatura();
    return c ? lineaDeTiempo(c.historial, c.notas) : [];
  });

  protected readonly formularioNota = new FormGroup({
    texto: new FormControl('', {
      nonNullable: true,
      validators: [obligatorio, Validators.maxLength(MAXIMO_NOTA)],
    }),
  });
  // resetForm() (y no reset()) quita también el «enviado»: si no, sale «obligatorio» tras guardar
  private readonly directivaNota = viewChild(FormGroupDirective);
  protected readonly guardandoNota = signal(false);
  protected readonly mensajeError = mensajeError;
  protected readonly campoNota = CAMPO_NOTA;

  // Estado que se estaba enseñando, para saber cuándo cambia en esta misma ficha
  private estadoVisto: { id: number; estado: EstadoCandidatura } | null = null;
  // El próximo cambio de estado es un «Deshacer» del aviso: el foco vuelve a los botones de estado
  private enfocarAcciones = false;

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.candidaturasService.cargarDetalle(id));
    });

    // Título de la pestaña con el puesto: ayuda a distinguir pestañas y lo lee el lector de pantalla
    effect(() => {
      const c = this.candidatura();
      if (c && c.id === this.id()) {
        this.titulo.setTitle(`${c.puesto} · ${c.empresa} · RadarEmpleo`);
      }
    });

    // Tras un cambio de estado, el botón pulsado desaparece y el foco caería en <body>: se lleva
    // al estado nuevo, que es lo que acaba de cambiar. Tras deshacerlo, al bloque «Cambiar estado»,
    // para seguir desde donde se estaba
    afterRenderEffect(() => {
      const c = this.candidatura();
      const visto = this.estadoVisto;
      this.estadoVisto = c ? { id: c.id, estado: c.estado } : null;
      if (c && visto?.id === c.id && visto.estado !== c.estado) {
        const acciones = this.enfocarAcciones ? this.bloqueAcciones() : undefined;
        this.enfocarAcciones = false;
        (acciones ?? this.estadoActual())?.nativeElement.focus();
      }
    });

    // «Añadir nota» en el aviso de un cambio de estado: foco en el campo de la nota, ya a la vista
    afterRenderEffect(() => {
      const pedida = this.cambioEstado.notaPedida();
      const campo = this.elementoNota()?.nativeElement;
      if (pedida === null || !campo || this.candidatura()?.id !== pedida) {
        return;
      }
      campo.focus({ preventScroll: true });
      // Sin animación: respeta a quien tiene reducido el movimiento (y jsdom no implementa scrollIntoView)
      campo.scrollIntoView?.({ block: 'center' });
      this.cambioEstado.notaAtendida();
    });
  }

  protected reintentar(): void {
    this.candidaturasService.cargarDetalle(this.id());
    // El botón desaparece y el foco caería en <body>
    this.inicio().nativeElement.focus({ preventScroll: true });
  }

  protected cambiarEstado(destino: EstadoCandidatura): void {
    const c = this.candidatura();
    // Los botones siguen recibiendo clics mientras están bloqueados (disabledInteractive, para no
    // perder el foco) y, si la API ya ha respondido, el del paso siguiente sale bajo el mismo cursor
    if (c && !this.bloqueado()) {
      this.cambioEstado.mover(c, destino, undefined, () => (this.enfocarAcciones = true));
    }
  }

  protected editar(): void {
    abrirDialogoCandidatura(this.dialog, this.candidatura()).subscribe();
  }

  protected borrar(): void {
    const c = this.candidatura();
    if (!c) {
      return;
    }
    confirmar(this.dialog, {
      titulo: 'Borrar candidatura',
      mensaje: `Se borrará «${c.puesto}» de ${c.empresa} con su historial, notas y recordatorios. No se puede deshacer.`,
      confirmar: 'Borrar',
    })
      .pipe(
        filter(Boolean),
        switchMap(() => this.candidaturasService.borrar(c.id)),
      )
      .subscribe({
        next: () => {
          this.avisos.exito('Candidatura borrada.');
          // Si ya se ha ido a otra página, no se le saca de ella
          if (!this.destroyRef.destroyed) {
            this.router.navigateByUrl('/tablero');
          }
        },
        error: (error: ErrorApi) => this.avisos.error(error.mensaje),
      });
  }

  /** También con Ctrl+Intro (o Cmd+Intro) desde el texto de la nota */
  protected anadirNota(evento?: Event): void {
    evento?.preventDefault();
    const c = this.candidatura();
    if (!c || this.guardandoNota()) {
      return;
    }
    if (this.formularioNota.invalid) {
      this.formularioNota.markAllAsTouched();
      return;
    }

    // Sin cancelar al destruirse: salir de la ficha no debe perder lo que ya se está guardando
    this.guardandoNota.set(true);
    this.candidaturasService
      .anadirNota(c.id, this.formularioNota.getRawValue().texto.trim())
      .subscribe({
        next: () => {
          this.guardandoNota.set(false);
          this.avisos.exito('Nota añadida.');
          if (!this.destroyRef.destroyed) {
            this.directivaNota()?.resetForm();
          }
        },
        error: (error: ErrorApi) => {
          this.guardandoNota.set(false);
          const sueltos = aplicarErroresServidor(this.formularioNota, error.errores);
          const sinCampo = Object.keys(error.errores).length === 0 || sueltos.length > 0;
          if (sinCampo || this.destroyRef.destroyed) {
            this.avisos.error(error.mensaje);
          }
        },
      });
  }

  protected borrarNota(nota: Nota): void {
    confirmar(this.dialog, {
      titulo: 'Borrar nota',
      mensaje: 'La nota se borrará y no se puede deshacer.',
      confirmar: 'Borrar',
    })
      .pipe(
        filter(Boolean),
        switchMap(() => this.candidaturasService.borrarNota(nota)),
      )
      .subscribe({
        next: () => {
          this.avisos.exito('Nota borrada.');
          this.enfocarHistorial();
        },
        error: (error: ErrorApi) => this.avisos.error(error.mensaje),
      });
  }

  /** El diálogo devuelve el foco al botón «Borrar», que desaparece con la nota: caería en <body> */
  private enfocarHistorial(): void {
    if (this.destroyRef.destroyed) {
      return;
    }
    afterNextRender(() => this.tituloHistorial()?.nativeElement.focus({ preventScroll: true }), {
      injector: this.injector,
    });
  }
}
