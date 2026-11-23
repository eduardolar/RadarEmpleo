import {
  afterRenderEffect,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  ElementRef,
  inject,
  input,
  linkedSignal,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { filter, finalize, fromEvent, Observable } from 'rxjs';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import {
  EjecucionAgente,
  EstadoEjecucion as EstadoDeLaApi,
} from '../../interfaces/ejecucion.interface';
import { AgentesService, esTerminal } from '../../services/agentes.service';
import {
  avisosCambio,
  ETIQUETA_ESTADO,
  ETIQUETA_PERDIDA,
  NOMBRE_TAREA,
  TEXTO_PERDIDA,
  textoEstado,
} from '../../utils/textos-ejecucion';

/** Cada cuánto avanza el tiempo de lo que va en curso */
export const INTERVALO_RELOJ_MS = 1000;

/** Lo que pinta la insignia: un estado de la API o que ya no se encuentra */
type EstadoVisible = EstadoDeLaApi | 'Perdida';

/** El color codifica el estado, pero siempre va con su icono y su texto */
const COLOR_INSIGNIA: Record<EstadoVisible, string> = {
  EnCola: 'bg-surface-container-high text-on-surface',
  EnCurso: 'bg-surface-container-high text-on-surface',
  Ok: 'bg-ok-suave text-on-ok',
  Error: 'bg-error-container text-on-error-container',
  Cancelada: 'bg-surface-container-high text-on-surface',
  Perdida: 'bg-surface-container-high text-on-surface',
};

const CIRCULO = 'M3 10a7 7 0 1 0 14 0a7 7 0 1 0-14 0';

/** El icono de la insignia (un trazo de 20 × 20) */
const ICONO: Record<EstadoVisible, string> = {
  EnCola: `${CIRCULO}M10 6v4l2.5 2`,
  EnCurso: 'M16 10a6 6 0 1 1-1.8-4.3M16 3.5v3h-3',
  Ok: 'm4 10.5 4 4 8-9',
  Error: 'M5 5l10 10M15 5 5 15',
  Cancelada: `${CIRCULO}m2 5 10-10`,
  Perdida: 'M7 7.5a3 3 0 1 1 4.2 2.7c-.8.4-1.2 1-1.2 1.8v.5M10 16v.5',
};

type Accion = 'cancelar' | 'reintentar';

/**
 * El estado de una ejecución de Claude en línea (ficha, Importación, Rastreos y, `compacta`, la lista de
 * /agentes): insignia, frase del estado, avisos rojos y «Cancelar» o «Reintentar», siempre con borde.
 *
 * Pinta la versión más reciente de `AgentesService.ejecuciones()` (la de la entrada si aún no está) y, si
 * no ha terminado, la sigue. Al reintentar pasa a enseñar la nueva. Si la API ya no la encuentra, lo dice
 * en vez de quedarse «En curso».
 */
@Component({
  imports: [MatButtonModule, MatProgressBarModule],
  selector: 'app-estado-ejecucion',
  templateUrl: './estado-ejecucion.html',
  host: { class: 'block', '(document:focusin)': 'alMoverFoco($event)' },
})
export class EstadoEjecucion {
  private readonly agentes = inject(AgentesService);
  private readonly documento = inject(DOCUMENT);
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly ejecucion = input.required<EjecucionAgente>();
  /** Para la lista de /agentes: sin marco ni barra de progreso */
  readonly compacta = input(false);
  /** El tope de hoy para el uso estimado; sin él, el del estado de los agentes (si se ha cargado) */
  readonly topeUsd = input<number | null>(null);

  /** La enseñada ha terminado (Ok, Error o Cancelada) mientras se seguía: sale una vez */
  readonly terminada = output<EjecucionAgente>();
  /** Reintentada: la nueva, con otro id, que es la que pasa a enseñar */
  readonly reintentada = output<EjecucionAgente>();
  /** La API ya no la encuentra: quien la usa puede volver a ofrecer lanzarla */
  readonly perdida = output<number>();

  private readonly tarjeta = viewChild.required<ElementRef<HTMLElement>>('tarjeta');

  /** La de la entrada o, tras reintentar, la nueva */
  private readonly base = linkedSignal(() => this.ejecucion());

  protected readonly actual = computed(() => {
    const base = this.base();
    return this.agentes.ejecuciones().get(base.id) ?? base;
  });

  protected readonly sinRastro = linkedSignal({
    source: () => this.base().id,
    computation: () => false,
  });

  private readonly enVuelo = signal<Accion | null>(null);
  protected readonly ocupada = computed(() => this.enVuelo() !== null);

  protected readonly errorAccion = linkedSignal<number, string | null>({
    source: () => this.base().id,
    computation: () => null,
  });

  private readonly ahora = signal(new Date());
  private readonly visible = signal(true);

  private readonly tope = computed(
    () => this.topeUsd() ?? this.agentes.estado()?.gasto.topeUsd ?? null,
  );

  protected readonly estadoVisible = computed<EstadoVisible>(() =>
    this.sinRastro() ? 'Perdida' : this.actual().estado,
  );

  protected readonly etiqueta = computed(() => {
    const estado = this.estadoVisible();
    return estado === 'Perdida' ? ETIQUETA_PERDIDA : ETIQUETA_ESTADO[estado];
  });

  protected readonly colorInsignia = computed(() => COLOR_INSIGNIA[this.estadoVisible()]);
  protected readonly icono = computed(() => ICONO[this.estadoVisible()]);

  protected readonly nombreTarea = computed(() => NOMBRE_TAREA[this.actual().tarea]);

  /**
   * Lo que se anuncia al lector de pantalla: no depende del reloj, así que solo cambia con el estado.
   * Sin `inicio`, la frase de en curso se queda en «Analizando el encaje…»
   */
  protected readonly anuncio = computed(() => {
    if (this.sinRastro()) {
      return TEXTO_PERDIDA;
    }
    const e = this.actual();
    const sinReloj = e.estado === 'EnCurso' ? { ...e, inicio: null } : e;
    return textoEstado(sinReloj, this.ahora(), this.tope());
  });

  /** « · 1 min 20 s (máximo 5 min)»: a la vista y leíble, pero fuera de la región que se anuncia */
  protected readonly reloj = computed(() => {
    if (this.estadoVisible() !== 'EnCurso') {
      return '';
    }
    const completo = textoEstado(this.actual(), this.ahora(), this.tope());
    const anuncio = this.anuncio();
    return completo.startsWith(anuncio) ? completo.slice(anuncio.length) : '';
  });

  protected readonly avisos = computed(() => (this.sinRastro() ? [] : avisosCambio(this.actual())));

  protected readonly puedeCancelar = computed(
    () => !this.sinRastro() && this.actual().puedeCancelar,
  );
  protected readonly puedeReintentar = computed(
    () => !this.sinRastro() && this.actual().puedeReintentar,
  );

  protected readonly enCurso = computed(() => this.estadoVisible() === 'EnCurso');

  /** El foco estaba en la tarjeta: si el botón pulsado desaparece, vuelve a ella y no a <body> */
  private focoDentro = false;
  private vistaEnMapa: number | null = null;

  constructor() {
    this.visible.set(this.documento.visibilityState !== 'hidden');
    fromEvent(this.documento, 'visibilitychange')
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.visible.set(this.documento.visibilityState !== 'hidden'));

    this.agentes.terminada$
      .pipe(
        filter((e) => !this.sinRastro() && e.id === this.base().id),
        takeUntilDestroyed(),
      )
      .subscribe((e) => this.terminada.emit(e));

    this.agentes.perdida$
      .pipe(
        filter((id) => id === this.base().id),
        takeUntilDestroyed(),
      )
      .subscribe((id) => this.marcarPerdida(id));

    // Lo que llega sin terminar se sigue (el servicio no lo repite si ya lo hace)
    effect(() => {
      const e = this.base();
      if (!esTerminal(e)) {
        untracked(() => {
          if (this.agentes.disponibles()) {
            this.agentes.seguir(e.id);
          }
        });
      }
    });

    // Si estaba en el mapa y desaparece, también se ha perdido (el servicio la quita tras un 404)
    effect(() => {
      const id = this.base().id;
      const esta = this.agentes.ejecuciones().has(id);
      untracked(() => {
        if (esta) {
          this.vistaEnMapa = id;
        } else if (this.vistaEnMapa === id) {
          this.marcarPerdida(id);
        }
      });
    });

    // Un reloj de 1 s solo mientras va en curso y la pestaña está a la vista
    effect((alLimpiar) => {
      if (!this.enCurso() || !this.visible()) {
        return;
      }
      untracked(() => this.ahora.set(new Date()));
      const reloj = setInterval(() => this.ahora.set(new Date()), INTERVALO_RELOJ_MS);
      alLimpiar(() => clearInterval(reloj));
    });

    afterRenderEffect(() => {
      this.puedeCancelar();
      this.puedeReintentar();
      this.estadoVisible();
      const activo = this.documento.activeElement;
      if (this.focoDentro && (activo === null || activo === this.documento.body)) {
        this.tarjeta().nativeElement.focus({ preventScroll: true });
      }
    });
  }

  protected alMoverFoco(evento: FocusEvent): void {
    this.focoDentro = this.elemento.nativeElement.contains(evento.target as Node | null);
  }

  protected cancelar(): void {
    this.ejecutar('cancelar', (e) => this.agentes.cancelar(e));
  }

  protected reintentar(): void {
    this.ejecutar(
      'reintentar',
      (e) => this.agentes.reintentar(e),
      (nueva) => {
        this.base.set(nueva);
        this.reintentada.emit(nueva);
      },
    );
  }

  private ejecutar(
    accion: Accion,
    pedir: (e: EjecucionAgente) => Observable<EjecucionAgente>,
    alResponder?: (e: EjecucionAgente) => void,
  ): void {
    // disabledInteractive deja pasar el clic mientras responde: aquí se para
    if (this.enVuelo() !== null) {
      return;
    }
    this.enVuelo.set(accion);
    this.errorAccion.set(null);
    pedir(this.actual())
      .pipe(
        finalize(() => this.enVuelo.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (e) => alResponder?.(e),
        error: (error: ErrorApi) =>
          this.errorAccion.set(
            `${accion === 'cancelar' ? 'No se ha podido cancelar' : 'No se ha podido reintentar'}. ${error.mensaje}`,
          ),
      });
  }

  private marcarPerdida(id: number): void {
    if (this.sinRastro()) {
      return;
    }
    this.sinRastro.set(true);
    this.perdida.emit(id);
  }
}
