import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Perfil } from '../interfaces/perfil.interface';
import { PerfilService } from './perfil.service';

describe('PerfilService', () => {
  let service: PerfilService;
  let http: HttpTestingController;

  const perfil: Perfil = {
    titulo: 'Perfil de ejemplo',
    fechaModificacion: '2026-11-06T10:00:00Z',
    introduccion: null,
    secciones: [{ titulo: 'Filtros de búsqueda', markdown: '- Remoto' }],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PerfilService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('pide el perfil y marca «cargando» mientras espera', () => {
    service.cargar();

    const peticion = http.expectOne('/api/perfil');
    expect(peticion.request.method).toBe('GET');
    expect(service.cargando()).toBe(true);
    peticion.flush(perfil);

    expect(service.perfil()).toEqual(perfil);
    expect(service.cargando()).toBe(false);
    expect(service.error()).toBeNull();
  });

  it('un 409 deja el mensaje de la API y quita el perfil anterior', () => {
    service.cargar();
    http.expectOne('/api/perfil').flush(perfil);

    service.cargar();
    http
      .expectOne('/api/perfil')
      .flush(
        { detail: 'No encuentro tu carpeta Curriculum.' },
        { status: 409, statusText: 'Conflict' },
      );

    expect(service.error()).toBe('No encuentro tu carpeta Curriculum.');
    expect(service.perfil()).toBeNull();
    expect(service.cargando()).toBe(false);
  });

  it('al pedirlo otra vez cancela la petición en curso', () => {
    service.cargar();
    const primera = http.expectOne('/api/perfil');

    service.cargar();

    expect(primera.cancelled).toBe(true);
    http.expectOne('/api/perfil').flush(perfil);
  });
});
