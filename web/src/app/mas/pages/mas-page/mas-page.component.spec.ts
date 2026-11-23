import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { crearSistemaInfo } from '../../../core/utils/sistema-de-prueba';
import MasPageComponent from './mas-page.component';

describe('MasPageComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function pintar(agentesDisponibles: boolean): HTMLElement {
    const fixture = TestBed.createComponent(MasPageComponent);
    fixture.detectChanges();
    http.expectOne('/api/sistema/info').flush(crearSistemaInfo({ agentesDisponibles }));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const rutas = (elemento: HTMLElement) =>
    [...elemento.querySelectorAll('li a')].map((a) => a.getAttribute('href'));

  it('con agentes disponibles, cinco tarjetas: la quinta, «Agentes de Claude»', () => {
    const elemento = pintar(true);

    expect(rutas(elemento)).toEqual([
      '/perfil',
      '/estadisticas',
      '/importacion',
      '/ajustes',
      '/agentes',
    ]);
    expect(elemento.querySelector('a[href="/agentes"]')!.textContent).toContain(
      'Agentes de Claude',
    );
  });

  it('sin agentes disponibles (el móvil, la demo), las cuatro de siempre', () => {
    expect(rutas(pintar(false))).toEqual(['/perfil', '/estadisticas', '/importacion', '/ajustes']);
  });
});
