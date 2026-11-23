import { HttpClient, HttpParams } from '@angular/common/http';
import { computed, DestroyRef, DOCUMENT, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  catchError,
  defer,
  EMPTY,
  finalize,
  forkJoin,
  fromEvent,
  map,
  Observable,
  of,
  Subject,
  Subscription,
  tap,
} from 'rxjs';
import { environment } from '../../../environments/environment';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { SistemaService } from '../../core/services/sistema.service';
import { conErrorApi, convertirErrorApi } from '../../core/utils/error-api';
import {
  EjecucionAgente,
  EstadoAgentes,
  EstadoEjecucion,
  TareaAgente,
} from '../interfaces/ejecucion.interface';

const URL_AGENTES = `${environment.apiUrl}/api/agentes`;

/** Cada cuánto se pregunta por las ejecuciones que no han terminado */
export const INTERVALO_SEGUIMIENTO_MS = 5000;

/** Cuántas enseña la lista de /agentes */
export const LIMITE_HISTORIAL = 50;

const TERMINALES: ReadonlySet<EstadoEjecucion> = new Set(['Ok', 'Error', 'Cancelada']);

export function esTerminal(ejecucion: Pick<EjecucionAgente, 'estado'>): boolean {
  return TERMINALES.has(ejecucion.estado);
}

/**
 * Agentes de Claude: el estado (gasto de hoy y quién lleva la cola), las ejecuciones conocidas y
 * lanzar, cancelar y reintentar. Sin `agentesDisponibles` en /api/sistema/info (el móvil, apagados) no
 * se pide nada a /api/agentes: la API daría 403. Un 403 que llegue igual deja `disponibles` a false.
 *
 * Seguimiento: lo que no ha terminado se pide cada 5 s con un solo temporizador para todas, sin
 * solapar peticiones; se pausa con la pestaña oculta y, al volver, pregunta al momento. Cuando una
 * seguida termina, sale una vez por `terminada$` para que la pantalla refresque lo suyo; si la API ya
 * no la encuentra (404), se quita de `ejecuciones` y sale su id por `perdida$`.
 *
 * Una operación a la vez por ejecución (y por lanzamiento): mientras espera a la API, otra igual no se
 * manda y su Observable acaba sin emitir nada.
 */
@Injectable({ providedIn: 'root' })
export class AgentesService {
  private readonly http = inject(HttpClient);
  private readonly sistema = inject(SistemaService);
  private readonly documento = inject(DOCUMENT);

  private readonly _estado = signal<EstadoAgentes | null>(null);
  readonly estado = this._estado.asReadonly();

  private readonly _errorEstado = signal<string | null>(null);
  readonly errorEstado = this._errorEstado.asReadonly();

  /** La API ha contestado 403: desde aquí no se puede, aunque /api/sistema/info dijera que sí */
  private readonly _rechazados = signal(false);
  /** Cuántos 403 van: un estado pedido antes del último no vuelve a dar por disponibles los agentes */
  private rechazos = 0;

  readonly disponibles = computed(
    () => this.sistema.info()?.agentesDisponibles === true && !this._rechazados(),
  );
  readonly llevaLaCola = computed(() => this._estado()?.llevaLaCola === true);
  readonly motivoCola = computed(() => this._estado()?.motivoCola ?? null);
  /** Para enseñar los botones de lanzar: hace falta haber cargado el estado */
  readonly puedeLanzar = computed(() => this.disponibles() && this.llevaLaCola());

  private readonly _ejecuciones = signal<ReadonlyMap<number, EjecucionAgente>>(new Map());
  readonly ejecuciones = this._ejecuciones.asReadonly();

  /** Las últimas de /agentes, de la más reciente a la más antigua; null hasta la primera respuesta */
  private readonly _historial = signal<readonly EjecucionAgente[] | null>(null);
  readonly historial = this._historial.asReadonly();

  private readonly _cargandoHistorial = signal(false);
  readonly cargandoHistorial = this._cargandoHistorial.asReadonly();

  private readonly _errorHistorial = signal<string | null>(null);
  readonly errorHistorial = this._errorHistorial.asReadonly();

  private readonly _terminada = new Subject<EjecucionAgente>();
  readonly terminada$ = this._terminada.asObservable();

  /** Una seguida que la API ya no encuentra: sin esto, la pantalla la vería en marcha para siempre */
  private readonly _perdida = new Subject<number>();
  readonly perdida$ = this._perdida.asObservable();

  private readonly seguidas = new Set<number>();
  private readonly operaciones = new Set<string>();
  private temporizador: ReturnType<typeof setTimeout> | undefined;
  private consultando = false;
  private destruido = false;
  private peticionSeguimiento?: Subscription;
  private peticionEstado?: Subscription;
  private peticionHistorial?: Subscription;

  constructor() {
    fromEvent(this.documento, 'visibilitychange')
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.alCambiarVisibilidad());

    inject(DestroyRef).onDestroy(() => {
      this.destruido = true;
      this.pararTemporizador();
      this.peticionSeguimiento?.unsubscribe();
      this.peticionEstado?.unsubscribe();
      this.peticionHistorial?.unsubscribe();
      this._terminada.complete();
      this._perdida.complete();
    });
  }

  /**
   * Pide el gasto de hoy, la versión y quién lleva la cola. Sin agentes disponibles no pide nada.
   * Tras un 403 se puede volver a llamar: si entonces contesta bien, `disponibles` vuelve a true. Con
   * un error que no es 403 se conserva el estado anterior y solo se deja el mensaje
   */
  cargarEstado(): void {
    if (this.sistema.info()?.agentesDisponibles !== true) {
      return;
    }
    this.peticionEstado?.unsubscribe();
    const rechazosAlPedir = this.rechazos;
    this.peticionEstado = this.http.get<EstadoAgentes>(`${URL_AGENTES}/estado`).subscribe({
      next: (estado) => {
        // Pedido con la lista y esta dio 403: una API que dice sí y no a la vez no reactiva nada
        // (si no, la página volvería a pedirlo todo en bucle)
        if (this.rechazos !== rechazosAlPedir) {
          return;
        }
        this._estado.set(estado);
        this._rechazados.set(false);
        this._errorEstado.set(null);
      },
      error: (error: unknown) => {
        const errorApi = convertirErrorApi(error);
        if (!this.siRechazados(errorApi)) {
          this._errorEstado.set(errorApi.mensaje);
        }
      },
    });
  }

  /**
   * Pide las últimas 50 para /agentes. Sin agentes disponibles no pide nada. Mientras vuelve a pedirlas
   * se conserva la lista anterior (refrescar no la vacía ni hace saltar la página); si falla, también
   */
  cargarHistorial(): void {
    if (!this.disponibles()) {
      return;
    }
    this.peticionHistorial?.unsubscribe();
    this._cargandoHistorial.set(true);
    this._errorHistorial.set(null);
    const params = new HttpParams().set('limite', LIMITE_HISTORIAL);
    this.peticionHistorial = this.http
      .get<EjecucionAgente[]>(`${URL_AGENTES}/ejecuciones`, { params })
      .subscribe({
        next: (lista) => {
          this._historial.set(lista);
          this._cargandoHistorial.set(false);
        },
        error: (error: unknown) => {
          const errorApi = convertirErrorApi(error);
          if (!this.siRechazados(errorApi)) {
            this._errorHistorial.set(errorApi.mensaje);
          }
          this._cargandoHistorial.set(false);
        },
      });
  }

  lanzarAnalisis(candidaturaId: number): Observable<EjecucionAgente> {
    return this.enviar(`analizar:${candidaturaId}`, `${URL_AGENTES}/analizar/${candidaturaId}`);
  }

  lanzarExtraccion(): Observable<EjecucionAgente> {
    return this.enviar('extraer-registro', `${URL_AGENTES}/extraer-registro`);
  }

  /** En curso, la ejecución que vuelve sigue En curso (sin poder cancelar) hasta que la API corta */
  cancelar(ejecucion: EjecucionAgente): Observable<EjecucionAgente> {
    return this.enviar(
      claveEjecucion(ejecucion.id),
      `${URL_AGENTES}/ejecuciones/${ejecucion.id}/cancelar`,
    );
  }

  /** Devuelve la ejecución nueva (otro id) */
  reintentar(ejecucion: EjecucionAgente): Observable<EjecucionAgente> {
    return this.enviar(
      claveEjecucion(ejecucion.id),
      `${URL_AGENTES}/ejecuciones/${ejecucion.id}/reintentar`,
    );
  }

  /** ¿Hay una cancelación o un reintento de esa ejecución esperando a la API? */
  ocupada(id: number): boolean {
    return this.operaciones.has(claveEjecucion(id));
  }

  /**
   * La última ejecución de esa tarea (y candidatura), para retomarla al entrar en la página: si no ha
   * terminado, se sigue. null si no hay ninguna o si no hay agentes disponibles (sin pedir nada)
   */
  ultimaDe(tarea: TareaAgente, candidaturaId?: number): Observable<EjecucionAgente | null> {
    return defer(() => {
      if (!this.disponibles()) {
        return of(null);
      }
      let params = new HttpParams().set('tarea', tarea).set('limite', 1);
      if (candidaturaId !== undefined) {
        params = params.set('candidaturaId', candidaturaId);
      }
      return this.http.get<EjecucionAgente[]>(`${URL_AGENTES}/ejecuciones`, { params }).pipe(
        map((lista) => lista[0] ?? null),
        tap((ejecucion) => {
          if (ejecucion) {
            this.registrar(ejecucion);
          }
        }),
        conErrorApi(),
        tap({ error: (error: ErrorApi) => this.siRechazados(error) }),
      );
    });
  }

  /** Empieza a seguirla hasta que termine (no hace nada si ya se sigue) */
  seguir(id: number): void {
    if (this.seguidas.has(id)) {
      return;
    }
    this.seguidas.add(id);
    this.programar();
  }

  private enviar(clave: string, url: string): Observable<EjecucionAgente> {
    return defer(() => {
      if (!this.disponibles() || this.operaciones.has(clave)) {
        return EMPTY;
      }
      this.operaciones.add(clave);
      return this.http.post<EjecucionAgente>(url, {}).pipe(
        tap((ejecucion) => this.registrar(ejecucion)),
        conErrorApi(),
        tap({ error: (error: ErrorApi) => this.siRechazados(error) }),
        finalize(() => this.operaciones.delete(clave)),
      );
    });
  }

  private registrar(ejecucion: EjecucionAgente): void {
    this._ejecuciones.update((actuales) => new Map(actuales).set(ejecucion.id, ejecucion));
    // Tras un 403, las respuestas que aún estaban en vuelo no vuelven a armar el seguimiento
    if (!esTerminal(ejecucion)) {
      if (!this._rechazados()) {
        this.seguir(ejecucion.id);
      }
    } else if (this.seguidas.delete(ejecucion.id)) {
      this._terminada.next(ejecucion);
    }
  }

  private programar(): void {
    if (
      this.destruido ||
      this.temporizador !== undefined ||
      this.consultando ||
      this.seguidas.size === 0 ||
      this.oculta()
    ) {
      return;
    }
    this.temporizador = setTimeout(() => {
      this.temporizador = undefined;
      this.consultar();
    }, INTERVALO_SEGUIMIENTO_MS);
  }

  private consultar(): void {
    if (this.seguidas.size === 0 || !this.disponibles()) {
      return;
    }
    this.consultando = true;
    const peticiones = [...this.seguidas].map((id) =>
      this.http.get<EjecucionAgente>(`${URL_AGENTES}/ejecuciones/${id}`).pipe(
        tap((ejecucion) => this.registrar(ejecucion)),
        catchError((error: unknown) => {
          this.alFallarSeguimiento(id, convertirErrorApi(error));
          return of(null);
        }),
      ),
    );
    this.peticionSeguimiento = forkJoin(peticiones)
      .pipe(
        finalize(() => {
          this.consultando = false;
          this.programar();
        }),
      )
      .subscribe();
  }

  /** Un 404 es que ya no existe; otros fallos (sin conexión) se vuelven a probar en 5 s */
  private alFallarSeguimiento(id: number, error: ErrorApi): void {
    if (error.estado === 404) {
      this.seguidas.delete(id);
      this._ejecuciones.update((actuales) => {
        const sin = new Map(actuales);
        sin.delete(id);
        return sin;
      });
      this._perdida.next(id);
    } else {
      this.siRechazados(error);
    }
  }

  private alCambiarVisibilidad(): void {
    if (this.oculta()) {
      this.pararTemporizador();
      return;
    }
    // Al volver se pregunta ya: lo que haya pasado mientras estaba oculta se ve al momento
    if (this.temporizador === undefined && !this.consultando) {
      this.consultar();
    }
  }

  /** Con un 403 deja de seguir y de ofrecer nada. Devuelve si lo era */
  private siRechazados(error: ErrorApi): boolean {
    if (error.estado !== 403) {
      return false;
    }
    this.rechazos++;
    this._rechazados.set(true);
    this._estado.set(null);
    this.seguidas.clear();
    this.pararTemporizador();
    return true;
  }

  private pararTemporizador(): void {
    clearTimeout(this.temporizador);
    this.temporizador = undefined;
  }

  private oculta(): boolean {
    return this.documento.visibilityState === 'hidden';
  }
}

function claveEjecucion(id: number): string {
  return `ejecucion:${id}`;
}
