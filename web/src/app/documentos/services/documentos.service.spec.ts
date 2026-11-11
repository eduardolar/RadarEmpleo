import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CandidaturasService } from '../../candidaturas/services/candidaturas.service';
import { crearDetalle } from '../../candidaturas/utils/candidatura-de-prueba';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { crearDocumento, crearSugerido } from '../utils/documento-de-prueba';
import { DocumentosService } from './documentos.service';

describe('DocumentosService', () => {
  let service: DocumentosService;
  let candidaturas: CandidaturasService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DocumentosService);
    candidaturas = TestBed.inject(CandidaturasService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function sugeridos(): ReturnType<HttpTestingController['expectOne']> {
    return http.expectOne((r) => r.url === '/api/documentos/sugeridos');
  }

  function abrirFicha(id = 1): void {
    candidaturas.cargarDetalle(id);
    http.expectOne(`/api/candidaturas/${id}`).flush(crearDetalle({ id }));
  }

  describe('cargarSugeridos', () => {
    it('pide los de la candidatura y marca «cargando» mientras espera', () => {
      service.cargarSugeridos(5);

      const peticion = sugeridos();
      expect(peticion.request.method).toBe('GET');
      expect(peticion.request.params.get('candidaturaId')).toBe('5');
      expect(service.cargandoSugeridos()).toBe(true);

      const lista = [crearSugerido()];
      peticion.flush(lista);

      expect(service.cargandoSugeridos()).toBe(false);
      expect(service.sugeridos()).toEqual(lista);
      expect(service.errorSugeridos()).toBeNull();
    });

    it('un 409 deja el mensaje de la API', () => {
      service.cargarSugeridos(5);
      sugeridos().flush(
        { detail: 'No se encuentra la carpeta Curriculum.' },
        { status: 409, statusText: 'Conflict' },
      );

      expect(service.errorSugeridos()).toBe('No se encuentra la carpeta Curriculum.');
      expect(service.cargandoSugeridos()).toBe(false);
    });

    it('al pedir otra vez, borra las anteriores y cancela la petición en curso', () => {
      service.cargarSugeridos(1);
      sugeridos().flush([crearSugerido()]);
      service.cargarSugeridos(2);
      const primera = sugeridos();
      service.cargarSugeridos(3);

      expect(primera.cancelled).toBe(true);
      expect(service.sugeridos()).toEqual([]);
      sugeridos().flush([]);
    });
  });

  describe('vincular', () => {
    it('manda la ruta, marca la sugerencia como vinculada y vuelve a pedir la ficha', () => {
      abrirFicha(1);
      service.cargarSugeridos(1);
      const sugerido = crearSugerido();
      sugeridos().flush([sugerido, crearSugerido({ rutaRelativa: 'Archivo/otro.pdf' })]);
      const documento = crearDocumento({ rutaRelativa: sugerido.rutaRelativa });
      let recibido: unknown;

      service.vincular(1, sugerido.rutaRelativa).subscribe((d) => (recibido = d));

      const peticion = http.expectOne('/api/candidaturas/1/documentos');
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({ rutaRelativa: sugerido.rutaRelativa });
      expect(service.vinculando()).toBe(true);
      peticion.flush(documento, { status: 201, statusText: 'Created' });

      expect(recibido).toEqual(documento);
      expect(service.vinculando()).toBe(false);
      expect(service.sugeridos().map((s) => s.yaVinculado)).toEqual([true, false]);
      http.expectOne('/api/candidaturas/1').flush(crearDetalle({ documentos: [documento] }));
      expect(candidaturas.detalle()?.documentos).toEqual([documento]);
    });

    it('con tipo, lo manda', () => {
      service.vincular(1, 'Archivo/x.pdf', 'Carta').subscribe();

      const peticion = http.expectOne('/api/candidaturas/1/documentos');
      expect(peticion.request.body).toEqual({ rutaRelativa: 'Archivo/x.pdf', tipo: 'Carta' });
      peticion.flush(crearDocumento());
    });

    it('un doble clic solo manda una petición', () => {
      service.vincular(1, 'Archivo/x.pdf').subscribe();
      service.vincular(1, 'Archivo/x.pdf').subscribe();

      http.expectOne('/api/candidaturas/1/documentos').flush(crearDocumento());
    });

    it('si la ficha abierta es otra, no la cambia', () => {
      abrirFicha(2);

      service.vincular(1, 'Archivo/x.pdf').subscribe();
      http.expectOne('/api/candidaturas/1/documentos').flush(crearDocumento());

      http.expectNone('/api/candidaturas/1');
      expect(candidaturas.detalle()?.id).toBe(2);
    });

    it('un 409 llega como ErrorApi y se puede volver a intentar', () => {
      let error: ErrorApi | undefined;

      service.vincular(1, 'Archivo/x.pdf').subscribe({ error: (e: ErrorApi) => (error = e) });
      http
        .expectOne('/api/candidaturas/1/documentos')
        .flush(
          { detail: 'No encuentro el archivo. Puede que se haya movido.' },
          { status: 409, statusText: 'Conflict' },
        );

      expect(error?.estado).toBe(409);
      expect(error?.mensaje).toBe('No encuentro el archivo. Puede que se haya movido.');
      expect(service.vinculando()).toBe(false);
    });
  });

  describe('quitar', () => {
    it('borra el vínculo y vuelve a pedir la ficha', () => {
      abrirFicha(1);
      let terminado = false;

      service
        .quitar(crearDocumento({ id: 7, candidaturaId: 1 }))
        .subscribe(() => (terminado = true));

      const peticion = http.expectOne('/api/documentos/7');
      expect(peticion.request.method).toBe('DELETE');
      peticion.flush(null, { status: 204, statusText: 'No Content' });

      expect(terminado).toBe(true);
      http.expectOne('/api/candidaturas/1').flush(crearDetalle());
    });

    it('un 404 llega como ErrorApi', () => {
      let error: ErrorApi | undefined;

      service.quitar(crearDocumento({ id: 7 })).subscribe({ error: (e: ErrorApi) => (error = e) });
      http.expectOne('/api/documentos/7').flush(null, { status: 404, statusText: 'Not Found' });

      expect(error?.estado).toBe(404);
    });
  });
});
