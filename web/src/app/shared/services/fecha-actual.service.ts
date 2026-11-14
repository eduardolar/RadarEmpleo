import { DestroyRef, DOCUMENT, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, filter, fromEvent, merge } from 'rxjs';
import { msHastaMedianocheMadrid } from '../utils/fechas-madrid';

/** Un poco después de las 00:00: un temporizador puede saltar unos milisegundos antes de tiempo */
const MARGEN_MEDIANOCHE_MS = 1000;

/**
 * El momento actual como signal, para lo que depende de «hoy» (los días en un estado): en una pestaña
 * que lleva abierta desde ayer, new Date() dentro de un computed no se volvería a calcular.
 *
 * Se pone al día a medianoche de Madrid, aunque la pestaña siga a la vista, y al volver a ella (al
 * hacerse visible o al recibir el foco la ventana): con el equipo suspendido, el temporizador llega tarde.
 */
@Injectable({ providedIn: 'root' })
export class FechaActualService {
  private readonly _ahora = signal(new Date());
  readonly ahora = this._ahora.asReadonly();

  private temporizador: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    const documento = inject(DOCUMENT);
    const ventana = documento.defaultView;
    merge(
      fromEvent(documento, 'visibilitychange').pipe(
        filter(() => documento.visibilityState === 'visible'),
      ),
      ventana ? fromEvent(ventana, 'focus') : EMPTY,
    )
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.actualizar());

    inject(DestroyRef).onDestroy(() => clearTimeout(this.temporizador));
    this.programarMedianoche();
  }

  private actualizar(): void {
    this._ahora.set(new Date());
    this.programarMedianoche();
  }

  /** Uno solo a la vez: cada actualización vuelve a contar hasta la medianoche siguiente */
  private programarMedianoche(): void {
    clearTimeout(this.temporizador);
    this.temporizador = setTimeout(
      () => this.actualizar(),
      msHastaMedianocheMadrid(new Date()) + MARGEN_MEDIANOCHE_MS,
    );
  }
}
