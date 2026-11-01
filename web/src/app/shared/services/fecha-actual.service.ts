import { DOCUMENT, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter, fromEvent } from 'rxjs';

/**
 * El momento actual como signal, para lo que depende de «hoy» (los días en un estado): en una pestaña
 * que lleva abierta desde ayer, new Date() dentro de un computed no se volvería a calcular.
 */
@Injectable({ providedIn: 'root' })
export class FechaActualService {
  private readonly _ahora = signal(new Date());
  readonly ahora = this._ahora.asReadonly();

  constructor() {
    const documento = inject(DOCUMENT);
    fromEvent(documento, 'visibilitychange')
      .pipe(
        filter(() => documento.visibilityState === 'visible'),
        takeUntilDestroyed(),
      )
      .subscribe(() => this._ahora.set(new Date()));
  }
}
