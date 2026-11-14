import { inject, Injectable } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { filter, Observable, switchMap } from 'rxjs';
import {
  Recordatorio,
  TipoRecordatorio,
} from '../../candidaturas/interfaces/candidatura.interface';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { CandidaturaBreve } from '../../hoy/interfaces/panel-hoy.interface';
import { confirmar } from '../../shared/components/dialogo-confirmacion/dialogo-confirmacion';
import { AvisosService } from '../../shared/services/avisos.service';
import { diaCorto, diaMadrid } from '../../shared/utils/fechas-madrid';
import { abrirDialogoRecordatorio } from '../components/dialogo-recordatorio/dialogo-recordatorio';
import { RecordatoriosService } from './recordatorios.service';

/** Más que el tiempo entre los dos clics de un doble clic (el sistema suele admitir hasta 500 ms) */
const PAUSA_TRAS_CAMBIO_MS = 500;

/**
 * Lo que se hace con un recordatorio desde su tarjeta, igual en Hoy y en la ficha: cada acción manda
 * el cambio y enseña su aviso. «Hecho» y «Posponer» van sin preguntar y se pueden deshacer desde el
 * aviso (o con Ctrl+Z); borrar pide confirmación, porque no se puede deshacer.
 *
 * Tras un cambio que mueve tarjetas hay una pausa corta: si la API responde antes del segundo clic de
 * un doble clic, ese clic caería en la tarjeta que ha subido a ese sitio y no debe contar.
 */
@Injectable({ providedIn: 'root' })
export class AccionesRecordatorioService {
  private readonly recordatorios = inject(RecordatoriosService);
  private readonly avisos = inject(AvisosService);
  private readonly dialog = inject(MatDialog);

  private pausaHasta = 0;

  /**
   * Lo marca como hecho. `alTerminar` deja a la pantalla recolocar el foco: la tarjeta cambia de sitio.
   * `alDeshacer`, si el «Deshacer» del aviso lo devuelve a pendiente, para que la pantalla lo vuelva a
   * enseñar como estaba
   */
  hecho(recordatorio: Recordatorio, alTerminar?: () => void, alDeshacer?: () => void): void {
    if (this.enPausa()) {
      return;
    }
    this.recordatorios.marcarHecho(recordatorio, true).subscribe({
      next: () => {
        this.empezarPausa();
        this.avisos.deshacible(`Hecho: «${recordatorio.titulo}».`, () =>
          this.mandar(
            recordatorio,
            () => this.recordatorios.marcarHecho(recordatorio, false),
            'Vuelve a estar pendiente.',
            alDeshacer,
          ),
        );
        alTerminar?.();
      },
      error: (error: ErrorApi) => this.avisos.error(error.mensaje),
    });
  }

  /** «Pendiente otra vez» de algo ya hecho */
  pendiente(recordatorio: Recordatorio, alTerminar?: () => void): void {
    if (this.enPausa()) {
      return;
    }
    this.recordatorios.marcarHecho(recordatorio, false).subscribe({
      next: () => {
        this.empezarPausa();
        this.avisos.exito(`«${recordatorio.titulo}» vuelve a estar pendiente.`);
        alTerminar?.();
      },
      error: (error: ErrorApi) => this.avisos.error(error.mensaje),
    });
  }

  /** Lo pasa al día `dia` («2026-11-14») a la misma hora. Deshacerlo lo devuelve a su día */
  posponer(recordatorio: Recordatorio, dia: string, alTerminar?: () => void): void {
    if (this.enPausa()) {
      return;
    }
    const diaOriginal = diaMadrid(recordatorio.fechaHora);
    this.recordatorios.posponer(recordatorio, dia).subscribe({
      next: (pospuesto) => {
        this.empezarPausa();
        this.avisos.deshacible(`Pospuesto al ${diaCorto(dia)}.`, () =>
          this.mandar(
            pospuesto,
            () => this.recordatorios.posponer(pospuesto, diaOriginal),
            `Vuelve al ${diaCorto(diaOriginal)}.`,
          ),
        );
        alTerminar?.();
      },
      error: (error: ErrorApi) => this.avisos.error(error.mensaje),
    });
  }

  /** Abre el diálogo para apuntar uno nuevo: suelto o, con `candidatura`, de ella */
  nuevo(
    tipoInicial: TipoRecordatorio,
    candidatura?: CandidaturaBreve,
  ): Observable<Recordatorio | undefined> {
    return abrirDialogoRecordatorio(this.dialog, { tipoInicial, candidatura });
  }

  /** Abre el diálogo para cambiarlo (también la fecha y la hora) */
  editar(
    recordatorio: Recordatorio,
    candidatura?: CandidaturaBreve | null,
  ): Observable<Recordatorio | undefined> {
    return abrirDialogoRecordatorio(this.dialog, { recordatorio, candidatura });
  }

  /** Pregunta y lo borra. `alBorrar` deja a la pantalla recolocar el foco: la tarjeta desaparece */
  borrar(recordatorio: Recordatorio, alBorrar?: () => void): void {
    confirmar(this.dialog, {
      titulo: 'Borrar recordatorio',
      mensaje: `Se borrará «${recordatorio.titulo}» y no se puede deshacer.`,
      confirmar: 'Borrar',
    })
      .pipe(
        filter(Boolean),
        switchMap(() => this.recordatorios.borrar(recordatorio)),
      )
      .subscribe({
        next: () => {
          this.avisos.exito('Recordatorio borrado.');
          alBorrar?.();
        },
        error: (error: ErrorApi) => this.avisos.error(error.mensaje),
      });
  }

  /**
   * El «Deshacer» de un aviso: devuelve false, sin mandar nada, si ese recordatorio tiene otra
   * operación en marcha (el aviso pide entonces esperar)
   */
  private mandar(
    recordatorio: Recordatorio,
    peticion: () => Observable<Recordatorio>,
    mensaje: string,
    alTerminar?: () => void,
  ): boolean {
    if (this.recordatorios.ocupado(recordatorio.id)) {
      return false;
    }
    peticion().subscribe({
      next: () => {
        this.avisos.exito(mensaje);
        alTerminar?.();
      },
      error: (error: ErrorApi) => this.avisos.error(error.mensaje),
    });
    return true;
  }

  private enPausa(): boolean {
    return Date.now() < this.pausaHasta;
  }

  private empezarPausa(): void {
    this.pausaHasta = Date.now() + PAUSA_TRAS_CAMBIO_MS;
  }
}
