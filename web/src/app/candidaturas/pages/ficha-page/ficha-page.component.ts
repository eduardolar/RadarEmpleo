import {
  afterNextRender,
  afterRenderEffect,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
import { filter, finalize, switchMap } from 'rxjs';
import { EstadoEjecucion } from '../../../agentes/components/estado-ejecucion/estado-ejecucion';
import { EjecucionAgente } from '../../../agentes/interfaces/ejecucion.interface';
import { AgentesService, esTerminal } from '../../../agentes/services/agentes.service';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import { SistemaService } from '../../../core/services/sistema.service';
import { PanelDocumentos } from '../../../documentos/components/panel-documentos/panel-documentos';
import { TarjetaRecordatorio } from '../../../recordatorios/components/tarjeta-recordatorio/tarjeta-recordatorio';
import { AccionesRecordatorioService } from '../../../recordatorios/services/acciones-recordatorio.service';
import { tipoInicialSegunEstado } from '../../../recordatorios/utils/tipo-inicial';
import { confirmar } from '../../../shared/components/dialogo-confirmacion/dialogo-confirmacion';
import { EnlaceVolver } from '../../../shared/components/enlace-volver/enlace-volver';
import { FechaMadridPipe } from '../../../shared/pipes/fecha-madrid.pipe';
import { AvisosService } from '../../../shared/services/avisos.service';
import { FechaActualService } from '../../../shared/services/fecha-actual.service';
import { diaMadrid } from '../../../shared/utils/fechas-madrid';
import {
  aplicarErroresServidor,
  mensajeError,
  obligatorio,
} from '../../../shared/utils/validadores';
import { abrirDialogoCandidatura } from '../../components/dialogo-candidatura/dialogo-candidatura';
import {
  CandidaturaDetalle,
  Encaje,
  EstadoCandidatura,
  Nota,
  Recordatorio,
} from '../../interfaces/candidatura.interface';
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
import { esEstadoFinal } from '../../utils/transiciones-permitidas';

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
    TarjetaRecordatorio,
    EstadoEjecucion,
  ],
  selector: 'app-ficha-page',
  templateUrl: './ficha-page.component.html',
})
export default class FichaPageComponent {
  private readonly candidaturasService = inject(CandidaturasService);
  private readonly cambioEstado = inject(CambioEstadoService);
  private readonly accionesRecordatorio = inject(AccionesRecordatorioService);
  private readonly dialog = inject(MatDialog);
  private readonly avisos = inject(AvisosService);
  private readonly router = inject(Router);
  private readonly titulo = inject(Title);
  private readonly fechaActual = inject(FechaActualService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly agentes = inject(AgentesService);
  private readonly sistema = inject(SistemaService);
  private readonly documento = inject(DOCUMENT);
  private readonly inicio = viewChild.required<ElementRef<HTMLElement>>('inicio');
  private readonly estadoActual = viewChild<ElementRef<HTMLElement>>('estadoActual');
  private readonly bloqueAcciones = viewChild<ElementRef<HTMLElement>>('bloqueAcciones');
  private readonly elementoNota = viewChild<ElementRef<HTMLTextAreaElement>>('entradaNota');
  private readonly tituloHistorial = viewChild<ElementRef<HTMLElement>>('tituloHistorial');
  private readonly tituloRecordatorios = viewChild<ElementRef<HTMLElement>>('tituloRecordatorios');
  private readonly tarjetaAnalisis = viewChild(EstadoEjecucion);
  // read: con la referencia en un mat-button, sin él llegaría el componente y no el elemento
  private readonly botonAnalizar = viewChild('botonAnalizar', { read: ElementRef<HTMLElement> });
  private readonly botonDescartarCerrada = viewChild('botonDescartarCerrada', {
    read: ElementRef<HTMLElement>,
  });

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

  /** El día de Madrid de hoy: decide qué recordatorios salen como vencidos */
  protected readonly hoy = computed(() => diaMadrid(this.fechaActual.ahora()));

  /** Descartada o cerrada: no se apuntan recordatorios nuevos y los que tenga no salen en Hoy */
  protected readonly fueraDelProceso = computed(() => {
    const c = this.candidatura();
    return c !== null && esEstadoFinal(c.estado);
  });
  protected readonly hayPendientesOcultos = computed(
    () => this.fueraDelProceso() && (this.candidatura()?.recordatorios ?? []).some((r) => !r.hecho),
  );

  /** El análisis del encaje que se enseña: el lanzado aquí o el que seguía en marcha al entrar */
  private readonly _analisis = signal<EjecucionAgente | null>(null);
  protected readonly analisis = computed(() => {
    const e = this._analisis();
    return e?.candidaturaId === this.id() ? e : null;
  });
  private readonly perdidoId = signal<number | null>(null);
  protected readonly lanzandoAnalisis = signal(false);
  /** La ficha que se recarga tras un análisis: el aviso compara el encaje de antes con el que traiga */
  private readonly avisarEncajeDe = signal<{ id: number; antes: Encaje } | null>(null);

  /** Con la versión del seguimiento; null si se ha perdido (la API ya no la encuentra) */
  private readonly analisisActual = computed(() => {
    const e = this.analisis();
    return e && this.perdidoId() !== e.id ? (this.agentes.ejecuciones().get(e.id) ?? e) : null;
  });
  private readonly puedeAnalizar = computed(
    () => this.agentes.puedeLanzar() && this.candidatura() !== null && !this.fueraDelProceso(),
  );
  protected readonly bloqueClaude = computed(
    () => this.puedeAnalizar() || this.analisis() !== null,
  );
  protected readonly resumenAnalisis = computed(() => {
    const e = this.analisisActual();
    return e?.estado === 'Ok' ? e.resumen : null;
  });
  /** Con la oferta cerrada no hay análisis: se propone descartarla, nunca se descarta sola */
  protected readonly proponerDescartar = computed(() => {
    const e = this.analisisActual();
    return (
      e?.estado === 'Ok' &&
      e.estadoOferta === 'Cerrada' &&
      (this.candidatura()?.estadosPosibles.includes('Descartada') ?? false)
    );
  });
  protected readonly verBotonAnalizar = computed(() => {
    const e = this.analisisActual();
    // En Error o Cancelada la tarjeta ya ofrece «Reintentar»
    const libre = e === null || (esTerminal(e) && !e.puedeReintentar);
    return this.puedeAnalizar() && libre && !this.proponerDescartar();
  });
  protected readonly textoAnalizar = computed(() =>
    this.candidatura()?.encaje !== 'SinValorar' || this.analisisActual()?.estado === 'Ok'
      ? 'Volver a analizar el encaje'
      : 'Analizar encaje con Claude',
  );
  protected readonly avisoGasto = 'Gasta un poco de tu tope diario de Claude.';
  // Aparte del effect: volver a guardar la misma info no repite la consulta de la última
  private readonly segunInfo = computed(() => this.sistema.info()?.agentesDisponibles === true);

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
  // El botón de lanzar desaparece al salir la tarjeta: el foco pasa a ella y no cae en <body>
  private enfocarTarjeta = false;
  private siguienteVisto: HTMLElement | undefined;
  /** Con un 403 de otra visita se vuelve a preguntar una vez al entrar */
  private comprobadoAlEntrar = false;

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

    // «Apuntar la entrevista» en el aviso de pasar a Entrevista: el diálogo de recordatorio, ya con ese
    // tipo, en cuanto la ficha de esa candidatura está cargada
    effect(() => {
      const pedida = this.cambioEstado.entrevistaPedida();
      const c = this.candidatura();
      if (pedida === null || c?.id !== pedida) {
        return;
      }
      untracked(() => {
        this.cambioEstado.entrevistaAtendida();
        this.accionesRecordatorio.nuevo('Entrevista', c).subscribe();
      });
    });

    // Al entrar (también directo o con F5) se retoma el análisis que siga en marcha; uno ya terminado
    // no se enseña
    this.sistema.cargarInfoSiFalta();
    effect(() => {
      const id = this.id();
      const segunInfo = this.segunInfo();
      const disponibles = this.agentes.disponibles();
      untracked(() => {
        if (disponibles) {
          this.comprobadoAlEntrar = true;
          this.retomarAnalisis(id);
        } else if (segunInfo && !this.comprobadoAlEntrar) {
          this.comprobadoAlEntrar = true;
          this.agentes.cargarEstado();
        }
      });
    });

    effect(() => {
      const pendiente = this.avisarEncajeDe();
      const c = this.candidatura();
      if (pendiente === null || this.cargando()) {
        return;
      }
      untracked(() => {
        this.avisarEncajeDe.set(null);
        if (c?.id === pendiente.id && this.error() === null) {
          const ahora = ETIQUETAS_ENCAJE[c.encaje];
          this.avisos.exito(
            c.encaje === pendiente.antes
              ? `Encaje sin cambios: ${ahora}.`
              : `Encaje actualizado: ${ETIQUETAS_ENCAJE[pendiente.antes]} → ${ahora}.`,
          );
        }
      });
    });

    // Lo siguiente que se puede hacer (volver a lanzar, descartar) recibe el foco solo si estaba en la
    // tarjeta (en su marco, que lo coge cuando desaparece su botón): a quien lee otra cosa no se le mueve
    afterRenderEffect(() => {
      const tarjeta = this.tarjetaAnalisis();
      const siguiente = (this.botonDescartarCerrada() ?? this.botonAnalizar())?.nativeElement;
      untracked(() => {
        if (this.enfocarTarjeta && tarjeta) {
          this.enfocarTarjeta = false;
          tarjeta.enfocar();
        }
        const nuevo = siguiente !== this.siguienteVisto;
        this.siguienteVisto = siguiente;
        const enTarjeta = this.documento.activeElement?.closest('app-estado-ejecucion') != null;
        if (tarjeta && siguiente && nuevo && enTarjeta) {
          siguiente.focus({ preventScroll: true });
        }
      });
    });
  }

  protected analizarEncaje(): void {
    const c = this.candidatura();
    if (!c || this.lanzandoAnalisis()) {
      return;
    }
    this.lanzandoAnalisis.set(true);
    this.agentes
      .lanzarAnalisis(c.id)
      .pipe(
        finalize(() => this.lanzandoAnalisis.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (e) => {
          // Si entretanto se ha ido a otra ficha, no hay tarjeta que enfocar
          this.enfocarTarjeta = e.candidaturaId === this.id();
          this._analisis.set(e);
        },
        error: (error: ErrorApi) => this.avisos.error(error.mensaje),
      });
  }

  /** También para `reintentada`: la tarjeta pasa a la nueva y la ficha la guarda como la suya */
  protected guardarAnalisis(e: EjecucionAgente): void {
    this._analisis.set(e);
  }

  protected alTerminarAnalisis(e: EjecucionAgente): void {
    this._analisis.set(e);
    if (e.estado !== 'Ok' || e.candidaturaId !== this.id()) {
      return;
    }
    const c = this.candidatura();
    if (e.estadoOferta === 'Cerrada') {
      this.avisos.exito(
        `La oferta de ${e.candidatura?.empresa ?? c?.empresa} ya no está publicada. ¿La descartas?`,
        {
          texto: 'Descartar',
          alPulsar: () => this.descartarCerrada(c),
        },
      );
      return;
    }
    if (c?.id === e.candidaturaId) {
      this.avisarEncajeDe.set({ id: c.id, antes: c.encaje });
    }
    this.candidaturasService.cargarDetalle(e.candidaturaId);
  }

  protected alPerderAnalisis(id: number): void {
    this.perdidoId.set(id);
  }

  /** El diálogo de descartar con «Oferta cerrada» ya elegido: hay que confirmarlo en él */
  protected descartarCerrada(desdeAviso?: CandidaturaDetalle | null): void {
    // Desde el aviso puede estarse ya en otra ficha: se descarta aquella, con su estado de ahora si es esta
    const actual = this.candidatura();
    const c = desdeAviso && actual?.id !== desdeAviso.id ? desdeAviso : actual;
    if (c && !this.bloqueado()) {
      this.cambioEstado.mover(
        c,
        'Descartada',
        undefined,
        // Deshacer desde el aviso estando en otra ficha no debe mover el foco de esta
        () => (this.enfocarAcciones = c.id === this.id()),
        'Cerrada',
      );
    }
  }

  private retomarAnalisis(id: number): void {
    if (this.agentes.estado() === null && !this.agentes.cargandoEstado()) {
      this.agentes.cargarEstado();
    }
    this.agentes
      .ultimaDe('AnalizarEncaje', id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (e) => {
          if (e && !esTerminal(e) && this.id() === id && this.analisis() === null) {
            this._analisis.set(e);
          }
        },
        // Sin la última se puede lanzar igual: la API devuelve la que esté en marcha
        error: () => undefined,
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

  protected anadirRecordatorio(): void {
    const c = this.candidatura();
    if (!c) {
      return;
    }
    this.accionesRecordatorio.nuevo(tipoInicialSegunEstado(c.estado), c).subscribe();
  }

  protected recordatorioHecho(recordatorio: Recordatorio): void {
    this.accionesRecordatorio.hecho(recordatorio);
  }

  protected recordatorioPendiente(recordatorio: Recordatorio): void {
    this.accionesRecordatorio.pendiente(recordatorio);
  }

  protected editarRecordatorio(recordatorio: Recordatorio): void {
    this.accionesRecordatorio.editar(recordatorio, this.candidatura()).subscribe();
  }

  protected borrarRecordatorio(recordatorio: Recordatorio): void {
    this.accionesRecordatorio.borrar(recordatorio, () => this.enfocar(this.tituloRecordatorios));
  }

  private enfocarHistorial(): void {
    this.enfocar(this.tituloHistorial);
  }

  /** El diálogo devuelve el foco al botón «Borrar», que desaparece con lo borrado: caería en <body> */
  private enfocar(titulo: () => ElementRef<HTMLElement> | undefined): void {
    if (this.destroyRef.destroyed) {
      return;
    }
    afterNextRender(() => titulo()?.nativeElement.focus({ preventScroll: true }), {
      injector: this.injector,
    });
  }
}
