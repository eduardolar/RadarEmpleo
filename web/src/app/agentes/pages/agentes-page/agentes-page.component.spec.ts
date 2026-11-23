import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SistemaInfo } from '../../../core/interfaces/sistema-info.interface';
import { crearSistemaInfo } from '../../../core/utils/sistema-de-prueba';
import { EjecucionAgente, EstadoAgentes } from '../../interfaces/ejecucion.interface';
import { INTERVALO_SEGUIMIENTO_MS } from '../../services/agentes.service';
import { crearEjecucion, crearEstadoAgentes, crearGasto } from '../../utils/ejecucion-de-prueba';
import AgentesPageComponent from './agentes-page.component';

const URL_ESTADO = '/api/agentes/estado';
const URL_EJECUCIONES = '/api/agentes/ejecuciones';

describe('AgentesPageComponent', () => {
  let fixture: ComponentFixture<AgentesPageComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-23T10:01:20Z'));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    fixture?.destroy();
    http.verify();
    vi.useRealTimers();
  });

  function abrir(info: Partial<SistemaInfo> = { agentesDisponibles: true }): void {
    fixture = TestBed.createComponent(AgentesPageComponent);
    fixture.detectChanges();
    http.expectOne('/api/sistema/info').flush(crearSistemaInfo(info));
    fixture.detectChanges();
  }

  /** Con agentes: responde al uso de hoy y a la lista */
  function responder(lista: EjecucionAgente[], estado: EstadoAgentes = crearEstadoAgentes()): void {
    http.expectOne(URL_ESTADO).flush(estado);
    http
      .expectOne((r) => r.url === URL_EJECUCIONES && r.params.get('limite') === '50')
      .flush(lista);
    fixture.detectChanges();
  }

  const elemento = () => fixture.nativeElement as HTMLElement;
  const texto = () => elemento().textContent!.replace(/\s+/g, ' ');
  const filas = () => [...elemento().querySelectorAll<HTMLElement>('ul > li')];
  const botones = (fila: HTMLElement, nombre: string) =>
    [...fila.querySelectorAll('button')].filter((b) => b.textContent!.includes(nombre));

  it('un solo h1, «Volver a Más» y ningún relleno lima', () => {
    abrir();
    responder([crearEjecucion({ estado: 'Error' })]);

    expect(elemento().querySelectorAll('h1')).toHaveLength(1);
    expect(elemento().querySelector('h1')!.textContent).toContain('Agentes de Claude');
    expect(elemento().querySelector('a[href="/mas"]')!.textContent).toContain('Volver a Más');
    expect(
      elemento().querySelector('[mat-flat-button], [mat-raised-button], .boton-acento'),
    ).toBeNull();
  });

  describe('sin agentes disponibles', () => {
    it('desde el móvil, solo la explicación y sin pedir nada a /api/agentes', () => {
      abrir({ modo: 'Local', agentesHabilitados: true, agentesDisponibles: false });

      expect(elemento().querySelector('.agentes__motivo')!.textContent).toContain(
        'solo se pueden usar desde el PC',
      );
      expect(elemento().querySelector('h2, ul, button')).toBeNull();
      expect(http.match((r) => r.url.startsWith('/api/agentes'))).toEqual([]);
    });

    it('en la demo lo dice así', () => {
      abrir({ modo: 'Demo', agentesDisponibles: false });

      expect(texto()).toContain('En la demo no se usa Claude');
    });

    it('si la API rechaza la lista (403), pasa a la explicación', () => {
      abrir();
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
      http
        .expectOne((r) => r.url === URL_EJECUCIONES)
        .flush(null, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();

      expect(elemento().querySelector('.agentes__motivo')).not.toBeNull();
      expect(elemento().querySelector('ul')).toBeNull();
    });
  });

  it('sin conexión con la API lo dice y deja reintentar', () => {
    fixture = TestBed.createComponent(AgentesPageComponent);
    fixture.detectChanges();
    http.expectOne('/api/sistema/info').flush(null, { status: 0, statusText: 'Unknown Error' });
    fixture.detectChanges();

    const aviso = elemento().querySelector('[role="alert"]')!;
    expect(aviso.textContent).toContain('No se ha podido hablar con la API');
    aviso.querySelector('button')!.click();
    http.expectOne('/api/sistema/info').flush(crearSistemaInfo());
  });

  describe('uso de hoy', () => {
    it('el porcentaje del tope y lo reservado, nunca en USD', () => {
      abrir();
      responder(
        [],
        crearEstadoAgentes({ gasto: crearGasto({ gastadoUsd: 0.42, reservadoUsd: 0.5 }) }),
      );

      const uso = elemento().querySelector('.agentes__uso')!.textContent!;
      expect(uso).toContain('Uso estimado de tu suscripción hoy: 3 % del tope');
      expect(uso).toContain('Reservado para lo que está en marcha: hasta el 4 % del tope de hoy');
      expect(texto()).not.toMatch(/USD|\$/);
    });

    it('sin nada usado, «Hoy aún no has usado Claude» y sin línea de reserva', () => {
      abrir();
      responder([]);

      const uso = elemento().querySelector('.agentes__uso')!.textContent!;
      expect(uso).toContain('Hoy aún no has usado Claude');
      expect(uso).not.toContain('Reservado');
    });

    it('sin gastar pero con algo en marcha no dice que no has usado Claude', () => {
      abrir();
      responder([], crearEstadoAgentes({ gasto: crearGasto({ reservadoUsd: 0.5 }) }));

      const uso = elemento().querySelector('.agentes__uso')!.textContent!;
      expect(uso).toContain('Hoy aún no has gastado nada; Claude tiene trabajo en marcha');
      expect(uso).not.toContain('aún no has usado');
    });
  });

  it('sin ninguna tarea, el estado vacío', () => {
    abrir();
    responder([]);

    expect(elemento().querySelector('ul')).toBeNull();
    expect(elemento().querySelector('.agentes__vacio')!.textContent).toContain(
      'Todavía no has lanzado ninguna tarea de Claude.',
    );
    expect(texto()).toContain('Se lanzan desde la ficha de una candidatura y desde Importación.');
  });

  describe('lista', () => {
    it('cada tarea en un li con su nombre, el enlace a la ficha, la fecha y su estado compacto', () => {
      abrir();
      responder([
        crearEjecucion({ id: 2, estado: 'Ok', costeUsd: 0.15 }),
        crearEjecucion({
          id: 1,
          tarea: 'ExtraerRegistro',
          estado: 'Ok',
          candidaturaId: null,
          candidatura: null,
        }),
      ]);

      const [primera, segunda] = filas();
      expect(filas()).toHaveLength(2);
      expect(primera.querySelector('h3')!.textContent).toContain('Análisis del encaje');
      const enlace = primera.querySelector<HTMLAnchorElement>('a[href="/candidaturas/12"]')!;
      expect(enlace.textContent!.trim()).toBe('Desarrollador Angular en Acme');
      expect(primera.querySelector('time')!.getAttribute('datetime')).toBe('2026-11-23T10:00:00Z');
      expect(primera.querySelector('time')!.textContent).toContain('23 nov 2026, 11:00');
      // Compacta: sin role="status" (no se anuncian todas a la vez) y con la empresa en su nombre
      const estado = primera.querySelector('app-estado-ejecucion [role="group"]')!;
      expect(estado.classList).toContain('estado-ejecucion--compacta');
      expect(estado.getAttribute('aria-label')).toBe('Tarea de Claude: Análisis del encaje · Acme');
      expect(primera.querySelector('[role="status"]')).toBeNull();

      expect(segunda.querySelector('h3')!.textContent).toContain('Lectura del registro');
      expect(segunda.querySelector('a')).toBeNull();
    });

    it('«Reintentar» solo en la última de cada tarea y candidatura', () => {
      abrir();
      responder([
        crearEjecucion({ id: 3, estado: 'Error' }),
        crearEjecucion({ id: 2, estado: 'Cancelada', candidaturaId: 20 }),
        crearEjecucion({ id: 1, estado: 'Error' }),
      ]);

      expect(filas().map((f) => botones(f, 'Reintentar').length)).toEqual([1, 1, 0]);
    });

    it('cuando una en marcha termina, vuelve a pedir la lista y el uso sin vaciarla', () => {
      abrir();
      responder([crearEjecucion({ id: 5, estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' })]);

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      http
        .expectOne(`${URL_EJECUCIONES}/5`)
        .flush(crearEjecucion({ id: 5, estado: 'Ok', inicio: '2026-11-23T10:00:00Z' }));
      fixture.detectChanges();

      // La lista anterior sigue a la vista mientras llega la nueva
      expect(filas()).toHaveLength(1);
      responder([crearEjecucion({ id: 5, estado: 'Ok', inicio: '2026-11-23T10:00:00Z' })]);
      expect(filas()[0].querySelector('[data-estado]')!.getAttribute('data-estado')).toBe('Ok');
    });

    it('al reintentar, vuelve a pedir la lista: la nueva arriba y la vieja ya sin botón', () => {
      abrir();
      responder([crearEjecucion({ id: 1, estado: 'Error' })]);

      botones(filas()[0], 'Reintentar')[0].click();
      http
        .expectOne(`${URL_EJECUCIONES}/1/reintentar`)
        .flush(crearEjecucion({ id: 2 }), { status: 202, statusText: 'Accepted' });
      responder([crearEjecucion({ id: 2 }), crearEjecucion({ id: 1, estado: 'Error' })]);

      expect(filas().map((f) => botones(f, 'Reintentar').length)).toEqual([0, 0]);
      expect(botones(filas()[0], 'Cancelar')).toHaveLength(1);
      fixture.destroy();
      // La nueva quedó en seguimiento: el servicio sigue preguntando hasta que termine
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      http.expectOne(`${URL_EJECUCIONES}/2`).flush(crearEjecucion({ id: 2, estado: 'Ok' }));
    });

    it('si falla la lista, lo dice y deja reintentar', () => {
      abrir();
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
      http
        .expectOne((r) => r.url === URL_EJECUCIONES)
        .flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      fixture.detectChanges();

      const aviso = elemento().querySelector<HTMLElement>('.agentes__lista [role="alert"]')!;
      aviso.querySelector('button')!.click();
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush([]);
    });
  });
});
