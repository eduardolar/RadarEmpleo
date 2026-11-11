import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';
import { convertirErrorApi } from '../../core/utils/error-api';
import { Perfil } from '../interfaces/perfil.interface';

const URL_PERFIL = `${environment.apiUrl}/api/perfil`;

/** El perfil de búsqueda en signals. Solo se lee: la app no lo cambia */
@Injectable({ providedIn: 'root' })
export class PerfilService {
  private readonly http = inject(HttpClient);

  private readonly _perfil = signal<Perfil | null>(null);
  readonly perfil = this._perfil.asReadonly();

  private readonly _cargando = signal(false);
  readonly cargando = this._cargando.asReadonly();

  private readonly _error = signal<string | null>(null);
  readonly error = this._error.asReadonly();

  private peticion?: Subscription;

  /** Lo pide otra vez: el equipo /empleo lo puede haber cambiado desde la última visita */
  cargar(): void {
    this.peticion?.unsubscribe();
    this._cargando.set(true);
    this._error.set(null);

    this.peticion = this.http.get<Perfil>(URL_PERFIL).subscribe({
      next: (perfil) => {
        this._perfil.set(perfil);
        this._cargando.set(false);
      },
      error: (error: unknown) => {
        this._perfil.set(null);
        this._error.set(convertirErrorApi(error).mensaje);
        this._cargando.set(false);
      },
    });
  }
}
