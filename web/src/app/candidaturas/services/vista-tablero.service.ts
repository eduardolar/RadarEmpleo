import { Injectable, signal } from '@angular/core';
import { EstadoCandidatura } from '../interfaces/candidatura.interface';

export type FiltroMovil = EstadoCandidatura | 'Todas';

/**
 * Cómo se estaba mirando el tablero (búsqueda y, en el móvil, el estado filtrado). Vive fuera de la
 * página para que, al entrar en una ficha y volver, el tablero siga igual que se dejó.
 */
@Injectable({ providedIn: 'root' })
export class VistaTableroService {
  private readonly _texto = signal('');
  readonly texto = this._texto.asReadonly();

  private readonly _filtroMovil = signal<FiltroMovil>('Todas');
  readonly filtroMovil = this._filtroMovil.asReadonly();

  buscar(texto: string): void {
    this._texto.set(texto);
  }

  filtrar(filtro: FiltroMovil): void {
    this._filtroMovil.set(filtro);
  }
}
