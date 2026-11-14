import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Estadisticas } from '../interfaces/estadisticas.interface';
import { EstadisticasService } from './estadisticas.service';

const VACIAS: Estadisticas = {
  total: 0,
  porEstado: [],
  enviadasPorSemana: [],
  respuesta: { enviadas: 0, conRespuesta: 0, porcentaje: null, diasMedios: null, casosDias: 0 },
  motivosDescarte: [],
};

describe('EstadisticasService', () => {
  let service: EstadisticasService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(EstadisticasService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('pide GET /api/estadisticas y guarda la respuesta', () => {
    service.cargar();
    expect(service.cargando()).toBe(true);

    const request = http.expectOne('/api/estadisticas');
    expect(request.request.method).toBe('GET');
    request.flush(VACIAS);

    expect(service.cargando()).toBe(false);
    expect(service.estadisticas()).toEqual(VACIAS);
    expect(service.error()).toBeNull();
  });

  it('si falla, guarda el mensaje y no deja datos viejos', () => {
    service.cargar();
    http.expectOne('/api/estadisticas').flush(VACIAS);

    service.cargar();
    http.expectOne('/api/estadisticas').error(new ProgressEvent('error'));

    expect(service.estadisticas()).toBeNull();
    expect(service.error()).toBeTruthy();
    expect(service.cargando()).toBe(false);
  });
});
