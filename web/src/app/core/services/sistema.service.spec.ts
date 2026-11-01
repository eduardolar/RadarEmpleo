import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SistemaInfo } from '../interfaces/sistema-info.interface';
import { SistemaService } from './sistema.service';

const INFO_LOCAL: SistemaInfo = { modo: 'Local', agentesHabilitados: false, version: '0.1.0' };

describe('SistemaService', () => {
  let service: SistemaService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SistemaService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('empieza comprobando y sin información', () => {
    expect(service.estado()).toBe('comprobando');
    expect(service.info()).toBeNull();
  });

  it('pide GET /api/sistema/info y guarda la respuesta', () => {
    service.cargarInfo();

    const request = http.expectOne('/api/sistema/info');
    expect(request.request.method).toBe('GET');
    expect(service.estado()).toBe('comprobando');

    request.flush(INFO_LOCAL);

    expect(service.estado()).toBe('conectada');
    expect(service.info()).toEqual(INFO_LOCAL);
  });

  it('marca sin conexión y borra la información si la API no responde', () => {
    service.cargarInfo();
    http.expectOne('/api/sistema/info').flush(INFO_LOCAL);

    service.cargarInfo();
    http.expectOne('/api/sistema/info').error(new ProgressEvent('error'));

    expect(service.estado()).toBe('sin-conexion');
    expect(service.info()).toBeNull();
  });

  it('marca sin conexión si la API contesta con un error', () => {
    service.cargarInfo();
    http
      .expectOne('/api/sistema/info')
      .flush('Error', { status: 500, statusText: 'Internal Server Error' });

    expect(service.estado()).toBe('sin-conexion');
  });

  it('cargarInfoSiFalta no repite la petición si la API ya respondió', () => {
    service.cargarInfoSiFalta();
    http.expectOne('/api/sistema/info').flush(INFO_LOCAL);

    service.cargarInfoSiFalta();

    http.expectNone('/api/sistema/info');
    expect(service.estado()).toBe('conectada');
  });

  it('cargarInfoSiFalta vuelve a preguntar si la última vez no hubo conexión', () => {
    service.cargarInfoSiFalta();
    http.expectOne('/api/sistema/info').error(new ProgressEvent('error'));

    service.cargarInfoSiFalta();

    http.expectOne('/api/sistema/info').flush(INFO_LOCAL);
    expect(service.estado()).toBe('conectada');
  });
});
