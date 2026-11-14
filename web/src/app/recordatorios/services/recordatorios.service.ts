import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { defer, EMPTY, finalize, Observable, Subscription, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Recordatorio } from '../../candidaturas/interfaces/candidatura.interface';
import { CandidaturasService } from '../../candidaturas/services/candidaturas.service';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { conErrorApi, convertirErrorApi } from '../../core/utils/error-api';
import { PanelHoy } from '../../hoy/interfaces/panel-hoy.interface';
import { GuardarRecordatorio } from '../interfaces/recordatorio.interface';

const URL_PANEL = `${environment.apiUrl}/api/panel/hoy`;
const URL_RECORDATORIOS = `${environment.apiUrl}/api/recordatorios`;
const URL_CANDIDATURAS = `${environment.apiUrl}/api/candidaturas`;

/**
 * Recordatorios y el panel de Hoy, en signals. Las operaciones devuelven un Observable (para que la
 * pantalla sepa cuándo acaban y enseñe el aviso) y, al terminar bien, ponen al día el panel y, si el
 * recordatorio es de la ficha abierta, también la ficha.
 *
 * Cada recordatorio admite una sola operación a la vez: mientras una espera a la API, otra sobre el
 * mismo (un doble clic, un Ctrl+Z repetido) no se manda y su Observable acaba sin emitir nada.
 */
@Injectable({ providedIn: 'root' })
export class RecordatoriosService {
  private readonly http = inject(HttpClient);
  private readonly candidaturasService = inject(CandidaturasService);

  private readonly _panel = signal<PanelHoy | null>(null);
  readonly panel = this._panel.asReadonly();

  private readonly _cargando = signal(false);
  readonly cargando = this._cargando.asReadonly();

  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  /**
   * Cuántas veces ha fallado un refresco con el panel ya a la vista (se queda el que había, sin aviso):
   * la pantalla lo mira para no esperar a un panel nuevo que no va a llegar
   */
  private readonly _refrescosFallidos = signal(0);
  readonly refrescosFallidos = this._refrescosFallidos.asReadonly();

  // Antes de pedirlo por primera vez no hay panel que refrescar (se cambian recordatorios desde la ficha
  // sin haber pasado por Hoy)
  private panelPedido = false;
  private peticionPanel?: Subscription;
  private readonly idsEnCurso = new Set<number>();

  /**
   * Pide el panel. Si ya hay uno a la vista, lo refresca sin poner «cargando» (sin parpadeos) y, si
   * falla, se queda el que había
   */
  cargarPanel(): void {
    this.panelPedido = true;
    const primeraVez = this._panel() === null;
    this.peticionPanel?.unsubscribe();
    this._cargando.set(primeraVez);
    if (primeraVez) {
      this._error.set(null);
    }

    this.peticionPanel = this.http.get<PanelHoy>(URL_PANEL).subscribe({
      next: (panel) => {
        this._panel.set(panel);
        this._error.set(null);
        this._cargando.set(false);
      },
      error: (error: unknown) => {
        if (primeraVez) {
          this._error.set(convertirErrorApi(error).mensaje);
        } else {
          this._refrescosFallidos.update((veces) => veces + 1);
        }
        this._cargando.set(false);
      },
    });
  }

  /** ¿Hay una operación de ese recordatorio esperando a la API? */
  ocupado(id: number): boolean {
    return this.idsEnCurso.has(id);
  }

  /** Suelto o, con `candidaturaId`, de esa candidatura */
  crear(datos: GuardarRecordatorio, candidaturaId?: number): Observable<Recordatorio> {
    const url =
      candidaturaId === undefined
        ? URL_RECORDATORIOS
        : `${URL_CANDIDATURAS}/${candidaturaId}/recordatorios`;
    return this.http.post<Recordatorio>(url, datos).pipe(
      tap((creado) => this.alGuardar(creado)),
      conErrorApi(),
    );
  }

  editar(recordatorio: Recordatorio, datos: GuardarRecordatorio): Observable<Recordatorio> {
    return this.cambiar(recordatorio, () =>
      this.http.put<Recordatorio>(`${URL_RECORDATORIOS}/${recordatorio.id}`, datos),
    );
  }

  /** `hecho` a false lo deja pendiente otra vez */
  marcarHecho(recordatorio: Recordatorio, hecho: boolean): Observable<Recordatorio> {
    return this.cambiar(recordatorio, () =>
      this.http.post<Recordatorio>(`${URL_RECORDATORIOS}/${recordatorio.id}/hecho`, { hecho }),
    );
  }

  /** Lo pasa al día `dia` («2026-11-14») a la misma hora */
  posponer(recordatorio: Recordatorio, dia: string): Observable<Recordatorio> {
    return this.cambiar(recordatorio, () =>
      this.http.post<Recordatorio>(`${URL_RECORDATORIOS}/${recordatorio.id}/posponer`, { dia }),
    );
  }

  borrar(recordatorio: Recordatorio): Observable<void> {
    const { id, candidaturaId } = recordatorio;
    return this.unaALaVez(
      recordatorio,
      () => this.http.delete<void>(`${URL_RECORDATORIOS}/${id}`),
      () => {
        this.candidaturasService.quitarRecordatorioDeDetalle(id, candidaturaId);
        this.refrescarPanel();
      },
    );
  }

  private cambiar(
    recordatorio: Recordatorio,
    peticion: () => Observable<Recordatorio>,
  ): Observable<Recordatorio> {
    return this.unaALaVez(recordatorio, peticion, (guardado) => this.alGuardar(guardado));
  }

  private unaALaVez<T>(
    recordatorio: Recordatorio,
    peticion: () => Observable<T>,
    alTerminarBien: (respuesta: T) => void,
  ): Observable<T> {
    const { id } = recordatorio;
    return defer(() => {
      if (this.idsEnCurso.has(id)) {
        return EMPTY;
      }
      this.idsEnCurso.add(id);
      return peticion().pipe(
        tap(alTerminarBien),
        conErrorApi(),
        tap({ error: (error: ErrorApi) => this.refrescarSiDesfasado(recordatorio, error) }),
        finalize(() => this.idsEnCurso.delete(id)),
      );
    });
  }

  private alGuardar(recordatorio: Recordatorio): void {
    this.candidaturasService.ponerRecordatorioEnDetalle(recordatorio);
    this.refrescarPanel();
  }

  /** Lo que se ve está desfasado: se cambió (409) o se borró (404) desde otro sitio */
  private refrescarSiDesfasado(recordatorio: Recordatorio, error: ErrorApi): void {
    if (error.estado !== 409 && error.estado !== 404) {
      return;
    }
    this.refrescarPanel();
    const { candidaturaId } = recordatorio;
    if (candidaturaId !== null && this.candidaturasService.detalle()?.id === candidaturaId) {
      this.candidaturasService.cargarDetalle(candidaturaId);
    }
  }

  private refrescarPanel(): void {
    if (this.panelPedido) {
      this.cargarPanel();
    }
  }
}
