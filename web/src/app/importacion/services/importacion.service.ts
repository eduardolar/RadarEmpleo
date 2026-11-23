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

/**
 * Importación del registro: la vista previa en signals y aplicarla. Al aplicar solo van el origen, la
 * ejecución, el hash del registro leído y las claves: el servidor vuelve a leer el registro y rehace el lote.
 * Al aplicar bien o con un 409 (por ejemplo, el registro ha cambiado), se vuelve a pedir la vista previa.
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

  /** Borra antes la vista previa anterior: aplicar un lote desfasado no debe estar al alcance */
  previsualizar(): void {
    this.peticionVista?.unsubscribe();
    this._vistaPrevia.set(null);
    this._cargando.set(true);
    this._error.set(null);

    this.peticionVista = this.http
      .post<VistaPreviaImportacion>(`${URL_IMPORTACION}/tabla/previsualizar`, null)
      .subscribe({
        next: (vista) => {
          this._vistaPrevia.set(vista);
          this._cargando.set(false);
        },
        error: (error: unknown) => {
          this._error.set(convertirErrorApi(error).mensaje);
          this._cargando.set(false);
        },
      });
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
      return this.http.post<ResultadoAplicacion>(`${URL_IMPORTACION}/lote/aplicar`, peticion).pipe(
        tap(() => this.previsualizar()),
        conErrorApi(),
        // Un 409 es que el registro ha cambiado u otra pestaña se ha adelantado: la vista previa ya no vale
        tap({
          error: (error: ErrorApi) => {
            if (error.estado === 409) {
              this.previsualizar();
            }
          },
        }),
        finalize(() => this._aplicando.set(false)),
      );
    });
  }
}
