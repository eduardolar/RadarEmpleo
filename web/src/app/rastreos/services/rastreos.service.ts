import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { defer, EMPTY, finalize, Observable, Subscription, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CandidaturaResumen } from '../../candidaturas/interfaces/candidatura.interface';
import { conErrorApi, convertirErrorApi } from '../../core/utils/error-api';
import {
  EstadoOfertaRastreo,
  OfertaRastreo,
  RastreoDetalle,
  RastreoResumen,
  ResultadoDeteccion,
  ResultadoEscaneo,
} from '../interfaces/rastreo.interface';
import { contarPendientes } from '../utils/agrupar-ofertas';
import { CandidaturaParecida } from '../utils/posibles-repetidas';

const URL_RASTREOS = `${environment.apiUrl}/api/rastreos`;
const URL_CANDIDATURAS = `${environment.apiUrl}/api/candidaturas`;

/**
 * Bandeja de rastreos en signals: escanear los informes, la lista, el rastreo que se está viendo y pasar
 * una oferta al tablero o dejarla de lado. Esas dos acciones se ven al momento (actualización
 * optimista) y, si la API falla, la oferta vuelve a como estaba.
 */
@Injectable({ providedIn: 'root' })
export class RastreosService {
  private readonly http = inject(HttpClient);

  private readonly _rastreos = signal<RastreoResumen[]>([]);
  readonly rastreos = this._rastreos.asReadonly();

  private readonly _seleccionado = signal<number | null>(null);
  readonly seleccionado = this._seleccionado.asReadonly();

  private readonly _detalle = signal<RastreoDetalle | null>(null);
  readonly detalle = this._detalle.asReadonly();

  private readonly _escaneando = signal(false);
  readonly escaneando = this._escaneando.asReadonly();

  private readonly _resultado = signal<ResultadoEscaneo | null>(null);
  readonly resultado = this._resultado.asReadonly();

  private readonly _errorEscaneo = signal<string | null>(null);
  readonly errorEscaneo = this._errorEscaneo.asReadonly();

  private readonly _cargando = signal(false);
  readonly cargando = this._cargando.asReadonly();

  /** Error al pedir la lista o el rastreo */
  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  /** Ofertas con una petición en curso: un segundo clic no hace nada */
  private readonly _ocupadas = signal<ReadonlySet<number>>(new Set());
  readonly ocupadas = this._ocupadas.asReadonly();

  /** Candidaturas del tablero, para avisar de ofertas que pueden estar repetidas */
  private readonly _delTablero = signal<CandidaturaParecida[]>([]);
  readonly delTablero = this._delTablero.asReadonly();

  private peticionEscaneo?: Subscription;
  private peticionLista?: Subscription;
  private peticionDetalle?: Subscription;
  private peticionTablero?: Subscription;

  /**
   * Busca informes nuevos en Curriculum y después pide la lista. Si el escaneo falla (sin carpeta, por
   * ejemplo), la lista se pide igual: lo leído otras veces sigue ahí.
   */
  escanear(): void {
    this.peticionEscaneo?.unsubscribe();
    this._escaneando.set(true);
    this._errorEscaneo.set(null);
    this._resultado.set(null);

    this.peticionEscaneo = this.http
      .post<ResultadoEscaneo>(`${URL_RASTREOS}/escanear`, {})
      .pipe(finalize(() => this._escaneando.set(false)))
      .subscribe({
        next: (resultado) => {
          this._resultado.set(resultado);
          this.cargarLista();
        },
        error: (error: unknown) => {
          this._errorEscaneo.set(convertirErrorApi(error).mensaje);
          this.cargarLista();
        },
      });
  }

  /** Pide la lista y abre el rastreo que se estaba viendo o, si ya no está, el más reciente */
  cargarLista(): void {
    this.peticionLista?.unsubscribe();
    this._cargando.set(true);
    this._error.set(null);

    this.peticionLista = this.http.get<RastreoResumen[]>(URL_RASTREOS).subscribe({
      next: (lista) => {
        this._rastreos.set(lista);
        const actual = this._seleccionado();
        const id = lista.some((r) => r.id === actual) ? actual : (lista[0]?.id ?? null);
        if (id === null) {
          this._seleccionado.set(null);
          this._detalle.set(null);
          this._cargando.set(false);
        } else {
          this.seleccionar(id);
        }
      },
      error: (error: unknown) => {
        this._error.set(convertirErrorApi(error).mensaje);
        this._cargando.set(false);
      },
    });
  }

  /** Abre otro rastreo. El anterior se quita al momento para no mezclar sus ofertas con el nuevo */
  seleccionar(id: number): void {
    this.peticionDetalle?.unsubscribe();
    this._seleccionado.set(id);
    if (this._detalle()?.id !== id) {
      this._detalle.set(null);
    }
    this._cargando.set(true);
    this._error.set(null);

    this.peticionDetalle = this.http.get<RastreoDetalle>(`${URL_RASTREOS}/${id}`).subscribe({
      next: (detalle) => {
        this._detalle.set(detalle);
        this._cargando.set(false);
      },
      error: (error: unknown) => {
        this._error.set(convertirErrorApi(error).mensaje);
        this._cargando.set(false);
      },
    });
  }

  /**
   * Pide todas las candidaturas (sin filtros: no toca la búsqueda del tablero). Las del registro no
   * tienen enlace y la API no las reconoce en un rastreo: se comparan por empresa. Si falla, no se avisa
   */
  cargarTablero(): void {
    this.peticionTablero?.unsubscribe();
    this.peticionTablero = this.http.get<CandidaturaResumen[]>(URL_CANDIDATURAS).subscribe({
      next: (lista) =>
        this._delTablero.set(
          lista.map(({ id, empresa, puesto, estado }) => ({ id, empresa, puesto, estado })),
        ),
      error: () => undefined,
    });
  }

  /** «Pasar al tablero». Se ve en el tablero al momento; si falla, vuelve a como estaba */
  detectar(oferta: OfertaRastreo): Observable<ResultadoDeteccion> {
    return this.enviar<ResultadoDeteccion>(oferta, 'detectar', 'Convertida', (r) => r.oferta).pipe(
      tap((resultado) => {
        // La nueva cuenta para avisar en otras ofertas de la misma empresa
        if (!this._delTablero().some((c) => c.id === resultado.candidaturaId)) {
          this._delTablero.update((lista) => [
            ...lista,
            {
              id: resultado.candidaturaId,
              empresa: oferta.empresa,
              puesto: oferta.puesto,
              estado: 'Detectada',
            },
          ]);
        }
      }),
    );
  }

  /** «No me interesa». Se pliega al momento; si falla, vuelve a como estaba */
  ignorar(oferta: OfertaRastreo): Observable<OfertaRastreo> {
    return this.enviar<OfertaRastreo>(oferta, 'ignorar', 'Ignorada', (o) => o);
  }

  private enviar<T>(
    oferta: OfertaRastreo,
    accion: 'detectar' | 'ignorar',
    estado: EstadoOfertaRastreo,
    ofertaDe: (respuesta: T) => OfertaRastreo,
  ): Observable<T> {
    return defer(() => {
      if (this._ocupadas().has(oferta.id)) {
        return EMPTY;
      }
      this.marcarOcupada(oferta.id, true);
      this.ponerOferta({ ...oferta, estado });

      return this.http.post<T>(`${URL_RASTREOS}/ofertas/${oferta.id}/${accion}`, {}).pipe(
        tap((respuesta) => this.ponerOferta(ofertaDe(respuesta))),
        conErrorApi(),
        tap({ error: () => this.ponerOferta(oferta) }),
        finalize(() => this.marcarOcupada(oferta.id, false)),
      );
    });
  }

  /** Cambia la oferta en el rastreo abierto (si es el suyo) y las pendientes de su rastreo en la lista */
  private ponerOferta(oferta: OfertaRastreo): void {
    const detalle = this._detalle();
    if (detalle?.id !== oferta.rastreoId) {
      return;
    }
    const ofertas = detalle.ofertas.map((o) => (o.id === oferta.id ? oferta : o));
    const pendientes = contarPendientes(ofertas);
    this._detalle.set({ ...detalle, ofertas, pendientes });
    this._rastreos.update((lista) =>
      lista.map((r) => (r.id === detalle.id ? { ...r, pendientes } : r)),
    );
  }

  private marcarOcupada(id: number, ocupada: boolean): void {
    this._ocupadas.update((actuales) => {
      const nuevas = new Set(actuales);
      if (ocupada) {
        nuevas.add(id);
      } else {
        nuevas.delete(id);
      }
      return nuevas;
    });
  }
}
