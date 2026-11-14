import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { EMPTY, of, Subject, throwError } from 'rxjs';
import { Recordatorio } from '../../candidaturas/interfaces/candidatura.interface';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { AvisosService } from '../../shared/services/avisos.service';
import { DialogoRecordatorio } from '../components/dialogo-recordatorio/dialogo-recordatorio';
import { crearRecordatorio } from '../utils/recordatorio-de-prueba';
import { AccionesRecordatorioService } from './acciones-recordatorio.service';
import { RecordatoriosService } from './recordatorios.service';

describe('AccionesRecordatorioService', () => {
  let service: AccionesRecordatorioService;
  let marcarHecho: ReturnType<typeof vi.fn>;
  let posponer: ReturnType<typeof vi.fn>;
  let borrar: ReturnType<typeof vi.fn>;
  let ocupado: ReturnType<typeof vi.fn>;
  let abrirDialogo: ReturnType<typeof vi.fn>;
  let avisos: Record<'exito' | 'error' | 'deshacible', ReturnType<typeof vi.fn>>;

  // Viernes 13/11/2026 a las 15:00 de Madrid
  const RECORDATORIO = crearRecordatorio({ id: 5, fechaHora: '2026-11-13T14:00:00Z' });
  const ERROR: ErrorApi = { estado: 409, mensaje: 'Ya está hecho.', errores: {} };

  beforeEach(() => {
    marcarHecho = vi.fn((r: Recordatorio, hecho: boolean) => of({ ...r, hecho }));
    posponer = vi.fn((r: Recordatorio) => of({ ...r, fechaHora: '2026-11-14T14:00:00Z' }));
    borrar = vi.fn(() => of(undefined));
    ocupado = vi.fn(() => false);
    abrirDialogo = vi.fn(() => ({ afterClosed: () => of(undefined) }));
    avisos = { exito: vi.fn(), error: vi.fn(), deshacible: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        { provide: RecordatoriosService, useValue: { marcarHecho, posponer, borrar, ocupado } },
        { provide: MatDialog, useValue: { open: abrirDialogo } },
        { provide: AvisosService, useValue: avisos },
      ],
    });
    service = TestBed.inject(AccionesRecordatorioService);
  });

  afterEach(() => vi.useRealTimers());

  function deshacerDelAviso(): () => boolean {
    return avisos.deshacible.mock.calls[0][1] as () => boolean;
  }

  describe('hecho', () => {
    it('lo marca y enseña el aviso con «Deshacer»', () => {
      const alTerminar = vi.fn();

      service.hecho(RECORDATORIO, alTerminar);

      expect(marcarHecho).toHaveBeenCalledWith(RECORDATORIO, true);
      expect(avisos.deshacible).toHaveBeenCalledWith(
        'Hecho: «Publicar en LinkedIn».',
        expect.any(Function),
      );
      expect(alTerminar).toHaveBeenCalledTimes(1);
    });

    it('«Deshacer» lo deja pendiente otra vez', () => {
      service.hecho(RECORDATORIO);

      expect(deshacerDelAviso()()).toBe(true);

      expect(marcarHecho).toHaveBeenLastCalledWith(RECORDATORIO, false);
      expect(avisos.exito).toHaveBeenCalledWith('Vuelve a estar pendiente.');
    });

    it('al deshacer avisa a la pantalla, para que lo vuelva a enseñar como estaba', () => {
      const alDeshacer = vi.fn();
      service.hecho(RECORDATORIO, undefined, alDeshacer);
      expect(alDeshacer).not.toHaveBeenCalled();

      deshacerDelAviso()();

      expect(alDeshacer).toHaveBeenCalledTimes(1);
    });

    it('si deshacer falla, no avisa a la pantalla', () => {
      const alDeshacer = vi.fn();
      service.hecho(RECORDATORIO, undefined, alDeshacer);
      marcarHecho.mockReturnValue(throwError(() => ERROR));

      deshacerDelAviso()();

      expect(alDeshacer).not.toHaveBeenCalled();
      expect(avisos.error).toHaveBeenCalledWith('Ya está hecho.');
    });

    it('«Deshacer» con otra operación de ese recordatorio en marcha pide esperar y no manda nada', () => {
      service.hecho(RECORDATORIO);
      ocupado.mockReturnValue(true);

      expect(deshacerDelAviso()()).toBe(false);

      expect(marcarHecho).toHaveBeenCalledTimes(1);
    });

    it('si la API lo rechaza, enseña su mensaje y no hay nada que deshacer', () => {
      marcarHecho.mockReturnValue(throwError(() => ERROR));
      const alTerminar = vi.fn();

      service.hecho(RECORDATORIO, alTerminar);

      expect(avisos.error).toHaveBeenCalledWith('Ya está hecho.');
      expect(avisos.deshacible).not.toHaveBeenCalled();
      expect(alTerminar).not.toHaveBeenCalled();
    });

    it('si el servicio no la manda (ya había otra en marcha), no enseña ningún aviso', () => {
      marcarHecho.mockReturnValue(EMPTY);

      service.hecho(RECORDATORIO);

      expect(avisos.deshacible).not.toHaveBeenCalled();
      expect(avisos.error).not.toHaveBeenCalled();
    });

    it('el segundo clic de un doble clic no marca la tarjeta que ha subido a ese sitio', () => {
      vi.useFakeTimers();
      const siguiente = crearRecordatorio({ id: 6, titulo: 'El siguiente' });

      service.hecho(RECORDATORIO);
      vi.advanceTimersByTime(200);
      service.hecho(siguiente);
      service.posponer(siguiente, '2026-11-14');

      expect(marcarHecho).toHaveBeenCalledTimes(1);
      expect(posponer).not.toHaveBeenCalled();

      vi.advanceTimersByTime(400);
      service.hecho(siguiente);

      expect(marcarHecho).toHaveBeenLastCalledWith(siguiente, true);
    });

    it('la pausa no empieza hasta que responde la API', () => {
      const respuesta = new Subject<Recordatorio>();
      marcarHecho.mockReturnValueOnce(respuesta);

      service.hecho(RECORDATORIO);
      service.hecho(crearRecordatorio({ id: 6 }));

      expect(marcarHecho).toHaveBeenCalledTimes(2);
    });
  });

  describe('pendiente', () => {
    it('lo deja pendiente y lo dice', () => {
      const alTerminar = vi.fn();

      service.pendiente({ ...RECORDATORIO, hecho: true }, alTerminar);

      expect(marcarHecho).toHaveBeenCalledWith({ ...RECORDATORIO, hecho: true }, false);
      expect(avisos.exito).toHaveBeenCalledWith('«Publicar en LinkedIn» vuelve a estar pendiente.');
      expect(alTerminar).toHaveBeenCalled();
    });

    it('si falla, enseña el mensaje', () => {
      marcarHecho.mockReturnValue(throwError(() => ERROR));

      service.pendiente(RECORDATORIO);

      expect(avisos.error).toHaveBeenCalledWith('Ya está hecho.');
    });
  });

  describe('posponer', () => {
    it('lo pasa al día elegido y el aviso dice a cuál', () => {
      const alTerminar = vi.fn();

      service.posponer(RECORDATORIO, '2026-11-14', alTerminar);

      expect(posponer).toHaveBeenCalledWith(RECORDATORIO, '2026-11-14');
      expect(avisos.deshacible).toHaveBeenCalledWith(
        'Pospuesto al sáb 14 nov.',
        expect.any(Function),
      );
      expect(alTerminar).toHaveBeenCalled();
    });

    it('«Deshacer» lo devuelve a su día original', () => {
      service.posponer(RECORDATORIO, '2026-11-14');

      expect(deshacerDelAviso()()).toBe(true);

      expect(posponer).toHaveBeenLastCalledWith(
        expect.objectContaining({ id: 5, fechaHora: '2026-11-14T14:00:00Z' }),
        '2026-11-13',
      );
      expect(avisos.exito).toHaveBeenCalledWith('Vuelve al vie 13 nov.');
    });

    it('el día original es el de Madrid, no el de UTC', () => {
      // 23:30Z del 12 son las 00:30 del 13 en Madrid
      service.posponer(crearRecordatorio({ fechaHora: '2026-11-12T23:30:00Z' }), '2026-11-14');

      deshacerDelAviso()();

      expect(posponer).toHaveBeenLastCalledWith(expect.anything(), '2026-11-13');
    });

    it('si falla, enseña el mensaje', () => {
      posponer.mockReturnValue(throwError(() => ERROR));

      service.posponer(RECORDATORIO, '2026-11-14');

      expect(avisos.error).toHaveBeenCalledWith('Ya está hecho.');
      expect(avisos.deshacible).not.toHaveBeenCalled();
    });
  });

  describe('nuevo y editar', () => {
    const CANDIDATURA = {
      id: 1,
      empresa: 'Acme',
      puesto: 'Dev',
      estado: 'Entrevista' as const,
      enlace: null,
    };

    it('nuevo abre el diálogo con el tipo inicial y la candidatura', () => {
      service.nuevo('Entrevista', CANDIDATURA).subscribe();

      const [componente, opciones] = abrirDialogo.mock.calls[0];
      expect(componente).toBe(DialogoRecordatorio);
      expect(opciones.data).toEqual({ tipoInicial: 'Entrevista', candidatura: CANDIDATURA });
      expect(opciones.disableClose).toBe(true);
    });

    it('editar abre el diálogo con el recordatorio', () => {
      service.editar(RECORDATORIO, null).subscribe();

      expect(abrirDialogo.mock.calls[0][1].data).toEqual({
        recordatorio: RECORDATORIO,
        candidatura: null,
      });
    });
  });

  describe('borrar', () => {
    it('pregunta antes y no borra si se cancela', () => {
      abrirDialogo.mockReturnValue({ afterClosed: () => of(false) });

      service.borrar(RECORDATORIO);

      expect(abrirDialogo.mock.calls[0][1].data.mensaje).toContain('«Publicar en LinkedIn»');
      expect(borrar).not.toHaveBeenCalled();
    });

    it('si se confirma, lo borra y avisa', () => {
      abrirDialogo.mockReturnValue({ afterClosed: () => of(true) });
      const alBorrar = vi.fn();

      service.borrar(RECORDATORIO, alBorrar);

      expect(borrar).toHaveBeenCalledWith(RECORDATORIO);
      expect(avisos.exito).toHaveBeenCalledWith('Recordatorio borrado.');
      expect(alBorrar).toHaveBeenCalled();
    });

    it('si falla, enseña el mensaje', () => {
      abrirDialogo.mockReturnValue({ afterClosed: () => of(true) });
      borrar.mockReturnValue(throwError(() => ERROR));

      service.borrar(RECORDATORIO);

      expect(avisos.error).toHaveBeenCalledWith('Ya está hecho.');
    });
  });
});
