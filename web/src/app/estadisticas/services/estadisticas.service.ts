import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';
import { convertirErrorApi } from '../../core/utils/error-api';
import { Estadisticas } from '../interfaces/estadisticas.interface';

const URL_ESTADISTICAS = `${environment.apiUrl}/api/estadisticas`;

/** Las estadísticas de la búsqueda en signals. Solo se leen */
@Injectable({ providedIn: 'root' })
export class EstadisticasService {
  private readonly http = inject(HttpClient);

  private readonly _estadisticas = signal<Estadisticas | null>(null);
  readonly estadisticas = this._estadisticas.asReadonly();

  private readonly _cargando = signal(false);
  readonly cargando = this._cargando.asReadonly();

  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  private peticion?: Subscription;

  /** Las pide otra vez cada vez que se entra: pueden haber cambiado desde el tablero */
  cargar(): void {
    this.peticion?.unsubscribe();
    this._cargando.set(true);
    this._error.set(null);

    this.peticion = this.http.get<Estadisticas>(URL_ESTADISTICAS).subscribe({
      next: (estadisticas) => {
        this._estadisticas.set(estadisticas);
        this._cargando.set(false);
      },
      error: (error: unknown) => {
        this._estadisticas.set(null);
        this._error.set(convertirErrorApi(error).mensaje);
        this._cargando.set(false);
      },
    });
  }
}
