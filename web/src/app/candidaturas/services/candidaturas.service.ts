import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import {
  catchError,
  defer,
  finalize,
  MonoTypeOperatorFunction,
  Observable,
  Subscription,
  tap,
  throwError,
} from 'rxjs';
import { environment } from '../../../environments/environment';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { convertirErrorApi } from '../../core/utils/error-api';
import {
  CambiarEstado,
  CandidaturaDetalle,
  CandidaturaResumen,
  FiltroCandidaturas,
  GuardarCandidatura,
  Nota,
} from '../interfaces/candidatura.interface';

const URL_CANDIDATURAS = `${environment.apiUrl}/api/candidaturas`;
const URL_NOTAS = `${environment.apiUrl}/api/notas`;

/** Los errores de las operaciones llegan a quien se suscribe ya convertidos en ErrorApi */
function conErrorApi<T>(): MonoTypeOperatorFunction<T> {
  return catchError((error: unknown) => throwError(() => convertirErrorApi(error)));
}

function aResumen(d: CandidaturaDetalle): CandidaturaResumen {
  return {
    id: d.id,
    empresaId: d.empresaId,
    empresa: d.empresa,
    puesto: d.puesto,
    enlace: d.enlace,
    portal: d.portal,
    modalidad: d.modalidad,
    ubicacion: d.ubicacion,
    encaje: d.encaje,
    pega: d.pega,
    estado: d.estado,
    fechaDeteccion: d.fechaDeteccion,
    fechaEnvio: d.fechaEnvio,
    fechaEstado: d.fechaEstado,
    fechaUltimoCambio: d.fechaUltimoCambio,
    motivoDescarte: d.motivoDescarte,
    origen: d.origen,
  };
}

function parametros(filtro: FiltroCandidaturas): HttpParams {
  let params = new HttpParams();
  if (filtro.estado) {
    params = params.set('estado', filtro.estado);
  }
  const texto = filtro.texto?.trim();
  if (texto) {
    params = params.set('texto', texto);
  }
  return params;
}

/**
 * Candidaturas: la lista del tablero y la ficha abierta, en signals. Las operaciones devuelven un
 * Observable (para que la pantalla sepa cuándo acaba y enseñe el aviso) y, al terminar bien,
 * actualizan la lista y la ficha.
 */
@Injectable({ providedIn: 'root' })
export class CandidaturasService {
  private readonly http = inject(HttpClient);

  private readonly _candidaturas = signal<CandidaturaResumen[]>([]);
  readonly candidaturas = this._candidaturas.asReadonly();

  private readonly _cargando = signal(false);
  readonly cargando = this._cargando.asReadonly();

  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  private readonly _detalle = signal<CandidaturaDetalle | null>(null);
  readonly detalle = this._detalle.asReadonly();

  private readonly _cargandoDetalle = signal(false);
  readonly cargandoDetalle = this._cargandoDetalle.asReadonly();

  private readonly _errorDetalle = signal<ErrorApi | null>(null);
  readonly errorDetalle = this._errorDetalle.asReadonly();

  // null hasta la primera carga de la lista: antes no hay nada que refrescar. Después se mantiene al
  // día aunque se salga del tablero, porque al volver se enseña la lista guardada mientras se pide
  private filtro: FiltroCandidaturas | null = null;
  private peticionLista?: Subscription;
  private peticionDetalle?: Subscription;
  private cambiosEnCurso = 0;
  private refrescoAplazado = false;

  /** Pide la lista con estos filtros (o con los últimos usados). Cancela la petición anterior */
  cargar(filtro: FiltroCandidaturas = this.filtro ?? {}): void {
    this.filtro = filtro;
    this.peticionLista?.unsubscribe();
    this._cargando.set(true);
    this._error.set(null);

    this.peticionLista = this.http
      .get<CandidaturaResumen[]>(URL_CANDIDATURAS, { params: parametros(filtro) })
      .subscribe({
        next: (lista) => {
          this._candidaturas.set(lista);
          this._cargando.set(false);
        },
        error: (error: unknown) => {
          this._error.set(convertirErrorApi(error).mensaje);
          this._cargando.set(false);
        },
      });
  }

  /** Pide la ficha. Si es otra candidatura, borra antes la que hubiera para no enseñarla por error */
  cargarDetalle(id: number): void {
    this.peticionDetalle?.unsubscribe();
    if (this._detalle()?.id !== id) {
      this._detalle.set(null);
    }
    this._errorDetalle.set(null);

    if (!Number.isInteger(id) || id <= 0) {
      this._cargandoDetalle.set(false);
      this._errorDetalle.set({ estado: 404, mensaje: 'No se ha encontrado.', errores: {} });
      return;
    }

    this._cargandoDetalle.set(true);
    this.peticionDetalle = this.http
      .get<CandidaturaDetalle>(`${URL_CANDIDATURAS}/${id}`)
      .subscribe({
        next: (detalle) => {
          this._detalle.set(detalle);
          this._cargandoDetalle.set(false);
        },
        error: (error: unknown) => {
          const problema = convertirErrorApi(error);
          if (problema.estado === 404) {
            this._detalle.set(null);
          }
          this._errorDetalle.set(problema);
          this._cargandoDetalle.set(false);
        },
      });
  }

  crear(datos: GuardarCandidatura): Observable<CandidaturaDetalle> {
    return this.http.post<CandidaturaDetalle>(URL_CANDIDATURAS, datos).pipe(
      tap((detalle) => {
        this._detalle.set(detalle);
        this.refrescarLista();
      }),
      conErrorApi(),
    );
  }

  editar(id: number, datos: GuardarCandidatura): Observable<CandidaturaDetalle> {
    return this.http.put<CandidaturaDetalle>(`${URL_CANDIDATURAS}/${id}`, datos).pipe(
      tap((detalle) => this.guardarDetalle(detalle)),
      conErrorApi(),
    );
  }

  borrar(id: number): Observable<void> {
    return this.http.delete<void>(`${URL_CANDIDATURAS}/${id}`).pipe(
      tap(() => {
        this._candidaturas.update((lista) => lista.filter((c) => c.id !== id));
        if (this._detalle()?.id === id) {
          this._detalle.set(null);
        }
      }),
      conErrorApi(),
    );
  }

  /**
   * Cambia el estado. En la lista se mueve al momento (para que la tarjeta no vuelva a su columna
   * mientras responde la API) y, si la API lo rechaza, vuelve a donde estaba.
   */
  cambiarEstado(id: number, peticion: CambiarEstado): Observable<CandidaturaDetalle> {
    return defer(() => {
      const anterior = this._candidaturas().find((c) => c.id === id);
      if (anterior) {
        this.reemplazarEnLista({ ...anterior, estado: peticion.estado });
      }
      this.empezarCambio();

      return this.http.post<CandidaturaDetalle>(`${URL_CANDIDATURAS}/${id}/estado`, peticion).pipe(
        tap((detalle) => this.guardarDetalle(detalle)),
        conErrorApi(),
        tap({
          error: (error: ErrorApi) => {
            if (anterior) {
              this.reemplazarEnLista(anterior);
            }
            // Lo que se ve está desfasado: se cambió (409) o se borró (404) desde otro sitio
            if (error.estado === 409 || error.estado === 404) {
              this.refrescarLista();
              if (this._detalle()?.id === id) {
                this.cargarDetalle(id);
              }
            }
          },
        }),
        finalize(() => this.terminarCambio()),
      );
    });
  }

  anadirNota(candidaturaId: number, texto: string): Observable<Nota> {
    return this.http.post<Nota>(`${URL_CANDIDATURAS}/${candidaturaId}/notas`, { texto }).pipe(
      tap((nota) =>
        this.actualizarDetalle(candidaturaId, (d) => ({ ...d, notas: [nota, ...d.notas] })),
      ),
      conErrorApi(),
    );
  }

  borrarNota(nota: Nota): Observable<void> {
    return this.http.delete<void>(`${URL_NOTAS}/${nota.id}`).pipe(
      tap(() =>
        this.actualizarDetalle(nota.candidaturaId, (d) => ({
          ...d,
          notas: d.notas.filter((n) => n.id !== nota.id),
        })),
      ),
      conErrorApi(),
    );
  }

  /** Tras guardar: la ficha con lo que devuelve la API y la lista al día */
  private guardarDetalle(detalle: CandidaturaDetalle): void {
    if (this._detalle()?.id === detalle.id) {
      this.cancelarCargaDetalle();
      this._detalle.set(detalle);
      this._errorDetalle.set(null);
    }
    this.reemplazarEnLista(aResumen(detalle));
    this.refrescarLista();
  }

  private reemplazarEnLista(candidatura: CandidaturaResumen): void {
    this._candidaturas.update((lista) =>
      lista.map((c) => (c.id === candidatura.id ? candidatura : c)),
    );
  }

  private actualizarDetalle(
    id: number,
    cambio: (d: CandidaturaDetalle) => CandidaturaDetalle,
  ): void {
    const actual = this._detalle();
    if (actual?.id === id) {
      this.cancelarCargaDetalle();
      this._detalle.set(cambio(actual));
    }
  }

  /** Una carga anterior traería la ficha de antes de este cambio y lo pisaría */
  private cancelarCargaDetalle(): void {
    this.peticionDetalle?.unsubscribe();
    this._cargandoDetalle.set(false);
  }

  private empezarCambio(): void {
    this.cambiosEnCurso++;
    // Una lista pedida antes de este cambio devolvería la tarjeta a su columna anterior
    if (this.peticionLista && !this.peticionLista.closed) {
      this.peticionLista.unsubscribe();
      this.refrescoAplazado = true;
    }
  }

  private terminarCambio(): void {
    this.cambiosEnCurso--;
    if (this.cambiosEnCurso === 0 && this.refrescoAplazado) {
      this.refrescoAplazado = false;
      this.refrescarLista();
    }
  }

  /**
   * Vuelve a pedir la lista con los últimos filtros sin poner «cargando» (sin parpadeos): un cambio
   * puede hacer que una candidatura entre o salga del filtro o cambie de sitio. Si falla, se queda
   * la que había. Con cambios de estado en curso espera a que acaben: la API aún no los tiene todos
   * y la lista haría volver atrás a las tarjetas que siguen esperando.
   */
  private refrescarLista(): void {
    if (this.filtro === null) {
      return;
    }
    if (this.cambiosEnCurso > 0) {
      this.refrescoAplazado = true;
      return;
    }
    this.peticionLista?.unsubscribe();
    this.peticionLista = this.http
      .get<CandidaturaResumen[]>(URL_CANDIDATURAS, { params: parametros(this.filtro) })
      .subscribe({
        // Si había una carga normal en marcha, esta la sustituye: también hay que cerrar «cargando»
        next: (lista) => {
          this._candidaturas.set(lista);
          this._error.set(null);
          this._cargando.set(false);
        },
        error: () => this._cargando.set(false),
      });
  }
}
