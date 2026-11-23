import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { crearEstadoAgentes, crearGasto } from '../../../agentes/utils/ejecucion-de-prueba';
import { crearSistemaInfo } from '../../../core/utils/sistema-de-prueba';
import AjustesPageComponent from './ajustes-page.component';

describe('AjustesPageComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function pintar() {
    const fixture = TestBed.createComponent(AjustesPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  const texto = (elemento: HTMLElement) => elemento.textContent!.replace(/\s+/g, ' ');

  const URL_ESTADO = '/api/agentes/estado';

  /** Con agentes disponibles: responde /api/sistema/info y devuelve el bloque de agentes */
  function pintarConAgentes() {
    const fixture = pintar();
    http
      .expectOne('/api/sistema/info')
      .flush(crearSistemaInfo({ agentesDisponibles: true, agentesHabilitados: true }));
    fixture.detectChanges();
    const bloque = () =>
      (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
        'section[aria-labelledby="titulo-agentes"]',
      )!;
    return { fixture, bloque };
  }

  it('enseña el modo, la versión y los agentes de la API', () => {
    const fixture = pintar();
    http.expectOne('/api/sistema/info').flush(crearSistemaInfo({ version: '0.6.0' }));
    fixture.detectChanges();

    const elemento = fixture.nativeElement as HTMLElement;
    expect(texto(elemento)).toContain('Local');
    expect(texto(elemento)).toContain('0.6.0');
    expect(texto(elemento)).toContain('Apagados');
    expect(elemento.querySelector('button')).toBeNull();
  });

  it('si la API no responde, lo dice', () => {
    const fixture = pintar();
    http.expectOne('/api/sistema/info').error(new ProgressEvent('error'));
    fixture.detectChanges();

    expect(texto(fixture.nativeElement)).toContain('No se puede conectar con RadarEmpleo');
  });

  describe('agentes de Claude', () => {
    function bloqueSin(cambios: Parameters<typeof crearSistemaInfo>[0]) {
      const fixture = pintar();
      http.expectOne('/api/sistema/info').flush(crearSistemaInfo(cambios));
      fixture.detectChanges();
      return (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
        'section[aria-labelledby="titulo-agentes"]',
      )!;
    }

    it('apagados: solo el estado con su explicación, sin pedir /api/agentes ni enlazar', () => {
      const bloque = bloqueSin({});

      expect(texto(bloque)).toContain('Apagados');
      expect(texto(bloque)).toContain('Se encienden en la configuración de la app.');
      expect(bloque.querySelectorAll('dt')).toHaveLength(1);
      expect(bloque.querySelector('a')).toBeNull();
      http.expectNone(URL_ESTADO);
    });

    it('en la demo dice que no se usa Claude', () => {
      const bloque = bloqueSin({ modo: 'Demo' });

      expect(texto(bloque)).toContain('Apagados');
      expect(texto(bloque)).toContain('En la demo no se usa Claude.');
      http.expectNone(URL_ESTADO);
    });

    it('desde el móvil: «Solo desde el PC»', () => {
      const bloque = bloqueSin({ desdeElPc: false });

      expect(texto(bloque)).toContain('Solo desde el PC');
      expect(texto(bloque)).not.toContain('Apagados');
      http.expectNone(URL_ESTADO);
    });

    it('encendidos: pide el estado una vez y enseña el uso, el tope y la versión, sin dólares', () => {
      const { fixture, bloque } = pintarConAgentes();
      expect(bloque().querySelector('[role="status"]')?.textContent).toContain('Comprobando');

      http
        .expectOne(URL_ESTADO)
        .flush(crearEstadoAgentes({ gasto: crearGasto({ gastadoUsd: 0.45 }) }));
      fixture.detectChanges();

      const contenido = texto(bloque());
      expect(contenido).toContain('Encendidos');
      expect(contenido).toContain('Uso estimado de tu suscripción hoy: 3 % del tope');
      expect(contenido).toContain(
        'No se cobra aparte: sale del mismo cupo que usas con Claude Code.',
      );
      expect(contenido).toContain(
        'Fijo en la configuración de la app; se podrá cambiar más adelante.',
      );
      expect(contenido).toContain('2.1.185');
      expect(contenido).not.toContain('probado con');
      expect(contenido).not.toMatch(/USD|\$/);
      expect(bloque().querySelectorAll('dt')).toHaveLength(4);
      expect(bloque().querySelector('button')).toBeNull();
      const enlace = bloque().querySelector<HTMLAnchorElement>('a[href="/agentes"]')!;
      expect(enlace.textContent!.trim()).toBe('Ver las tareas de Claude');
      http.expectNone(URL_ESTADO);
    });

    it('con la cola en otra copia, la reserva y otra versión, lo dice', () => {
      const { fixture, bloque } = pintarConAgentes();
      http.expectOne(URL_ESTADO).flush(
        crearEstadoAgentes({
          llevaLaCola: false,
          motivoCola: 'La cola la lleva la otra copia de la API.',
          versionClaude: '2.1.190',
          versionDistinta: true,
          gasto: crearGasto({ reservadoUsd: 0.6 }),
        }),
      );
      fixture.detectChanges();

      const contenido = texto(bloque());
      expect(contenido).toContain('La cola la lleva la otra copia de la API.');
      expect(contenido).toContain('Hoy aún no has gastado nada; Claude tiene trabajo en marcha');
      expect(contenido).toContain('hasta el 4 % del tope de hoy');
      expect(contenido).toContain('2.1.190 · probado con 2.1.185');
    });

    it('sin versión conocida: «No se ha podido comprobar»', () => {
      const { fixture, bloque } = pintarConAgentes();
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes({ versionClaude: null }));
      fixture.detectChanges();

      expect(bloque().querySelector('dd:last-of-type')!.textContent!.trim()).toBe(
        'No se ha podido comprobar',
      );
    });

    it('si falla el estado, avisa y «Reintentar» lo vuelve a pedir', () => {
      const { fixture, bloque } = pintarConAgentes();
      http.expectOne(URL_ESTADO).flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      fixture.detectChanges();

      const aviso = bloque().querySelector<HTMLElement>('[role="alert"]')!;
      expect(aviso).not.toBeNull();
      aviso.querySelector<HTMLButtonElement>('button')!.click();
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
      fixture.detectChanges();

      expect(bloque().querySelector('[role="alert"]')).toBeNull();
      expect(texto(bloque())).toContain('Hoy aún no has usado Claude');
    });
  });
});
