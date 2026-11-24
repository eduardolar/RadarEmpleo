import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { defer, EMPTY, finalize, Observable, Subscription, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { conErrorApi, convertirErrorApi } from '../../core/utils/error-api';
import {
  AplicarImportacion,
  ResultadoAplicacion,
  VistaPreviaImportacion,
} from '../interfaces/importacion.interface';
import { filasAplicables } from '../utils/resumen-importacion';

const URL_IMPORTACION = `${environment.apiUrl}/api/importacion`;

/** Una extracción que ya no vale (la API no la encuentra, el registro ha cambiado, no terminó bien...) */
const EXTRACCION_NO_VALE: ReadonlySet<number> = new Set([403, 404, 409]);

/**
 * Importación del registro: la vista previa (de la tabla o, tras extraer con Claude, de la tabla más el texto
 * libre) en signals y aplicarla. Al aplicar solo van el origen, la ejecución, el hash del registro leído y las
 * claves: el servidor vuelve a leer el registro y rehace el lote. Al aplicar bien o con un 409 (por ejemplo,
 * el registro ha cambiado), se vuelve a pedir la misma vista previa; si era la combinada y la extracción ya no
 * vale, la de la tabla.
 */
@Injectable({ providedIn: 'root' })
export class ImportacionService {
  private readonly http = inject(HttpClient);

  private readonly _vistaPrevia = signal<VistaPreviaImportacion | null>(null);
  readonly vistaPrevia = this._vistaPrevia.asReadonly();

  private readonly _cargando = signal(false);
  readonly cargando = this._cargando.asReadonly();

  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  private readonly _aplicando = signal(false);
  readonly aplicando = this._aplicando.asReadonly();

  private peticionVista?: Subscription;
  /** La extracción de la última vista pedida; null si era la de la tabla */
  private extraccionId: number | null = null;

  /** La de la tabla. Borra antes la vista previa anterior: aplicar un lote desfasado no debe estar al alcance */
  previsualizar(): void {
    this.extraccionId = null;
    this.pedirVista(`${URL_IMPORTACION}/tabla/previsualizar`, null);
  }

  /**
   * La tabla más lo que ha leído Claude en el texto libre. Si la extracción ya no vale (403, 404 o 409), pide
   * la de la tabla y pasa el mensaje de la API a `alNoValer`
   */
  previsualizarExtraccion(ejecucionId: number, alNoValer: (mensaje: string) => void): void {
    this.extraccionId = ejecucionId;
    this.pedirVista(`${URL_IMPORTACION}/extraccion/${ejecucionId}/previsualizar`, {}, (error) => {
      if (!EXTRACCION_NO_VALE.has(error.estado)) {
        return false;
      }
      this.previsualizar();
      alNoValer(error.mensaje);
      return true;
    });
  }

  /** Vuelve a pedir la misma vista previa: la de la tabla o la combinada */
  volverALeer(alNoValer: (mensaje: string) => void): void {
    if (this.extraccionId === null) {
      this.previsualizar();
    } else {
      this.previsualizarExtraccion(this.extraccionId, alNoValer);
    }
  }

  /** Añade las nuevas y las que cambian de estado. No hace nada si no hay qué o ya está guardando */
  aplicar(): Observable<ResultadoAplicacion> {
    return defer(() => {
      const vista = this._vistaPrevia();
      const claves = filasAplicables(vista?.filas ?? []).map((f) => f.clave);
      if (!vista || claves.length === 0 || this._aplicando()) {
        return EMPTY;
      }

      this._aplicando.set(true);
      const peticion: AplicarImportacion = {
        origen: vista.origen,
        ejecucionId: vista.ejecucionId,
        hashRegistro: vista.hashRegistro,
        claves,
      };
      // Quien aplica ya enseña el resultado o el 409: si al volver a leer la extracción no vale, no se repite
      const sinAviso = () => undefined;
      return this.http.post<ResultadoAplicacion>(`${URL_IMPORTACION}/lote/aplicar`, peticion).pipe(
        tap(() => this.volverALeer(sinAviso)),
        conErrorApi(),
        // Un 409 es que el registro ha cambiado u otra pestaña se ha adelantado: la vista previa ya no vale
        tap({
          error: (error: ErrorApi) => {
            if (error.estado === 409) {
              this.volverALeer(sinAviso);
            }
          },
        }),
        finalize(() => this._aplicando.set(false)),
      );
    });
  }

  /** `siFalla` devuelve true si ya se ha ocupado del error; si no, se deja para enseñarlo */
  private pedirVista(
    url: string,
    cuerpo: object | null,
    siFalla?: (error: ErrorApi) => boolean,
  ): void {
    this.peticionVista?.unsubscribe();
    this._vistaPrevia.set(null);
    this._cargando.set(true);
    this._error.set(null);

    this.peticionVista = this.http.post<VistaPreviaImportacion>(url, cuerpo).subscribe({
      next: (vista) => {
        this._vistaPrevia.set(vista);
        this._cargando.set(false);
      },
      error: (error: unknown) => {
        const errorApi = convertirErrorApi(error);
        this._cargando.set(false);
        if (!siFalla?.(errorApi)) {
          this._error.set(errorApi.mensaje);
        }
      },
    });
  }
}
