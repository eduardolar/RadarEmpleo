import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SistemaInfo } from '../../../core/interfaces/sistema-info.interface';
import { crearSistemaInfo } from '../../../core/utils/sistema-de-prueba';
import { EjecucionAgente, EstadoAgentes } from '../../interfaces/ejecucion.interface';
import { INTERVALO_SEGUIMIENTO_MS } from '../../services/agentes.service';
import { crearEjecucion, crearEstadoAgentes, crearGasto } from '../../utils/ejecucion-de-prueba';
import { NBSP, TEXTO_RECHAZADOS, TEXTO_SIGUE_RECHAZADO } from '../../utils/textos-ejecucion';
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

  // Como la API: disponibles implica encendidos
  function abrir(
    info: Partial<SistemaInfo> = { agentesHabilitados: true, agentesDisponibles: true },
  ): void {
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
      // Lo que da el remoto aunque estén encendidos en el PC: agentesHabilitados siempre a false
      abrir({
        modo: 'Local',
        agentesHabilitados: false,
        agentesDisponibles: false,
        desdeElPc: false,
      });

      expect(elemento().querySelector('.agentes__motivo')!.textContent).toContain(
        'solo se pueden usar desde el PC',
      );
      expect(elemento().querySelector('h2, ul, button')).toBeNull();
      expect(http.match((r) => r.url.startsWith('/api/agentes'))).toEqual([]);
    });

    it('en el PC con los agentes apagados, dice que se encienden en la configuración', () => {
      abrir({ agentesHabilitados: false, agentesDisponibles: false, desdeElPc: true });

      expect(texto()).toContain('están apagados en la configuración de la app. Se encienden allí');
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

    it('un estado bueno que llega después del 403 de la lista no vuelve a dar los agentes por buenos', () => {
      abrir();
      const estado = http.expectOne(URL_ESTADO);
      http
        .expectOne((r) => r.url === URL_EJECUCIONES)
        .flush(null, { status: 403, statusText: 'Forbidden' });
      estado.flush(crearEstadoAgentes());
      fixture.detectChanges();

      // Sin bucle: no se vuelve a pedir nada (lo comprueba http.verify)
      expect(elemento().querySelector('.agentes__motivo')).not.toBeNull();
    });

    describe('tras un 403', () => {
      function rechazar(): void {
        abrir();
        http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
        http
          .expectOne((r) => r.url === URL_EJECUCIONES)
          .flush(null, { status: 403, statusText: 'Forbidden' });
        fixture.detectChanges();
      }

      function reabrir(): void {
        fixture.destroy();
        // /api/sistema/info ya contestó: no se vuelve a pedir
        fixture = TestBed.createComponent(AgentesPageComponent);
        fixture.detectChanges();
      }

      const volver = () =>
        [...elemento().querySelectorAll('button')].find((b) =>
          b.textContent!.includes('Volver a comprobar'),
        );

      it('en el PC dice que se vuelva a comprobar y «Volver a comprobar» pide el estado y luego la lista', () => {
        rechazar();
        expect(elemento().querySelector('.agentes__motivo')!.textContent).toContain(
          TEXTO_RECHAZADOS,
        );

        volver()!.click();
        // Una sola vez el estado; con el 200, solo falta la lista
        http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
        fixture.detectChanges();
        http.expectOne((r) => r.url === URL_EJECUCIONES).flush([]);
        fixture.detectChanges();

        expect(elemento().querySelector('.agentes__motivo')).toBeNull();
        expect(elemento().querySelector('.agentes__vacio')).not.toBeNull();
      });

      it('al volver a entrar pregunta una vez: con 200, pide la lista', () => {
        rechazar();
        reabrir();

        http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
        fixture.detectChanges();
        http.expectOne((r) => r.url === URL_EJECUCIONES).flush([]);
        fixture.detectChanges();

        expect(elemento().querySelector('.agentes__vacio')).not.toBeNull();
      });

      it('al volver a entrar, otro 403 deja la explicación sin volver a preguntar', () => {
        rechazar();
        reabrir();

        http.expectOne(URL_ESTADO).flush(null, { status: 403, statusText: 'Forbidden' });
        fixture.detectChanges();

        expect(elemento().querySelector('.agentes__motivo')!.textContent).toContain(
          TEXTO_RECHAZADOS,
        );
        expect(volver()).toBeDefined();
      });
    });

    it('desde el móvil no hay «Volver a comprobar»', () => {
      abrir({ agentesHabilitados: false, agentesDisponibles: false, desdeElPc: false });

      expect(elemento().querySelector('button')).toBeNull();
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
      expect(uso).toContain(`Uso estimado de tu suscripción hoy: 3${NBSP}% del tope`);
      expect(uso).toContain(
        `Reservado para lo que está en marcha: hasta el 4${NBSP}% del tope de hoy`,
      );
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

    it('si falla, lo dice y «Reintentar» vuelve a pedir el uso y la lista', () => {
      abrir();
      http.expectOne(URL_ESTADO).flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush([]);
      fixture.detectChanges();

      const boton = elemento().querySelector<HTMLButtonElement>(
        '.agentes__uso [role="alert"] button',
      )!;
      boton.focus();
      boton.click();
      fixture.detectChanges();

      // Mientras espera, el aviso y su botón (con el foco) siguen ahí
      expect(elemento().querySelector('.agentes__uso [role="alert"]')).not.toBeNull();
      expect(boton.isConnected).toBe(true);
      expect(boton.textContent!.trim()).toBe('Reintentando…');
      expect(boton.getAttribute('aria-disabled')).toBe('true');
      boton.click();

      responder([]);
      expect(elemento().querySelector('.agentes__uso')!.textContent).toContain(
        'Hoy aún no has usado Claude',
      );
      expect(elemento().querySelector('.agentes__uso [role="alert"]')).toBeNull();
      expect(document.activeElement).toBe(elemento().querySelector('#titulo-uso'));
    });

    it('si el reintento vuelve a fallar, el aviso no desaparece y el foco se queda en el botón', () => {
      abrir();
      http.expectOne(URL_ESTADO).flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush([]);
      fixture.detectChanges();

      const boton = elemento().querySelector<HTMLButtonElement>(
        '.agentes__uso [role="alert"] button',
      )!;
      boton.focus();
      boton.click();
      fixture.detectChanges();
      http.expectOne(URL_ESTADO).flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush([]);
      fixture.detectChanges();

      expect(boton.isConnected).toBe(true);
      expect(boton.textContent!.trim()).toBe('Reintentar');
      expect(document.activeElement).toBe(boton);
    });
  });

  it('sin ninguna tarea, el estado vacío', () => {
    abrir();
    responder([]);

    expect(elemento().querySelector('ul')).toBeNull();
    expect(elemento().querySelector('.agentes__vacio')!.textContent).toContain(
      'Todavía no has lanzado ninguna tarea de Claude.',
    );
    expect(texto()).toContain('Se lanzan desde la ficha de una candidatura o desde Importación.');
    const vacio = elemento().querySelector('.agentes__vacio')!;
    expect(vacio.querySelector('a[href="/tablero"]')!.textContent).toContain('Ir al Tablero');
    expect(vacio.querySelector('a[href="/importacion"]')!.textContent).toContain(
      'Ir a Importación',
    );
    expect(vacio.querySelector('[mat-flat-button]')).toBeNull();
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
      expect(primera.querySelector('h4')!.textContent).toContain('Análisis del encaje');
      const enlace = primera.querySelector<HTMLAnchorElement>('a[href="/candidaturas/12"]')!;
      expect(enlace.textContent!.trim()).toBe('Desarrollador Angular en Acme');
      expect(primera.querySelector('time')!.getAttribute('datetime')).toBe('2026-11-23T10:00:00Z');
      expect(primera.querySelector('time')!.textContent).toBe('11:00');
      // Compacta: sin role="status" (no se anuncian todas a la vez) y con la empresa en su nombre
      const estado = primera.querySelector('app-estado-ejecucion [role="group"]')!;
      expect(estado.classList).toContain('estado-ejecucion--compacta');
      expect(estado.getAttribute('aria-label')).toBe('Tarea de Claude: Análisis del encaje · Acme');
      expect(primera.querySelector('[role="status"]')).toBeNull();

      expect(segunda.querySelector('h4')!.textContent).toContain('Lectura del texto libre');
      expect(segunda.querySelector('a[href^="/candidaturas"]')).toBeNull();
    });

    it('sin los datos de la candidatura, «Ver la candidatura» lleva la tarea en su nombre', () => {
      abrir();
      responder([crearEjecucion({ estado: 'Ok', candidatura: null })]);

      const enlace = filas()[0].querySelector('a[href="/candidaturas/12"]')!;
      expect(enlace.textContent!.trim()).toBe('Ver la candidatura');
      expect(enlace.getAttribute('aria-label')).toBe('Ver la candidatura: Análisis del encaje');
    });

    it('el uso de otro día es «del tope diario» y un error solo da su motivo', () => {
      abrir();
      responder([
        crearEjecucion({ id: 3, estado: 'Ok', inicio: '2026-11-23T10:00:00Z', costeUsd: 0.15 }),
        crearEjecucion({ id: 2, estado: 'Ok', inicio: '2026-11-20T10:00:00Z', costeUsd: 0.15 }),
        crearEjecucion({ id: 1, estado: 'Error', candidaturaId: 20 }),
      ]);

      const [hoy, otroDia, error] = filas().map((f) =>
        f.querySelector('.estado-ejecucion__texto')!.textContent!.trim(),
      );
      expect(hoy).toContain('del tope de hoy');
      expect(otroDia).toContain('del tope diario');
      expect(otroDia).not.toContain('de hoy');
      expect(error).toBe('Sin más detalle.');
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

    it('cuando una en marcha termina, vuelve a pedir la lista y el uso sin vaciarla', async () => {
      abrir();
      responder([crearEjecucion({ id: 5, estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' })]);

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      http
        .expectOne(`${URL_EJECUCIONES}/5`)
        .flush(crearEjecucion({ id: 5, estado: 'Ok', inicio: '2026-11-23T10:00:00Z' }));
      fixture.detectChanges();
      await Promise.resolve();

      // La lista anterior sigue a la vista mientras llega la nueva
      expect(filas()).toHaveLength(1);
      responder([crearEjecucion({ id: 5, estado: 'Ok', inicio: '2026-11-23T10:00:00Z' })]);
      expect(filas()[0].querySelector('[data-estado]')!.getAttribute('data-estado')).toBe('Ok');
    });

    it('si terminan dos en la misma vuelta, pide la lista y el uso una sola vez', async () => {
      const enCurso = { estado: 'EnCurso' as const, inicio: '2026-11-23T10:00:00Z' };
      abrir();
      responder([
        crearEjecucion({ id: 6, ...enCurso }),
        crearEjecucion({ id: 5, candidaturaId: 20, ...enCurso }),
      ]);

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      // Cada respuesta llega en su propio evento: entre una y otra, la página no refresca
      http.expectOne(`${URL_EJECUCIONES}/6`).flush(crearEjecucion({ id: 6, estado: 'Ok' }));
      fixture.detectChanges();
      await Promise.resolve();
      expect(http.match(URL_ESTADO)).toEqual([]);
      http
        .expectOne(`${URL_EJECUCIONES}/5`)
        .flush(crearEjecucion({ id: 5, candidaturaId: 20, estado: 'Ok' }));
      fixture.detectChanges();
      await Promise.resolve();

      // expectOne falla si llegan dos
      responder([]);
    });

    it('al reintentar, vuelve a pedir la lista: la nueva arriba y la vieja ya sin botón', async () => {
      abrir();
      responder([crearEjecucion({ id: 1, estado: 'Error' })]);

      botones(filas()[0], 'Reintentar')[0].click();
      http
        .expectOne(`${URL_EJECUCIONES}/1/reintentar`)
        .flush(crearEjecucion({ id: 2 }), { status: 202, statusText: 'Accepted' });
      await Promise.resolve();
      responder([crearEjecucion({ id: 2 }), crearEjecucion({ id: 1, estado: 'Error' })]);

      expect(filas().map((f) => botones(f, 'Reintentar').length)).toEqual([0, 0]);
      expect(botones(filas()[0], 'Cancelar')).toHaveLength(1);
      fixture.destroy();
      // La nueva quedó en seguimiento: el servicio sigue preguntando hasta que termine
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      http.expectOne(`${URL_EJECUCIONES}/2`).flush(crearEjecucion({ id: 2, estado: 'Ok' }));
    });

    it('si falla la lista, lo dice y «Reintentar» vuelve a pedir la lista y el uso', () => {
      abrir();
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
      http
        .expectOne((r) => r.url === URL_EJECUCIONES)
        .flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      fixture.detectChanges();

      const aviso = elemento().querySelector<HTMLElement>('.agentes__lista [role="alert"]')!;
      aviso.querySelector('button')!.click();
      responder([]);
    });

    it('agrupada por día («Hoy», «Ayer» y la fecha), con solo la hora en cada fila', () => {
      abrir();
      responder([
        crearEjecucion({ id: 4, estado: 'Ok', fechaAlta: '2026-11-23T10:00:00Z' }),
        crearEjecucion({ id: 3, estado: 'Ok', fechaAlta: '2026-11-23T08:05:00Z' }),
        crearEjecucion({ id: 2, estado: 'Ok', fechaAlta: '2026-11-22T17:30:00Z' }),
        crearEjecucion({ id: 1, estado: 'Ok', fechaAlta: '2026-11-20T10:00:00Z' }),
      ]);

      const dias = [...elemento().querySelectorAll<HTMLElement>('.agentes__dia')];
      expect(dias.map((d) => d.querySelector('h3')!.textContent!.trim())).toEqual([
        'Hoy',
        'Ayer',
        '20 nov 2026',
      ]);
      expect(dias.map((d) => d.querySelectorAll('ul > li').length)).toEqual([2, 1, 1]);
      // Cada grupo se llama como su encabezado
      expect(dias[0].getAttribute('aria-labelledby')).toBe(dias[0].querySelector('h3')!.id);
      expect(filas().map((f) => f.querySelector('time')!.textContent)).toEqual([
        '11:00',
        '09:05',
        '18:30',
        '11:00',
      ]);
    });

    it('un error que salió bien después va atenuado y con «Resuelto: salió bien después.»', () => {
      abrir();
      responder([
        crearEjecucion({ id: 4, estado: 'Error', candidaturaId: 20 }),
        crearEjecucion({ id: 3, estado: 'Ok' }),
        crearEjecucion({ id: 2, estado: 'Error' }),
      ]);

      const [sinResolver, , resuelto] = filas();
      expect(resuelto.classList).toContain('agentes__ejecucion--resuelta');
      expect(resuelto.querySelector('.opacity-60')).not.toBeNull();
      const aviso = resuelto.querySelector('.agentes__resuelto')!;
      expect(aviso.textContent!.trim()).toBe('Resuelto: salió bien después.');
      // El aviso no va atenuado
      expect(aviso.closest('.opacity-60')).toBeNull();
      expect(sinResolver.querySelector('.agentes__resuelto')).toBeNull();
      expect(sinResolver.querySelector('.opacity-60')).toBeNull();
    });

    it('«Ver en Importación» solo en la última lectura del registro, si terminó bien', () => {
      const lectura = { tarea: 'ExtraerRegistro' as const, candidaturaId: null, candidatura: null };
      abrir();
      responder([
        crearEjecucion({ id: 3, estado: 'Ok', ...lectura }),
        crearEjecucion({ id: 2, estado: 'Ok', ...lectura }),
        crearEjecucion({ id: 1, estado: 'Ok' }),
      ]);

      const enlaces = filas().map((f) => f.querySelector('a[href="/importacion"]'));
      expect(enlaces[0]!.textContent!.trim()).toBe('Ver en Importación');
      expect(enlaces.slice(1)).toEqual([null, null]);
    });

    it('si la última lectura del registro no ha salido, no lleva «Ver en Importación»', () => {
      abrir();
      responder([
        crearEjecucion({
          estado: 'Error',
          tarea: 'ExtraerRegistro',
          candidaturaId: null,
          candidatura: null,
        }),
      ]);

      expect(filas()[0].querySelector('a')).toBeNull();
    });
  });

  describe('recuento y filtro', () => {
    const chips = () => [
      ...elemento().querySelectorAll<HTMLButtonElement>('[aria-label="Filtrar las tareas"] button'),
    ];
    const chip = (nombre: string) => chips().find((c) => c.textContent!.trim() === nombre)!;
    const recuento = () => elemento().querySelector('.agentes__recuento')!.textContent!.trim();
    const sinFiltro = () => elemento().querySelector('.agentes__sin-filtro')!.textContent!.trim();

    function conTareas(): void {
      abrir();
      responder([
        crearEjecucion({ id: 5, estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' }),
        crearEjecucion({ id: 4, estado: 'Error', candidaturaId: 20 }),
        crearEjecucion({ id: 3, estado: 'Ok', candidaturaId: 21 }),
        crearEjecucion({
          id: 2,
          estado: 'Error',
          candidaturaId: 22,
          fechaAlta: '2026-11-22T10:00:00Z',
        }),
      ]);
    }

    it('arriba, las tareas de hoy y cuántas no han salido; los errores de otro día no cuentan', () => {
      conTareas();

      expect(recuento()).toBe('Hoy: 3 tareas · 1 no ha salido');
    });

    it('los chips son botones con aria-pressed y empieza en «Todas»', () => {
      conTareas();

      expect(chips().map((c) => [c.textContent!.trim(), c.getAttribute('aria-pressed')])).toEqual([
        ['Todas', 'true'],
        ['No han salido', 'false'],
        ['En marcha', 'false'],
      ]);
      expect(filas()).toHaveLength(4);
      expect(sinFiltro()).toBe('');
    });

    it('«No han salido» deja los errores de cualquier día y «En marcha», lo que está en cola o en curso', () => {
      conTareas();

      chip('No han salido').click();
      fixture.detectChanges();
      expect(chip('No han salido').getAttribute('aria-pressed')).toBe('true');
      expect(
        filas().map((f) => f.querySelector('[data-estado]')!.getAttribute('data-estado')),
      ).toEqual(['Error', 'Error']);
      expect(elemento().querySelectorAll('.agentes__dia')).toHaveLength(2);

      chip('En marcha').click();
      fixture.detectChanges();
      expect(
        filas().map((f) => f.querySelector('[data-estado]')!.getAttribute('data-estado')),
      ).toEqual(['EnCurso']);
      // El recuento es del día, no de lo filtrado
      expect(recuento()).toBe('Hoy: 3 tareas · 1 no ha salido');

      chip('Todas').click();
      fixture.detectChanges();
      expect(filas()).toHaveLength(4);
    });

    it('si el filtro no deja nada, lo dice en un role="status"', () => {
      abrir();
      responder([crearEjecucion({ estado: 'Ok' })]);

      chip('No han salido').click();
      fixture.detectChanges();
      expect(filas()).toHaveLength(0);
      expect(sinFiltro()).toBe('Ninguna tarea ha fallado.');
      expect(elemento().querySelector('.agentes__sin-filtro')!.getAttribute('role')).toBe('status');

      chip('En marcha').click();
      fixture.detectChanges();
      expect(sinFiltro()).toBe('Ahora mismo no hay ninguna tarea en marcha.');
    });
  });

  describe('«Volver a comprobar» tras un 403', () => {
    const PROHIBIDO = { status: 403, statusText: 'Forbidden' };
    const boton = () => elemento().querySelector<HTMLButtonElement>('.agentes__motivo ~ button')!;
    const resultado = () => elemento().querySelector('.agentes__comprobacion')!.textContent!.trim();

    function rechazarYPulsar(): HTMLButtonElement {
      abrir();
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush(null, PROHIBIDO);
      fixture.detectChanges();
      expect(resultado()).toBe('');
      const pulsado = boton();
      pulsado.focus();
      pulsado.click();
      fixture.detectChanges();
      return pulsado;
    }

    it('mientras espera dice «Comprobando…» y un segundo clic no pide nada más', () => {
      const pulsado = rechazarYPulsar();

      expect(pulsado.isConnected).toBe(true);
      expect(pulsado.textContent!.trim()).toBe('Comprobando…');
      expect(pulsado.getAttribute('aria-disabled')).toBe('true');
      pulsado.click();

      // expectOne falla si el segundo clic hubiera pedido otra vez
      http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
    });

    it('si vuelve a dar 403, lo dice debajo y el foco se queda en el botón', () => {
      const pulsado = rechazarYPulsar();
      http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
      fixture.detectChanges();

      expect(pulsado.isConnected).toBe(true);
      expect(pulsado.textContent!.trim()).toBe('Volver a comprobar');
      expect(resultado()).toBe(TEXTO_SIGUE_RECHAZADO);
      expect(elemento().querySelector('.agentes__comprobacion')!.getAttribute('role')).toBe(
        'status',
      );
      expect(document.activeElement).toBe(pulsado);
    });

    it('si falla por otra cosa, enseña el mensaje del fallo y al volver a pulsar lo borra', () => {
      rechazarYPulsar();
      http.expectOne(URL_ESTADO).flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      fixture.detectChanges();

      expect(resultado()).not.toBe('');
      expect(resultado()).not.toBe(TEXTO_SIGUE_RECHAZADO);

      boton().click();
      fixture.detectChanges();
      expect(resultado()).toBe('');
      http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
    });

    it('si contesta bien, el panel desaparece y el foco va al título de «Uso de hoy», no a <body>', () => {
      rechazarYPulsar();
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
      fixture.detectChanges();
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush([]);
      fixture.detectChanges();

      expect(elemento().querySelector('.agentes__motivo')).toBeNull();
      expect(document.activeElement).toBe(elemento().querySelector('#titulo-uso'));
    });

    it('si un reintento del uso da 403, el foco va a la explicación, no a <body>', () => {
      abrir();
      http.expectOne(URL_ESTADO).flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush([]);
      fixture.detectChanges();

      const reintentar = elemento().querySelector<HTMLButtonElement>(
        '.agentes__uso [role="alert"] button',
      )!;
      reintentar.focus();
      reintentar.click();
      fixture.detectChanges();
      http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush([]);
      fixture.detectChanges();

      expect(reintentar.isConnected).toBe(false);
      const motivo = elemento().querySelector('.agentes__motivo')!;
      expect(motivo.textContent).toContain(TEXTO_RECHAZADOS);
      expect(document.activeElement).toBe(motivo);
    });
  });

  describe('una vuelta del seguimiento con un 404 y un 403', () => {
    const PROHIBIDO = { status: 403, statusText: 'Forbidden' };
    const NO_ESTA = { status: 404, statusText: 'Not Found' };

    function seguirDos(): void {
      const enCurso = { estado: 'EnCurso' as const, inicio: '2026-11-23T10:00:00Z' };
      abrir();
      responder([
        crearEjecucion({ id: 6, ...enCurso }),
        crearEjecucion({ id: 5, candidaturaId: 20, ...enCurso }),
      ]);
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
    }

    /** Tras la vuelta: la explicación en vez de la lista, y el seguimiento parado */
    async function compruebaQueQuedaRechazada(): Promise<void> {
      fixture.detectChanges();
      await Promise.resolve();
      // La tarea perdida pide refrescar: como mucho sale una petición del uso, y la lista no se pide
      const usos = http.match(URL_ESTADO);
      expect(usos.length).toBeLessThanOrEqual(1);
      usos.forEach((uso) => uso.flush(null, PROHIBIDO));
      fixture.detectChanges();

      expect(elemento().querySelector('.agentes__motivo')!.textContent).toContain(TEXTO_RECHAZADOS);
      expect(elemento().querySelector('ul')).toBeNull();
      // Ni otra vuelta ni más peticiones (lo comprueba http.verify)
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 2);
    }

    it('primero el 404 y luego el 403: se queda con la explicación y deja de preguntar', async () => {
      seguirDos();
      http.expectOne(`${URL_EJECUCIONES}/6`).flush(null, NO_ESTA);
      http.expectOne(`${URL_EJECUCIONES}/5`).flush(null, PROHIBIDO);

      await compruebaQueQuedaRechazada();
    });

    it('primero el 403 y luego el 404: lo mismo', async () => {
      seguirDos();
      http.expectOne(`${URL_EJECUCIONES}/6`).flush(null, PROHIBIDO);
      http.expectOne(`${URL_EJECUCIONES}/5`).flush(null, NO_ESTA);

      await compruebaQueQuedaRechazada();
    });

    it('si después del 404 y el 403 el uso contesta bien, vuelve la lista sin entrar en bucle', async () => {
      seguirDos();
      http.expectOne(`${URL_EJECUCIONES}/6`).flush(null, NO_ESTA);
      http.expectOne(`${URL_EJECUCIONES}/5`).flush(null, PROHIBIDO);
      fixture.detectChanges();
      await Promise.resolve();

      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
      fixture.detectChanges();
      responder([crearEjecucion({ id: 5, candidaturaId: 20, estado: 'Ok' })]);

      expect(elemento().querySelector('.agentes__motivo')).toBeNull();
      expect(filas()).toHaveLength(1);
    });
  });
});
