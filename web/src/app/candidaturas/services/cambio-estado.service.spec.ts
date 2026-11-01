import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { AccionAviso, AvisosService } from '../../shared/services/avisos.service';
import { DatosCambioEstado } from '../components/dialogo-cambio-estado/dialogo-cambio-estado';
import { CambiarEstado, CandidaturaDetalle } from '../interfaces/candidatura.interface';
import { crearDetalle, crearResumen } from '../utils/candidatura-de-prueba';
import { CambioEstadoService } from './cambio-estado.service';
import { CandidaturasService } from './candidaturas.service';

describe('CambioEstadoService', () => {
  let service: CambioEstadoService;
  let cambiarEstado: ReturnType<typeof vi.fn>;
  let abrirDialogo: ReturnType<typeof vi.fn>;
  let avisos: { exito: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let navegar: ReturnType<typeof vi.fn>;

  const CANDIDATURA = {
    id: 7,
    empresa: 'Acme',
    puesto: 'Desarrollador Angular',
    estado: 'Preparada' as const,
  };

  afterEach(() => vi.useRealTimers());

  beforeEach(() => {
    cambiarEstado = vi.fn(() => of(crearDetalle()));
    abrirDialogo = vi.fn();
    avisos = { exito: vi.fn(), error: vi.fn() };
    navegar = vi.fn(() => Promise.resolve(true));

    TestBed.configureTestingModule({
      providers: [
        { provide: CandidaturasService, useValue: { cambiarEstado } },
        { provide: MatDialog, useValue: { open: abrirDialogo } },
        { provide: AvisosService, useValue: avisos },
        { provide: Router, useValue: { navigate: navegar } },
      ],
    });
    service = TestBed.inject(CambioEstadoService);
  });

  /** El diálogo se cierra con esta respuesta (undefined: Cancelar) */
  function responderDialogo(peticion: CambiarEstado | undefined): void {
    abrirDialogo.mockReturnValue({ afterClosed: () => of(peticion) });
  }

  function accionDelAviso(): AccionAviso {
    return avisos.exito.mock.calls[0][1] as AccionAviso;
  }

  describe('avanzar o reabrir', () => {
    it('va en un toque: sin diálogo y con el estado como única petición', () => {
      service.mover(CANDIDATURA, 'Entrevista');

      expect(abrirDialogo).not.toHaveBeenCalled();
      expect(cambiarEstado).toHaveBeenCalledWith(7, { estado: 'Entrevista' });
    });

    it('reabrir tampoco pide confirmación', () => {
      service.mover({ ...CANDIDATURA, estado: 'Descartada' }, 'Detectada');

      expect(abrirDialogo).not.toHaveBeenCalled();
      expect(cambiarEstado).toHaveBeenCalledWith(7, { estado: 'Detectada' });
    });

    it('avisa del cambio y ofrece «Añadir nota» para comentar después', () => {
      service.mover(CANDIDATURA, 'Entrevista');

      expect(avisos.exito).toHaveBeenCalledWith(
        '«Desarrollador Angular» pasa a Entrevista.',
        expect.objectContaining({ texto: 'Añadir nota' }),
      );
    });

    it('al marcar enviada avisa del seguimiento que programa la API', () => {
      service.mover(CANDIDATURA, 'Enviada');

      expect(avisos.exito.mock.calls[0][0]).toBe(
        '«Desarrollador Angular» pasa a Enviada. Seguimiento programado dentro de 7 días.',
      );
    });

    it('«Añadir nota» lleva a la ficha y pide el foco en la nota hasta que la ficha lo atiende', () => {
      service.mover(CANDIDATURA, 'Entrevista');

      accionDelAviso().alPulsar();

      expect(navegar).toHaveBeenCalledWith(['/candidaturas', 7]);
      expect(service.notaPedida()).toBe(7);

      service.notaAtendida();
      expect(service.notaPedida()).toBeNull();
    });
  });

  describe('descartar y cerrar', () => {
    function datosDelDialogo(): DatosCambioEstado {
      return (abrirDialogo.mock.calls[0][1] as { data: DatosCambioEstado }).data;
    }

    it('se confirman en el diálogo, con el estado de origen y el de destino', () => {
      responderDialogo(undefined);

      service.mover(CANDIDATURA, 'Descartada');

      expect(datosDelDialogo()).toEqual({
        empresa: 'Acme',
        puesto: 'Desarrollador Angular',
        actual: 'Preparada',
        destino: 'Descartada',
      });
    });

    it('si se cancela no se toca nada', () => {
      responderDialogo(undefined);

      service.mover(CANDIDATURA, 'Cerrada');

      expect(cambiarEstado).not.toHaveBeenCalled();
      expect(avisos.exito).not.toHaveBeenCalled();
    });

    it('al confirmar manda lo que devuelve el diálogo, con motivo y comentario', () => {
      const peticion: CambiarEstado = {
        estado: 'Descartada',
        motivoDescarte: 'Ingles',
        comentario: 'Piden C1',
      };
      responderDialogo(peticion);

      service.mover(CANDIDATURA, 'Descartada');

      expect(cambiarEstado).toHaveBeenCalledWith(7, peticion);
    });

    it('el diálogo no se cierra solo con Esc o clic fuera: decide él si hay algo escrito', () => {
      responderDialogo(undefined);

      service.mover(CANDIDATURA, 'Descartada');

      expect(abrirDialogo.mock.calls[0][1]).toMatchObject({ disableClose: true });
    });

    it('con el diálogo abierto no se abre otro para la misma candidatura', () => {
      abrirDialogo.mockReturnValue({ afterClosed: () => new Subject() });

      service.mover(CANDIDATURA, 'Descartada');
      service.mover(CANDIDATURA, 'Descartada');

      expect(abrirDialogo).toHaveBeenCalledTimes(1);
    });

    it('al cancelar queda libre: se puede volver a intentar en el momento', () => {
      responderDialogo(undefined);

      service.mover(CANDIDATURA, 'Descartada');
      service.mover(CANDIDATURA, 'Descartada');

      expect(abrirDialogo).toHaveBeenCalledTimes(2);
    });

    it('si ya se comentó en el diálogo, el aviso no ofrece la nota', () => {
      responderDialogo({ estado: 'Cerrada', comentario: 'La han retirado' });

      service.mover(CANDIDATURA, 'Cerrada');

      expect(avisos.exito).toHaveBeenCalledWith('«Desarrollador Angular» pasa a Cerrada.');
    });

    it('sin comentario, el aviso sí la ofrece', () => {
      responderDialogo({ estado: 'Cerrada', comentario: null });

      service.mover(CANDIDATURA, 'Cerrada');

      expect(accionDelAviso().texto).toBe('Añadir nota');
    });
  });

  describe('mientras responde la API', () => {
    it('«cambiando» está activo hasta que llega la respuesta', () => {
      const respuesta = new Subject<CandidaturaDetalle>();
      cambiarEstado.mockReturnValue(respuesta);

      service.mover(CANDIDATURA, 'Enviada');
      expect(service.cambiando()).toBe(true);

      respuesta.next(crearDetalle());
      expect(service.cambiando()).toBe(false);
    });

    it('si la API lo rechaza, enseña su mensaje y deja de esperar', () => {
      const error: ErrorApi = {
        estado: 409,
        mensaje: 'No se puede volver de «Enviada» a «Detectada»: los estados solo avanzan.',
        errores: {},
      };
      cambiarEstado.mockReturnValue(throwError(() => error));

      service.mover(CANDIDATURA, 'Enviada');

      expect(avisos.error).toHaveBeenCalledWith(error.mensaje);
      expect(avisos.exito).not.toHaveBeenCalled();
      expect(service.cambiando()).toBe(false);
    });
  });

  describe('un movimiento a la vez por candidatura', () => {
    const ERROR: ErrorApi = { estado: 500, mensaje: 'Ha fallado.', errores: {} };

    it('ignora un segundo movimiento de la misma tarjeta mientras espera a la API', () => {
      cambiarEstado.mockReturnValue(new Subject<CandidaturaDetalle>());

      service.mover(CANDIDATURA, 'Enviada');
      service.mover(CANDIDATURA, 'Oferta');

      expect(cambiarEstado).toHaveBeenCalledTimes(1);
      expect(cambiarEstado).toHaveBeenCalledWith(7, { estado: 'Enviada' });
    });

    it('no frena a otra tarjeta', () => {
      cambiarEstado.mockReturnValue(new Subject<CandidaturaDetalle>());

      service.mover(CANDIDATURA, 'Enviada');
      service.mover({ ...CANDIDATURA, id: 8 }, 'Oferta');

      expect(cambiarEstado).toHaveBeenCalledTimes(2);
    });

    it('tampoco un doble clic con la respuesta ya recibida, hasta pasados 500 ms', () => {
      vi.useFakeTimers();

      service.mover(CANDIDATURA, 'Enviada');
      vi.advanceTimersByTime(499);
      service.mover({ ...CANDIDATURA, estado: 'Enviada' }, 'Enviada');
      expect(cambiarEstado).toHaveBeenCalledTimes(1);

      vi.advanceTimersByTime(1);
      service.mover({ ...CANDIDATURA, estado: 'Enviada' }, 'Entrevista');
      expect(cambiarEstado).toHaveBeenCalledTimes(2);
    });

    it('«bloqueado» sigue activo durante esa pausa, también con otra tarjeta ya libre', () => {
      vi.useFakeTimers();

      service.mover(CANDIDATURA, 'Enviada');
      expect(service.cambiando()).toBe(false);
      expect(service.bloqueado()).toBe(true);

      vi.advanceTimersByTime(500);
      expect(service.bloqueado()).toBe(false);
    });

    it('si la API falla la pausa también acaba: nada se queda bloqueado para siempre', () => {
      vi.useFakeTimers();
      cambiarEstado.mockReturnValue(throwError(() => ERROR));

      service.mover(CANDIDATURA, 'Enviada');
      expect(service.bloqueado()).toBe(true);

      vi.advanceTimersByTime(500);
      expect(service.bloqueado()).toBe(false);
      service.mover(CANDIDATURA, 'Enviada');
      expect(cambiarEstado).toHaveBeenCalledTimes(2);
    });

    it('no deja temporizadores vivos al destruirse', () => {
      vi.useFakeTimers();
      service.mover(CANDIDATURA, 'Enviada');
      expect(vi.getTimerCount()).toBe(1);

      TestBed.resetTestingModule();

      expect(vi.getTimerCount()).toBe(0);
    });
  });

  describe('cómo acabó el movimiento', () => {
    it('avisa de que se ha hecho', () => {
      const alTerminar = vi.fn();

      service.mover(CANDIDATURA, 'Enviada', alTerminar);

      expect(alTerminar.mock.calls).toEqual([['hecho']]);
    });

    it('avisa del rechazo después de que la API haya respondido', () => {
      const respuesta = new Subject<CandidaturaDetalle>();
      cambiarEstado.mockReturnValue(respuesta);
      const alTerminar = vi.fn();

      service.mover(CANDIDATURA, 'Enviada', alTerminar);
      expect(alTerminar).not.toHaveBeenCalled();

      respuesta.error({ estado: 409, mensaje: 'No.', errores: {} } satisfies ErrorApi);
      expect(alTerminar.mock.calls).toEqual([['rechazado']]);
    });

    it('avisa de que se canceló el diálogo', () => {
      responderDialogo(undefined);
      const alTerminar = vi.fn();

      service.mover(CANDIDATURA, 'Cerrada', alTerminar);

      expect(alTerminar.mock.calls).toEqual([['cancelado']]);
    });

    it('cuenta como cancelado el movimiento ignorado', () => {
      cambiarEstado.mockReturnValue(new Subject<CandidaturaDetalle>());
      const alTerminar = vi.fn();
      service.mover(CANDIDATURA, 'Enviada');

      service.mover(CANDIDATURA, 'Oferta', alTerminar);

      expect(alTerminar.mock.calls).toEqual([['cancelado']]);
    });
  });
});

// Con el servicio real y la API simulada: el problema se veía en las peticiones que salían
describe('CambioEstadoService con CandidaturasService', () => {
  let service: CambioEstadoService;
  let candidaturas: CandidaturasService;
  let http: HttpTestingController;
  let avisos: { exito: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  const TARJETA = crearResumen({ id: 1, estado: 'Preparada' });

  beforeEach(() => {
    avisos = { exito: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialog, useValue: { open: vi.fn() } },
        { provide: AvisosService, useValue: avisos },
        { provide: Router, useValue: { navigate: vi.fn() } },
      ],
    });
    service = TestBed.inject(CambioEstadoService);
    candidaturas = TestBed.inject(CandidaturasService);
    http = TestBed.inject(HttpTestingController);

    candidaturas.cargar();
    http.expectOne('/api/candidaturas').flush([TARJETA]);
  });

  afterEach(() => http.verify());

  const estadoEnLista = () => candidaturas.candidaturas()[0].estado;
  const peticionEstado = () => http.expectOne('/api/candidaturas/1/estado');

  it('dos movimientos seguidos con la API lenta y fallando no dejan una columna falsa', () => {
    service.mover(TARJETA, 'Enviada');
    service.mover({ ...TARJETA, estado: 'Enviada' }, 'Oferta');

    expect(estadoEnLista()).toBe('Enviada');
    peticionEstado().flush({}, { status: 500, statusText: 'Server Error' });

    expect(estadoEnLista()).toBe('Preparada');
  });

  it('doble clic en «Mover a…» con la API lenta: un solo POST y ningún 409', () => {
    service.mover(TARJETA, 'Enviada');
    service.mover(TARJETA, 'Enviada');

    peticionEstado().flush(crearDetalle({ id: 1, estado: 'Enviada' }));
    http.expectOne('/api/candidaturas').flush([{ ...TARJETA, estado: 'Enviada' }]);

    expect(avisos.error).not.toHaveBeenCalled();
    expect(avisos.exito).toHaveBeenCalledTimes(1);
  });

  it('doble clic en «Mover a…» con la API rápida: el segundo clic llega ya respondido', () => {
    service.mover(TARJETA, 'Enviada');
    peticionEstado().flush(crearDetalle({ id: 1, estado: 'Enviada' }));
    http.expectOne('/api/candidaturas').flush([{ ...TARJETA, estado: 'Enviada' }]);

    service.mover(TARJETA, 'Enviada');

    http.expectNone('/api/candidaturas/1/estado');
    expect(avisos.error).not.toHaveBeenCalled();
  });
});
