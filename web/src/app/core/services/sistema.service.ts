import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { EstadoConexionApi, SistemaInfo } from '../interfaces/sistema-info.interface';

@Injectable({ providedIn: 'root' })
export class SistemaService {
  private readonly http = inject(HttpClient);

  private readonly _info = signal<SistemaInfo | null>(null);
  readonly info = this._info.asReadonly();

  private readonly _estado = signal<EstadoConexionApi>('comprobando');
  readonly estado = this._estado.asReadonly();

  /** Pide el modo, los agentes y la versión a la API. Si falla, el estado pasa a «sin-conexion» */
  cargarInfo(): void {
    this._estado.set('comprobando');

    this.http.get<SistemaInfo>(`${environment.apiUrl}/api/sistema/info`).subscribe({
      next: (info) => {
        this._info.set(info);
        this._estado.set('conectada');
      },
      error: () => {
        this._info.set(null);
        this._estado.set('sin-conexion');
      },
    });
  }

  /** Como cargarInfo, pero no repite la petición si la API ya respondió (p. ej. al volver a Hoy) */
  cargarInfoSiFalta(): void {
    if (this._estado() !== 'conectada') {
      this.cargarInfo();
    }
  }
}
