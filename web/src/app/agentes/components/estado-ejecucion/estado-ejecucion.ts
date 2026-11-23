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
import { filter, fromEvent, Observable } from 'rxjs';
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
  relojEnCurso,
  TEXTO_PERDIDA,
  TEXTO_PERDIDA_CORTO,
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
  // text-error sobre su 8 %: 5,6:1 de día (#ba1a1a) y 9:1 de noche (#ffb4ab sobre #111827)
  Error: 'bg-error/8 text-error',
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
 *
 * Quien la usa debe escuchar `reintentada` y guardar la nueva como su ejecución: si vuelve a pasar la
 * vieja en `ejecucion`, la tarjeta vuelve a ella. `terminada` sale una vez y solo si la ejecución llegó
 * sin terminar: una que ya llega terminada (la lista de /agentes) no avisa.
 */
@Component({
  imports: [MatButtonModule, MatProgressBarModule],
  selector: 'app-estado-ejecucion',
  templateUrl: './estado-ejecucion.html',
  host: { class: 'block', '(focusin)': 'focoDentro = true', '(focusout)': 'alSalirFoco($event)' },
})
export class EstadoEjecucion {
  private readonly agentes = inject(AgentesService);
  private readonly documento = inject(DOCUMENT);
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly ejecucion = input.required<EjecucionAgente>();
  /** Para la lista de /agentes: en una línea, sin marco, sin barra y sin anunciar los cambios */
  readonly compacta = input(false);
  /** El tope de hoy para el uso estimado; sin él, el del estado de los agentes (si se ha cargado) */
  readonly topeUsd = input<number | null>(null);
  /** Lo que la distingue de otras iguales en la pantalla (la empresa), para los nombres accesibles */
  readonly contexto = input<string | null>(null);

  /** La enseñada ha terminado (Ok, Error o Cancelada) tras llegar sin terminar: sale una vez */
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

  /** Al cambiar de ejecución, lo que estaba en vuelo deja de contar (su respuesta se ignora) */
  private readonly enVuelo = linkedSignal<number, Accion | null>({
    source: () => this.base().id,
    computation: () => null,
  });
  protected readonly ocupada = computed(() => this.enVuelo() !== null);

  private readonly ahora = signal(new Date());
  private readonly visible = signal(true);

  private readonly tope = computed(
    () => this.topeUsd() ?? this.agentes.estado()?.gasto.topeUsd ?? null,
  );

  protected readonly estadoVisible = computed<EstadoVisible>(() =>
    this.sinRastro() ? 'Perdida' : this.actual().estado,
  );

  /** Se borra al cambiar de ejecución o de estado: un fallo de antes ya no dice nada del de ahora */
  protected readonly errorAccion = linkedSignal<string, string | null>({
    source: () => `${this.base().id}:${this.estadoVisible()}`,
    computation: () => null,
  });

  protected readonly etiqueta = computed(() => {
    const estado = this.estadoVisible();
    return estado === 'Perdida' ? ETIQUETA_PERDIDA : ETIQUETA_ESTADO[estado];
  });

  protected readonly colorInsignia = computed(() => COLOR_INSIGNIA[this.estadoVisible()]);
  protected readonly icono = computed(() => ICONO[this.estadoVisible()]);

  /** «Análisis del encaje · Acme»: el nombre de la tarjeta y el de sus botones */
  protected readonly nombre = computed(() => {
    const tarea = NOMBRE_TAREA[this.actual().tarea];
    const contexto = this.contexto()?.trim();
    return contexto ? `${tarea} · ${contexto}` : tarea;
  });

  /** Lo que se anuncia al lector de pantalla: sin el reloj, así que solo cambia con el estado */
  protected readonly anuncio = computed(() => {
    if (this.sinRastro()) {
      return this.compacta() ? TEXTO_PERDIDA_CORTO : TEXTO_PERDIDA;
    }
    return textoEstado(this.actual(), null, this.tope());
  });

  // Si hay reloj no depende de la hora, solo de la ejecución
  private readonly hayReloj = computed(
    () => !this.sinRastro() && relojEnCurso(this.actual(), new Date()) !== null,
  );

  /** «1 min 20 s (máximo 5 min)»: a la vista y leíble, pero fuera de la región que se anuncia */
  protected readonly reloj = computed(() =>
    this.hayReloj() ? relojEnCurso(this.actual(), this.ahora()) : null,
  );

  protected readonly avisos = computed(() =>
    this.sinRastro() ? [] : avisosCambio(this.actual(), this.compacta()),
  );

  // Sin agentes disponibles el servicio no manda nada: el botón no haría nada
  protected readonly puedeCancelar = computed(
    () => !this.sinRastro() && this.agentes.disponibles() && this.actual().puedeCancelar,
  );
  protected readonly puedeReintentar = computed(
    () => !this.sinRastro() && this.agentes.disponibles() && this.actual().puedeReintentar,
  );

  protected readonly enCurso = computed(() => this.estadoVisible() === 'EnCurso');

  /** En la tarjeta, «Cancelar la tarea» (no se confunde con cancelar lo de alrededor); en la lista, corto */
  protected readonly textoCancelar = computed(() =>
    this.compacta() ? 'Cancelar' : 'Cancelar la tarea',
  );

  /** El foco está en la tarjeta: si el botón pulsado desaparece, vuelve a ella y no a <body> */
  protected focoDentro = false;
  private vistaEnMapa: number | null = null;
  /** La última por la que ha salido `terminada`, para que no salga dos veces */
  private avisadaId: number | null = null;

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
      .subscribe((e) => this.avisarTerminada(e));

    this.agentes.perdida$
      .pipe(
        filter((id) => id === this.base().id),
        takeUntilDestroyed(),
      )
      .subscribe((id) => this.marcarPerdida(id));

    // Se decide con la versión del mapa: la entrada puede ser vieja (la ficha remontada con una en
    // cola que ya ha terminado) y seguirla otra vez pediría de más y avisaría dos veces
    effect(() => {
      const entrada = this.base();
      const e = this.actual();
      untracked(() => {
        if (this.sinRastro()) {
          return;
        }
        if (!esTerminal(e)) {
          if (this.agentes.disponibles()) {
            this.agentes.seguir(e.id);
          }
        } else if (!esTerminal(entrada)) {
          this.avisarTerminada(e);
        }
      });
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

    // Un reloj de 1 s solo mientras hay tiempo que enseñar y la pestaña está a la vista
    effect((alLimpiar) => {
      if (!this.hayReloj() || !this.visible()) {
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

  protected alSalirFoco(evento: FocusEvent): void {
    const destino = evento.relatedTarget as Node | null;
    if (destino !== null) {
      this.focoDentro = this.elemento.nativeElement.contains(destino);
      return;
    }
    // Sin destino es un clic en otra parte o el botón que se va del DOM (hay navegadores que avisan
    // al quitarlo): solo en el primer caso sigue conectado
    const origen = evento.target as Node;
    queueMicrotask(() => {
      if (origen.isConnected) {
        this.focoDentro = false;
      }
    });
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
    const id = this.base().id;
    // Si entretanto se enseña otra, la respuesta ya no es de esta tarjeta
    const vigente = () => this.base().id === id;
    this.enVuelo.set(accion);
    this.errorAccion.set(null);
    pedir(this.actual())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (e) => {
          if (vigente()) {
            alResponder?.(e);
          }
        },
        error: (error: ErrorApi) => {
          if (!vigente()) {
            return;
          }
          this.enVuelo.set(null);
          this.errorAccion.set(
            `${accion === 'cancelar' ? 'No se ha podido cancelar' : 'No se ha podido reintentar'}. ${error.mensaje}`,
          );
        },
        complete: () => {
          if (vigente()) {
            this.enVuelo.set(null);
          }
        },
      });
  }

  private avisarTerminada(e: EjecucionAgente): void {
    if (this.avisadaId === e.id) {
      return;
    }
    this.avisadaId = e.id;
    this.terminada.emit(e);
  }

  private marcarPerdida(id: number): void {
    if (this.sinRastro()) {
      return;
    }
    this.sinRastro.set(true);
    this.perdida.emit(id);
  }
}
