import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { AvisosService } from '../../shared/services/avisos.service';
import {
  DatosCambioEstado,
  DialogoCambioEstado,
} from '../components/dialogo-cambio-estado/dialogo-cambio-estado';
import {
  CambiarEstado,
  CandidaturaResumen,
  EstadoCandidatura,
} from '../interfaces/candidatura.interface';
import { ETIQUETAS_ESTADO } from '../utils/etiquetas';
import { esEstadoFinal } from '../utils/transiciones-permitidas';
import { CandidaturasService } from './candidaturas.service';

type CandidaturaQueSeMueve = Pick<CandidaturaResumen, 'id' | 'empresa' | 'puesto' | 'estado'>;

/** Cómo acabó un movimiento: `cancelado` también si se ignoró por haber otro de esa candidatura */
export type ResultadoMovimiento = 'hecho' | 'cancelado' | 'rechazado';

/** Tras deshacer, a qué estado ha vuelto la candidatura: para devolverle el foco */
export type AlDeshacer = (estado: EstadoCandidatura) => void;

/** Más que el tiempo entre los dos clics de un doble clic (el sistema suele admitir hasta 500 ms) */
const PAUSA_TRAS_CAMBIO_MS = 500;

/**
 * Mover una candidatura de estado desde el tablero (arrastrando o con «Mover a…») o desde la ficha,
 * igual en los dos sitios:
 * - Avanzar o reabrir va en un toque. El aviso ofrece «Deshacer» (también con Ctrl+Z) por si se ha
 *   movido sin querer y «Añadir nota» por si se quiere dejar un comentario (la API no deja comentar
 *   un cambio ya hecho; la nota sale a su lado en el historial).
 * - Descartar y Cerrar sacan la candidatura del proceso: se confirman en un diálogo, que además
 *   pide el motivo del descarte (obligatorio en la API) y deja comentar.
 *
 * Cada candidatura admite un solo movimiento a la vez, y tras acabar hay una pausa corta: si la API
 * responde antes del segundo clic de un doble clic, ese clic no debe contar como otro cambio.
 */
@Injectable({ providedIn: 'root' })
export class CambioEstadoService {
  private readonly candidaturasService = inject(CandidaturasService);
  private readonly dialog = inject(MatDialog);
  private readonly avisos = inject(AvisosService);
  private readonly router = inject(Router);

  private readonly _enCurso = signal(0);
  /** Hay algún cambio de estado esperando a la API */
  readonly cambiando = computed(() => this._enCurso() > 0);

  private readonly idsEnCurso = new Set<number>();

  private readonly _idsEnPausa = signal<ReadonlySet<number>>(new Set());
  private readonly temporizadores = new Set<ReturnType<typeof setTimeout>>();

  /** Los botones de estado de la ficha no deben hacer nada: hay un cambio en marcha o recién hecho */
  readonly bloqueado = computed(() => this.cambiando() || this._idsEnPausa().size > 0);

  private readonly _notaPedida = signal<number | null>(null);
  /** Candidatura en la que se ha pulsado «Añadir nota» del aviso: la ficha lleva el foco a la nota */
  readonly notaPedida = this._notaPedida.asReadonly();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.temporizadores.forEach((t) => clearTimeout(t)));
  }

  /**
   * `alTerminar` deja a quien mueve reaccionar a cómo acabó (p. ej. colocar el foco), y `alDeshacer`, a que
   * se deshaga desde el aviso
   */
  mover(
    candidatura: CandidaturaQueSeMueve,
    destino: EstadoCandidatura,
    alTerminar?: (resultado: ResultadoMovimiento) => void,
    alDeshacer?: AlDeshacer,
  ): void {
    const { id } = candidatura;
    if (this.idsEnCurso.has(id) || this._idsEnPausa().has(id)) {
      alTerminar?.('cancelado');
      return;
    }
    this.idsEnCurso.add(id);

    if (!esEstadoFinal(destino)) {
      this.enviar(candidatura, { estado: destino }, alTerminar, alDeshacer);
      return;
    }

    this.dialog
      .open<DialogoCambioEstado, DatosCambioEstado, CambiarEstado>(DialogoCambioEstado, {
        data: {
          empresa: candidatura.empresa,
          puesto: candidatura.puesto,
          actual: candidatura.estado,
          destino,
        },
        width: '32rem',
        maxWidth: '95vw',
        disableClose: true,
        // Cerrar solo pide un comentario opcional: con el foco en él, el móvil sacaría el teclado
        autoFocus: destino === 'Descartada' ? 'first-tabbable' : 'first-heading',
      })
      .afterClosed()
      .subscribe((peticion) => {
        if (peticion) {
          this.enviar(candidatura, peticion, alTerminar, alDeshacer);
        } else {
          this.idsEnCurso.delete(id);
          alTerminar?.('cancelado');
        }
      });
  }

  /** La ficha avisa de que ya ha llevado el foco a la nota */
  notaAtendida(): void {
    this._notaPedida.set(null);
  }

  private enviar(
    candidatura: CandidaturaQueSeMueve,
    peticion: CambiarEstado,
    alTerminar?: (resultado: ResultadoMovimiento) => void,
    alDeshacer?: AlDeshacer,
  ): void {
    this._enCurso.update((n) => n + 1);
    this.candidaturasService.cambiarEstado(candidatura.id, peticion).subscribe({
      next: (detalle) => {
        this.terminar(candidatura.id);
        const seguimiento =
          peticion.estado === 'Enviada' ? ' Seguimiento programado dentro de 7 días.' : '';
        const mensaje = `«${candidatura.puesto}» pasa a ${ETIQUETAS_ESTADO[peticion.estado]}.${seguimiento}`;
        // Si ya se comentó en el diálogo, no hace falta ofrecer la nota
        const nota = peticion.comentario
          ? undefined
          : { texto: 'Añadir nota', alPulsar: () => this.pedirNota(candidatura.id) };
        // El apunte que ha creado este movimiento (el historial va del más antiguo al más reciente): se
        // deshace ese y no «el último que haya», que puede ser de otra pestaña
        const cambioId = detalle.historial.at(-1)?.id;
        if (cambioId === undefined) {
          this.avisos.exito(mensaje, nota);
        } else {
          this.avisos.deshacible(
            mensaje,
            () => this.deshacer(candidatura.id, cambioId, alDeshacer),
            nota,
          );
        }
        alTerminar?.('hecho');
      },
      error: (error: ErrorApi) => {
        this.terminar(candidatura.id);
        this.avisos.error(error.mensaje);
        alTerminar?.('rechazado');
      },
    });
  }

  /**
   * «Deshacer» del aviso (o Ctrl+Z): la API vuelve al estado anterior. Cuenta como un movimiento más de
   * esa candidatura, así que no se junta con otro que esté en marcha ni con la pausa del doble clic:
   * entonces devuelve false y el aviso pide esperar
   */
  private deshacer(id: number, cambioId: number, alDeshacer?: AlDeshacer): boolean {
    if (this.idsEnCurso.has(id) || this._idsEnPausa().has(id)) {
      return false;
    }
    this.idsEnCurso.add(id);
    this._enCurso.update((n) => n + 1);
    this.candidaturasService.deshacerCambioEstado(id, cambioId).subscribe({
      next: (detalle) => {
        this.terminar(id);
        this.avisos.exito('Cambio deshecho.');
        alDeshacer?.(detalle.estado);
      },
      error: (error: ErrorApi) => {
        this.terminar(id);
        this.avisos.error(error.mensaje);
      },
    });
    return true;
  }

  /** Vale igual si el cambio falla: la pausa acaba sola y los botones vuelven a funcionar */
  private terminar(id: number): void {
    this._enCurso.update((n) => n - 1);
    this.idsEnCurso.delete(id);

    this._idsEnPausa.update((ids) => new Set(ids).add(id));
    const temporizador = setTimeout(() => {
      this.temporizadores.delete(temporizador);
      this._idsEnPausa.update((ids) => new Set([...ids].filter((otro) => otro !== id)));
    }, PAUSA_TRAS_CAMBIO_MS);
    this.temporizadores.add(temporizador);
  }

  /** Lleva a la ficha (si ya se está en ella, navegar a la misma URL no hace nada) y pide el foco en la nota */
  private pedirNota(id: number): void {
    this._notaPedida.set(id);
    void this.router.navigate(['/candidaturas', id]);
  }
}
