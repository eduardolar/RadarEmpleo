import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import {
  crearDetalleRastreo,
  crearOferta,
  crearRastreo,
  crearResultadoEscaneo,
} from '../utils/rastreo-de-prueba';
import { RastreosService } from './rastreos.service';

describe('RastreosService', () => {
  let service: RastreosService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RastreosService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Deja abierto un rastreo con dos ofertas pendientes */
  function abrirRastreo(): void {
    service.cargarLista();
    http.expectOne('/api/rastreos').flush([crearRastreo({ id: 1, total: 2, pendientes: 2 })]);
    http.expectOne('/api/rastreos/1').flush(
      crearDetalleRastreo({
        id: 1,
        total: 2,
        pendientes: 2,
        ofertas: [crearOferta({ id: 10 }), crearOferta({ id: 11, linea: 11 })],
      }),
    );
  }

  function oferta(id: number) {
    return service.detalle()!.ofertas.find((o) => o.id === id)!;
  }

  describe('escanear', () => {
    it('manda {} como JSON, guarda el resultado y luego pide la lista', () => {
      service.escanear();

      const peticion = http.expectOne('/api/rastreos/escanear');
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({});
      expect(service.escaneando()).toBe(true);
      const resultado = crearResultadoEscaneo({ ofertasNuevas: 3 });
      peticion.flush(resultado);

      expect(service.escaneando()).toBe(false);
      expect(service.resultado()).toEqual(resultado);
      http.expectOne('/api/rastreos').flush([]);
      expect(service.rastreos()).toEqual([]);
      expect(service.detalle()).toBeNull();
    });

    it('si falla, deja el mensaje y pide la lista igual', () => {
      service.escanear();
      http
        .expectOne('/api/rastreos/escanear')
        .flush(
          { detail: 'No encuentro tu carpeta Curriculum.' },
          { status: 409, statusText: 'Conflict' },
        );

      expect(service.errorEscaneo()).toBe('No encuentro tu carpeta Curriculum.');
      expect(service.escaneando()).toBe(false);
      http.expectOne('/api/rastreos').flush([]);
    });
  });

  describe('cargarLista', () => {
    it('abre el más reciente (el primero de la lista)', () => {
      service.cargarLista();
      http
        .expectOne('/api/rastreos')
        .flush([crearRastreo({ id: 5, fecha: '2026-11-06' }), crearRastreo({ id: 4 })]);

      expect(service.seleccionado()).toBe(5);
      http.expectOne('/api/rastreos/5').flush(crearDetalleRastreo({ id: 5 }));
      expect(service.detalle()?.id).toBe(5);
      expect(service.cargando()).toBe(false);
    });

    it('si se estaba viendo otro que sigue en la lista, se queda en ese', () => {
      service.seleccionar(4);
      http.expectOne('/api/rastreos/4').flush(crearDetalleRastreo({ id: 4 }));

      service.cargarLista();
      http.expectOne('/api/rastreos').flush([crearRastreo({ id: 5 }), crearRastreo({ id: 4 })]);

      expect(service.seleccionado()).toBe(4);
      http.expectOne('/api/rastreos/4').flush(crearDetalleRastreo({ id: 4 }));
    });

    it('un error deja el mensaje', () => {
      service.cargarLista();
      http.expectOne('/api/rastreos').flush(null, { status: 500, statusText: 'Error' });

      expect(service.error()).toContain('error inesperado');
      expect(service.cargando()).toBe(false);
    });
  });

  describe('seleccionar', () => {
    it('quita al momento las ofertas del rastreo anterior', () => {
      abrirRastreo();

      service.seleccionar(2);

      expect(service.detalle()).toBeNull();
      http.expectOne('/api/rastreos/2').flush(crearDetalleRastreo({ id: 2 }));
      expect(service.detalle()?.id).toBe(2);
    });
  });

  describe('detectar', () => {
    it('la marca en el tablero al momento y luego pone la que devuelve la API', () => {
      abrirRastreo();
      let candidaturaId: number | undefined;

      service.detectar(oferta(10)).subscribe((r) => (candidaturaId = r.candidaturaId));

      expect(oferta(10).estado).toBe('Convertida');
      expect(service.ocupadas().has(10)).toBe(true);
      expect(service.detalle()?.pendientes).toBe(1);
      expect(service.rastreos()[0].pendientes).toBe(1);

      const peticion = http.expectOne('/api/rastreos/ofertas/10/detectar');
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({});
      peticion.flush({
        oferta: crearOferta({ id: 10, estado: 'Convertida', candidaturaId: 33 }),
        candidaturaId: 33,
        yaExistia: false,
      });

      expect(candidaturaId).toBe(33);
      expect(oferta(10).candidaturaId).toBe(33);
      expect(service.ocupadas().has(10)).toBe(false);
    });

    it('si falla, la oferta vuelve a como estaba y llega el ErrorApi', () => {
      abrirRastreo();
      let error: ErrorApi | undefined;

      service.detectar(oferta(10)).subscribe({ error: (e: ErrorApi) => (error = e) });
      http
        .expectOne('/api/rastreos/ofertas/10/detectar')
        .flush(null, { status: 0, statusText: '' });

      expect(error?.estado).toBe(0);
      expect(oferta(10).estado).toBe('Pendiente');
      expect(service.rastreos()[0].pendientes).toBe(2);
      expect(service.ocupadas().has(10)).toBe(false);
    });

    it('un doble clic solo manda una petición', () => {
      abrirRastreo();
      const pendiente = oferta(10);

      service.detectar(pendiente).subscribe();
      service.detectar(pendiente).subscribe();

      http.expectOne('/api/rastreos/ofertas/10/detectar').flush({
        oferta: crearOferta({ id: 10, estado: 'Convertida', candidaturaId: 33 }),
        candidaturaId: 33,
        yaExistia: false,
      });
    });

    it('si ya se está viendo otro rastreo, no lo toca', () => {
      abrirRastreo();
      const pendiente = oferta(10);
      service.seleccionar(2);
      http.expectOne('/api/rastreos/2').flush(crearDetalleRastreo({ id: 2, ofertas: [] }));

      service.detectar(pendiente).subscribe();
      http.expectOne('/api/rastreos/ofertas/10/detectar').flush({
        oferta: crearOferta({ id: 10, estado: 'Convertida', candidaturaId: 33 }),
        candidaturaId: 33,
        yaExistia: false,
      });

      expect(service.detalle()?.id).toBe(2);
      expect(service.detalle()?.ofertas).toEqual([]);
    });
  });

  describe('ignorar', () => {
    it('la pliega al momento y se queda con la que devuelve la API', () => {
      abrirRastreo();

      service.ignorar(oferta(11)).subscribe();

      expect(oferta(11).estado).toBe('Ignorada');
      const respuesta = crearOferta({
        id: 11,
        linea: 11,
        estado: 'Ignorada',
        fechaEstado: '2026-11-08T10:00:00Z',
      });
      http.expectOne('/api/rastreos/ofertas/11/ignorar').flush(respuesta);
      expect(oferta(11)).toEqual(respuesta);
    });

    it('un 409 la deja como estaba', () => {
      abrirRastreo();
      let error: ErrorApi | undefined;

      service.ignorar(oferta(11)).subscribe({ error: (e: ErrorApi) => (error = e) });
      http
        .expectOne('/api/rastreos/ofertas/11/ignorar')
        .flush(
          { detail: 'Esta oferta ya está en el tablero.' },
          { status: 409, statusText: 'Conflict' },
        );

      expect(error?.mensaje).toBe('Esta oferta ya está en el tablero.');
      expect(oferta(11).estado).toBe('Pendiente');
    });
  });
});
