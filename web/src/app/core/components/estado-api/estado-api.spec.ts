import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EstadoApi } from './estado-api';

describe('EstadoApi', () => {
  let fixture: ComponentFixture<EstadoApi>;
  let http: HttpTestingController;

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function limpiar(texto: string | null | undefined): string {
    return texto?.replace(/\s+/g, ' ').trim() ?? '';
  }

  function texto(): string {
    return limpiar(elemento().textContent);
  }

  function region(): HTMLElement | null {
    return elemento().querySelector('[role="status"]');
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [EstadoApi],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EstadoApi);
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  it('avisa de que está comprobando mientras espera a la API', () => {
    http.expectOne('/api/sistema/info');

    expect(texto()).toContain('Comprobando la conexión');
  });

  it('muestra «Conectado · modo Local» cuando la API responde', async () => {
    http
      .expectOne('/api/sistema/info')
      .flush({ modo: 'Local', agentesHabilitados: false, version: '0.1.0' });
    await fixture.whenStable();

    expect(texto()).toBe('Conectado · modo Local');
  });

  it('anuncia todos los estados en la misma región role="status"', async () => {
    const regionInicial = region();
    expect(regionInicial?.getAttribute('aria-live')).toBe('polite');
    expect(limpiar(regionInicial?.textContent)).toContain('Comprobando');

    http.expectOne('/api/sistema/info').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(region()).toBe(regionInicial);
    expect(limpiar(regionInicial?.textContent)).toBe('No se puede conectar con RadarEmpleo');
    expect(elemento().querySelectorAll('[role]')).toHaveLength(1);
  });

  it('muestra que no se puede conectar y deja reintentar cuando la API falla', async () => {
    http.expectOne('/api/sistema/info').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(texto()).toContain('No se puede conectar con RadarEmpleo');

    const reintentar = elemento().querySelector('button');
    expect(reintentar?.textContent?.trim()).toBe('Reintentar');
    // Fuera de la región: si no, el lector de pantalla leería «Reintentar» en cada aviso
    expect(region()?.contains(reintentar)).toBe(false);

    reintentar?.click();
    http
      .expectOne('/api/sistema/info')
      .flush({ modo: 'Demo', agentesHabilitados: false, version: '0.1.0' });
    await fixture.whenStable();

    expect(texto()).toBe('Conectado · modo Demo');
  });

  it('lleva el foco al indicador al pulsar «Reintentar» (el botón desaparece)', async () => {
    http.expectOne('/api/sistema/info').error(new ProgressEvent('error'));
    await fixture.whenStable();

    const reintentar = elemento().querySelector('button')!;
    reintentar.focus();
    reintentar.click();
    await fixture.whenStable();

    expect(elemento().querySelector('button')).toBeNull();
    expect(document.activeElement).toBe(region());

    http
      .expectOne('/api/sistema/info')
      .flush({ modo: 'Local', agentesHabilitados: false, version: '0.1.0' });
    await fixture.whenStable();

    expect(document.activeElement).toBe(region());
  });

  it('no vuelve a preguntar ni a mostrar «Comprobando» si la API ya estaba conectada', async () => {
    http
      .expectOne('/api/sistema/info')
      .flush({ modo: 'Local', agentesHabilitados: false, version: '0.1.0' });
    fixture.destroy();

    // Como al salir de Hoy y volver: el servicio es el mismo y el componente, uno nuevo
    fixture = TestBed.createComponent(EstadoApi);
    await fixture.whenStable();

    http.expectNone('/api/sistema/info');
    expect(texto()).toBe('Conectado · modo Local');
  });
});
