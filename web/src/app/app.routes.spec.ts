import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Title } from '@angular/platform-browser';
import { provideRouter, Router, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { routes } from './app.routes';
import { crearDetalle } from './candidaturas/utils/candidatura-de-prueba';

describe('Navegación', () => {
  let http: HttpTestingController;
  let abrirDialogo: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    abrirDialogo = vi.fn(() => ({ afterClosed: () => of(false) }));
    TestBed.configureTestingModule({
      providers: [
        // Igual que en app.config.ts: sin esto, la ficha no recibe el id de la ruta
        provideRouter(routes, withComponentInputBinding()),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialog, useValue: { open: abrirDialogo } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // Hoy lleva el indicador de la API, que pregunta al entrar
  function responderInfo(): void {
    http
      .expectOne('/api/sistema/info')
      .flush({ modo: 'Local', agentesHabilitados: false, version: '0.1.0' });
  }

  it('«/» redirige a «/hoy» y su enlace lleva aria-current="page"', async () => {
    const harness = await RouterTestingHarness.create('/');
    responderInfo();

    expect(TestBed.inject(Router).url).toBe('/hoy');

    // Uno en el menú lateral y otro en la barra inferior: cada uno se oculta por CSS según el ancho
    const activos = harness.routeNativeElement!.querySelectorAll('a[aria-current="page"]');
    expect(activos).toHaveLength(2);
    activos.forEach((enlace) => expect(enlace.getAttribute('href')).toBe('/hoy'));
  });

  it('/candidaturas/1 abre esa ficha y resalta Tablero en el menú y en la barra', async () => {
    const harness = await RouterTestingHarness.create('/candidaturas/1');

    http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1, puesto: 'Dev de la ficha' }));
    await harness.fixture.whenStable();

    const pagina = harness.routeNativeElement!;
    expect(pagina.querySelector('h1')?.textContent).toContain('Dev de la ficha');
    const activos = pagina.querySelectorAll('a[aria-current="page"]');
    expect(activos).toHaveLength(2);
    activos.forEach((enlace) => expect(enlace.getAttribute('href')).toBe('/tablero'));
    expect(activos[0].classList.contains('bg-blue-500/10')).toBe(true);
    expect(activos[1].classList.contains('text-white')).toBe(true);
  });

  it('al salir de la ficha, Tablero deja de estar resaltado', async () => {
    const harness = await RouterTestingHarness.create('/candidaturas/1');
    http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));

    await harness.navigateByUrl('/rastreos');

    const activos = harness.routeNativeElement!.querySelectorAll('a[aria-current="page"]');
    expect(activos).toHaveLength(2);
    activos.forEach((enlace) => expect(enlace.getAttribute('href')).toBe('/rastreos'));
  });

  it('desde Más se llega a la importación, que resalta Más y pide la vista previa', async () => {
    const harness = await RouterTestingHarness.create('/mas');
    const pagina = harness.routeNativeElement!;

    pagina.querySelector<HTMLAnchorElement>('main a[href="/importacion"]')!.click();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/importacion');
    expect(TestBed.inject(Title).getTitle()).toBe('Traer del registro · RadarEmpleo');
    http.expectOne('/api/importacion/tabla/previsualizar').flush({
      lote: { version: 1, origen: 'Tabla', filas: [] },
      resumen: {
        nuevas: 0,
        avanzan: 0,
        yaExisten: 0,
        paraRevisar: 0,
        sinLeer: 0,
        enviosEnTexto: 0,
      },
      filas: [],
      sinLeer: [],
    });
    await harness.fixture.whenStable();

    expect(pagina.querySelector('h1')?.textContent).toContain('Traer del registro');
    const activos = pagina.querySelectorAll('a[aria-current="page"]');
    expect(activos).toHaveLength(2);
    activos.forEach((enlace) => expect(enlace.getAttribute('href')).toBe('/mas'));
  });

  describe('salir de Apuntar', () => {
    async function apuntarConAlgoEscrito(): Promise<void> {
      const harness = await RouterTestingHarness.create('/apuntar');
      const empresa = harness.routeNativeElement!.querySelector<HTMLInputElement>(
        '[formcontrolname="empresa"]',
      )!;
      empresa.value = 'Acme';
      empresa.dispatchEvent(new Event('input'));
    }

    it('con algo escrito pregunta y se queda si no se confirma', async () => {
      await apuntarConAlgoEscrito();

      const navegado = await TestBed.inject(Router).navigateByUrl('/rastreos');

      expect(abrirDialogo).toHaveBeenCalledTimes(1);
      expect(navegado).toBe(false);
      expect(TestBed.inject(Router).url).toBe('/apuntar');
    });

    it('sale si se confirma', async () => {
      await apuntarConAlgoEscrito();
      abrirDialogo.mockReturnValue({ afterClosed: () => of(true) });

      await TestBed.inject(Router).navigateByUrl('/rastreos');

      expect(TestBed.inject(Router).url).toBe('/rastreos');
    });

    it('sin nada escrito sale sin preguntar', async () => {
      await RouterTestingHarness.create('/apuntar');

      await TestBed.inject(Router).navigateByUrl('/rastreos');

      expect(abrirDialogo).not.toHaveBeenCalled();
      expect(TestBed.inject(Router).url).toBe('/rastreos');
    });
  });

  it('lleva el foco a <main> al cambiar de página, pero no en la primera carga', async () => {
    const harness = await RouterTestingHarness.create('/hoy');
    responderInfo();

    expect(document.activeElement?.id).not.toBe('principal');

    await harness.navigateByUrl('/tablero');
    // El tablero pide la lista al entrar
    http.expectOne('/api/candidaturas').flush([]);

    expect(document.activeElement?.id).toBe('principal');
    expect(
      harness.routeNativeElement!.querySelector('a[aria-current="page"]')?.getAttribute('href'),
    ).toBe('/tablero');
  });
});
