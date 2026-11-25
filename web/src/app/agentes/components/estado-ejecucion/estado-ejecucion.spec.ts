import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SistemaService } from '../../../core/services/sistema.service';
import { crearSistemaInfo } from '../../../core/utils/sistema-de-prueba';
import { FechaActualService } from '../../../shared/services/fecha-actual.service';
import { EjecucionAgente } from '../../interfaces/ejecucion.interface';
import { AgentesService, INTERVALO_SEGUIMIENTO_MS } from '../../services/agentes.service';
import { crearEjecucion, crearEstadoAgentes } from '../../utils/ejecucion-de-prueba';
import {
  avisoCambio,
  avisoCambioCorto,
  TEXTO_PERDIDA,
  TEXTO_PERDIDA_CORTO,
} from '../../utils/textos-ejecucion';
import { EstadoEjecucion, INTERVALO_RELOJ_MS, PAUSA_CANCELAR_MS } from './estado-ejecucion';

const URL_EJECUCIONES = '/api/agentes/ejecuciones';
const AHORA = new Date('2026-11-23T10:01:20Z');

describe('EstadoEjecucion', () => {
  let fixture: ComponentFixture<EstadoEjecucion>;
  let http: HttpTestingController;

  // Sin el temporizador de medianoche del de verdad: los tests cuentan los temporizadores
  const fechaActual = signal(AHORA);

  beforeEach(() => {
    fechaActual.set(AHORA);
    vi.useFakeTimers();
    vi.setSystemTime(AHORA);
    TestBed.configureTestingModule({
      imports: [EstadoEjecucion],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: FechaActualService, useValue: { ahora: fechaActual } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    fixture?.destroy();
    http.verify();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  /** Con agentes disponibles, el servicio sigue las que no han terminado y deja cancelar y reintentar */
  function conAgentes(): void {
    TestBed.inject(SistemaService).cargarInfo();
    http.expectOne('/api/sistema/info').flush(crearSistemaInfo({ agentesDisponibles: true }));
  }

  function crear(
    ejecucion: EjecucionAgente,
    entradas: {
      compacta?: boolean;
      topeUsd?: number | null;
      contexto?: string;
      permitirReintentar?: boolean;
      protegerDobleToque?: boolean;
    } = {},
  ): HTMLElement {
    fixture = TestBed.createComponent(EstadoEjecucion);
    fixture.componentRef.setInput('ejecucion', ejecucion);
    for (const [nombre, valor] of Object.entries(entradas)) {
      fixture.componentRef.setInput(nombre, valor);
    }
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function pintar(): void {
    fixture.detectChanges();
  }

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function insignia(): string {
    return elemento().querySelector('.estado-ejecucion__insignia')!.textContent!.trim();
  }

  function anuncio(): HTMLElement {
    return elemento().querySelector<HTMLElement>('[role="status"]')!;
  }

  function texto(): string {
    return elemento()
      .querySelector('.estado-ejecucion__texto')!
      .textContent!.replace(/\s+/g, ' ')
      .trim();
  }

  function boton(nombre: string): HTMLButtonElement | undefined {
    return [...elemento().querySelectorAll('button')].find((b) => b.textContent?.includes(nombre));
  }

  /** Con disabledInteractive, Material deja el botón enfocable y lo marca con aria-disabled */
  function deshabilitado(b: HTMLButtonElement): boolean {
    return b.getAttribute('aria-disabled') === 'true';
  }

  function ponerVisibilidad(estado: DocumentVisibilityState): void {
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue(estado);
    document.dispatchEvent(new Event('visibilitychange'));
  }

  const enCurso = (cambios: Partial<EjecucionAgente> = {}) =>
    crearEjecucion({ estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z', ...cambios });

  describe('cada estado con su insignia y su texto', () => {
    it.each<[string, Partial<EjecucionAgente>, string, string]>([
      [
        'en cola por turno',
        { espera: 'Turno' },
        'En cola',
        'En cola: hay otra tarea de Claude en marcha.',
      ],
      [
        'esperando el bloqueo',
        { espera: 'BloqueoOcupado', quienBloquea: 'la tarea de los lunes' },
        'En cola',
        'Esperando: la tarea de los lunes está usando Claude. Empieza sola cuando termine.',
      ],
      [
        'en curso, con el tiempo',
        { estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' },
        'En curso',
        'Analizando el encaje… · 1 min 20 s (máximo 5 min)',
      ],
      [
        'hecha, con la duración y el uso',
        {
          estado: 'Ok',
          inicio: '2026-11-23T10:00:00Z',
          fin: '2026-11-23T10:02:10Z',
          costeUsd: 0.15,
        },
        'Hecha',
        'Hecha en 2 min 10 s · uso estimado: 1 % del tope de hoy',
      ],
      [
        'no ha salido, con el error',
        { estado: 'Error', error: 'Ha tardado más de 5 min y la he parado.' },
        'No ha salido',
        'No ha salido. Ha tardado más de 5 min y la he parado.',
      ],
      [
        'cancelada antes de empezar',
        { estado: 'Cancelada' },
        'Cancelada',
        'Cancelada antes de empezar. No ha usado nada.',
      ],
      [
        'cancelada en curso, con su uso',
        { estado: 'Cancelada', inicio: '2026-11-23T10:00:00Z', costeUsd: 0.15 },
        'Cancelada',
        'Cancelada antes de terminar · uso estimado: 1 % del tope de hoy',
      ],
    ])('%s', (_caso, cambios, etiqueta, frase) => {
      crear(crearEjecucion(cambios), { topeUsd: 15 });

      expect(insignia()).toBe(etiqueta);
      expect(
        elemento().querySelector('.estado-ejecucion__insignia svg path')!.getAttribute('d'),
      ).toBeTruthy();
      expect(texto()).toBe(frase);
    });

    it('sin tope de entrada, usa el del estado de los agentes', () => {
      conAgentes();
      const agentes = TestBed.inject(AgentesService);
      agentes.cargarEstado();
      http.expectOne('/api/agentes/estado').flush(crearEstadoAgentes());

      crear(
        crearEjecucion({
          estado: 'Ok',
          inicio: '2026-11-23T10:00:00Z',
          fin: '2026-11-23T10:00:30Z',
          costeUsd: 0.45,
        }),
      );

      expect(texto()).toBe('Hecha en 30 s · uso estimado: 3 % del tope de hoy');
    });

    it('la región que se anuncia es role="status" con aria-live polite y el grupo lleva el nombre de la tarea', () => {
      crear(crearEjecucion());

      expect(anuncio().getAttribute('aria-live')).toBe('polite');
      expect(elemento().querySelector('[role="group"]')!.getAttribute('aria-label')).toBe(
        'Tarea de Claude: Análisis del encaje',
      );
    });

    it('con contexto, el grupo lo añade a su nombre', () => {
      crear(crearEjecucion(), { contexto: 'Acme' });

      expect(elemento().querySelector('[role="group"]')!.getAttribute('aria-label')).toBe(
        'Tarea de Claude: Análisis del encaje · Acme',
      );
    });

    it('compacta: sin región que se anuncie', () => {
      crear(crearEjecucion(), { compacta: true });

      expect(elemento().querySelector('[role="status"]')).toBeNull();
      expect(elemento().querySelector('[aria-live]')).toBeNull();
    });
  });

  describe('botones', () => {
    beforeEach(() => conAgentes());

    it.each<[Partial<EjecucionAgente>, boolean, boolean]>([
      [{ estado: 'EnCola' }, true, false],
      [{ estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' }, true, false],
      [{ estado: 'Ok' }, false, false],
      [{ estado: 'Error' }, false, true],
      [{ estado: 'Cancelada' }, false, true],
    ])('%o: Cancelar %s, Reintentar %s', (cambios, cancelar, reintentar) => {
      crear(crearEjecucion(cambios));

      expect(boton('Cancelar') !== undefined).toBe(cancelar);
      expect(boton('Reintentar') !== undefined).toBe(reintentar);
    });

    it('en curso con la cancelación ya pedida no ofrece cancelar otra vez', () => {
      crear(enCurso({ puedeCancelar: false }));

      expect(boton('Cancelar')).toBeUndefined();
      expect(texto()).toBe('Cancelando… se parará en unos segundos.');
    });

    it('ningún mat-flat-button ni relleno lima; los botones van con borde y con nombre accesible', () => {
      for (const ejecucion of [crearEjecucion(), crearEjecucion({ estado: 'Error' })]) {
        crear(ejecucion);
        const html = elemento().innerHTML;

        expect(elemento().querySelector('[mat-flat-button], [mat-raised-button]')).toBeNull();
        expect(elemento().querySelector('.mat-mdc-unelevated-button')).toBeNull();
        expect(html).not.toMatch(/fosforito|boton-enviar/);
        for (const b of elemento().querySelectorAll('button')) {
          expect(b.hasAttribute('mat-stroked-button')).toBe(true);
        }
        fixture.destroy();
      }
    });

    it('«Cancelar la tarea» en rojo con borde y nombre con la tarea; en la compacta, «Cancelar»', () => {
      crear(crearEjecucion());

      expect(boton('Cancelar la tarea')!.classList).toContain('boton-peligro-borde');
      expect(boton('Cancelar')!.getAttribute('aria-label')).toBe(
        'Cancelar la tarea: Análisis del encaje',
      );
      fixture.destroy();

      crear(crearEjecucion(), { compacta: true, contexto: 'Acme' });
      expect(boton('Cancelar')!.textContent!.trim()).toBe('Cancelar');
      expect(boton('Cancelar')!.getAttribute('aria-label')).toBe(
        'Cancelar: Análisis del encaje · Acme',
      );
    });

    it('«Reintentar» con el contexto en su nombre', () => {
      crear(crearEjecucion({ estado: 'Error' }), { contexto: 'Acme' });

      expect(boton('Reintentar')!.getAttribute('aria-label')).toBe(
        'Reintentar: Análisis del encaje · Acme',
      );
    });

    it('compacta: al pasar la medianoche, lo de ayer ya es «del tope diario»', () => {
      crear(crearEjecucion({ estado: 'Ok', inicio: '2026-11-23T10:00:00Z', costeUsd: 0.15 }), {
        compacta: true,
        topeUsd: 15,
      });
      expect(texto()).toContain('del tope de hoy');

      fechaActual.set(new Date('2026-11-24T08:00:00Z'));
      pintar();
      expect(texto()).toContain('del tope diario');
    });

    it('con permitirReintentar a false no ofrece reintentar, pero sí cancelar', () => {
      crear(crearEjecucion({ estado: 'Error' }), { permitirReintentar: false });
      expect(boton('Reintentar')).toBeUndefined();
      fixture.destroy();

      crear(crearEjecucion(), { permitirReintentar: false });
      expect(boton('Cancelar')).toBeDefined();
    });
  });

  it('sin agentes disponibles no salen los botones (no harían nada)', () => {
    crear(crearEjecucion());
    expect(boton('Cancelar')).toBeUndefined();
    fixture.destroy();

    crear(crearEjecucion({ estado: 'Error' }));
    expect(boton('Reintentar')).toBeUndefined();
  });

  describe('avisos rojos', () => {
    it('sin cambios en el registro ni en el perfil, ningún aviso', () => {
      crear(crearEjecucion({ estado: 'Ok' }));

      expect(elemento().querySelector('[role="alert"]')).toBeNull();
      expect(elemento().querySelector('.estado-ejecucion__aviso')).toBeNull();
    });

    it('con registroCambiado y perfilCambiado, un role="alert" por cada uno', () => {
      crear(crearEjecucion({ estado: 'Ok', registroCambiado: true, perfilCambiado: true }));

      const alertas = [...elemento().querySelectorAll('[role="alert"]')].map((a) =>
        a.textContent!.trim(),
      );
      expect(alertas).toEqual([avisoCambio('registro'), avisoCambio('perfil')]);
    });

    it('solo con registroCambiado, solo el suyo', () => {
      crear(crearEjecucion({ estado: 'Ok', registroCambiado: true }));

      const alertas = elemento().querySelectorAll('[role="alert"]');
      expect(alertas.length).toBe(1);
      expect(alertas[0].textContent).toContain('tu registro de candidaturas');
    });

    it('compacta (la lista de /agentes): el aviso corto se ve, pero sin role="alert"', () => {
      crear(crearEjecucion({ estado: 'Ok', perfilCambiado: true }), { compacta: true });

      expect(elemento().querySelector('.estado-ejecucion__aviso')!.textContent!.trim()).toBe(
        avisoCambioCorto('perfil'),
      );
      expect(elemento().querySelector('[role="alert"]')).toBeNull();
    });
  });

  describe('en curso', () => {
    it('el tiempo avanza cada segundo, pero lo que se anuncia no cambia', () => {
      crear(enCurso());
      const frase = anuncio().textContent!.trim();

      vi.advanceTimersByTime(INTERVALO_RELOJ_MS);
      pintar();

      expect(texto()).toBe('Analizando el encaje… · 1 min 21 s (máximo 5 min)');
      expect(anuncio().textContent!.trim()).toBe(frase);
      expect(frase).toBe('Analizando el encaje…');
    });

    it('barra de progreso solo en curso y sin compacta', () => {
      crear(enCurso());
      expect(elemento().querySelector('mat-progress-bar')).not.toBeNull();
      fixture.destroy();

      crear(enCurso(), { compacta: true });
      expect(elemento().querySelector('mat-progress-bar')).toBeNull();
      fixture.destroy();

      crear(crearEjecucion());
      expect(elemento().querySelector('mat-progress-bar')).toBeNull();
    });

    it('sin reloj fuera de en curso, con la pestaña oculta ni tras destruirse', () => {
      crear(crearEjecucion({ estado: 'Ok' }));
      expect(vi.getTimerCount()).toBe(0);
      fixture.destroy();

      crear(enCurso());
      expect(vi.getTimerCount()).toBe(1);

      ponerVisibilidad('hidden');
      pintar();
      expect(vi.getTimerCount()).toBe(0);

      ponerVisibilidad('visible');
      pintar();
      expect(vi.getTimerCount()).toBe(1);

      fixture.destroy();
      expect(vi.getTimerCount()).toBe(0);
    });

    it('cancelándose (sin tiempo que enseñar) no corre el reloj', () => {
      crear(enCurso({ puedeCancelar: false }));

      expect(vi.getTimerCount()).toBe(0);
    });
  });

  describe('acciones', () => {
    beforeEach(() => conAgentes());

    it('cancelar en cola: deshabilitado mientras responde y, cancelada, ofrece reintentar y avisa una vez', () => {
      const cancelar = vi.spyOn(TestBed.inject(AgentesService), 'cancelar');
      crear(crearEjecucion());
      const terminadas: EjecucionAgente[] = [];
      fixture.componentInstance.terminada.subscribe((e) => terminadas.push(e));

      boton('Cancelar')!.click();
      pintar();
      expect(deshabilitado(boton('Cancelando')!)).toBe(true);
      expect(boton('Cancelando')!.getAttribute('aria-label')).toBe(
        'Cancelando: Análisis del encaje',
      );

      // Un segundo clic mientras va el primero no manda otra
      boton('Cancelando')!.click();
      expect(cancelar).toHaveBeenCalledTimes(1);
      const peticion = http.expectOne(`${URL_EJECUCIONES}/1/cancelar`);
      expect(peticion.request.method).toBe('POST');
      peticion.flush(crearEjecucion({ estado: 'Cancelada' }));
      pintar();

      expect(insignia()).toBe('Cancelada');
      expect(boton('Cancelar')).toBeUndefined();
      expect(deshabilitado(boton('Reintentar')!)).toBe(false);
      expect(terminadas.map((e) => e.estado)).toEqual(['Cancelada']);
    });

    it('si el botón con el foco desaparece, el foco pasa a la tarjeta y no a <body>', () => {
      crear(crearEjecucion());
      boton('Cancelar')!.focus();

      boton('Cancelar')!.click();
      http
        .expectOne(`${URL_EJECUCIONES}/1/cancelar`)
        .flush(crearEjecucion({ estado: 'Cancelada' }));
      pintar();

      expect(document.activeElement).toBe(elemento().querySelector('[role="group"]'));
    });

    it('si el foco ya se había ido a otra parte, no salta a la tarjeta', async () => {
      crear(crearEjecucion());
      boton('Cancelar')!.focus();
      boton('Cancelar')!.blur();
      await Promise.resolve();

      boton('Cancelar')!.click();
      http
        .expectOne(`${URL_EJECUCIONES}/1/cancelar`)
        .flush(crearEjecucion({ estado: 'Cancelada' }));
      pintar();

      expect(document.activeElement).toBe(document.body);
    });

    it('si cancelar falla, lo dice con role="alert" y se puede volver a intentar', () => {
      crear(crearEjecucion());

      boton('Cancelar')!.click();
      http
        .expectOne(`${URL_EJECUCIONES}/1/cancelar`)
        .flush({ title: 'Ya ha terminado.' }, { status: 409, statusText: 'Conflict' });
      pintar();

      expect(
        elemento().querySelector('.estado-ejecucion__error-accion')!.getAttribute('role'),
      ).toBe('alert');
      expect(elemento().querySelector('.estado-ejecucion__error-accion')!.textContent).toContain(
        'No se ha podido cancelar.',
      );
      expect(deshabilitado(boton('Cancelar')!)).toBe(false);

      // El fallo deja de valer en cuanto cambia el estado
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      http.expectOne(`${URL_EJECUCIONES}/1`).flush(enCurso());
      pintar();
      expect(elemento().querySelector('.estado-ejecucion__error-accion')).toBeNull();
    });

    it('si cambia la ejecución con una acción en vuelo, la respuesta vieja no cae en la nueva', () => {
      crear(crearEjecucion());
      boton('Cancelar')!.click();
      pintar();

      fixture.componentRef.setInput('ejecucion', crearEjecucion({ id: 2 }));
      pintar();
      expect(deshabilitado(boton('Cancelar')!)).toBe(false);

      http
        .expectOne(`${URL_EJECUCIONES}/1/cancelar`)
        .flush({ title: 'Ya ha terminado.' }, { status: 409, statusText: 'Conflict' });
      pintar();

      expect(insignia()).toBe('En cola');
      expect(elemento().querySelector('.estado-ejecucion__error-accion')).toBeNull();
      expect(deshabilitado(boton('Cancelar')!)).toBe(false);
    });

    it('reintentar: enseña la nueva, la sigue y la emite', () => {
      crear(crearEjecucion({ estado: 'Error', error: 'Se interrumpió.' }));
      const reintentadas: EjecucionAgente[] = [];
      fixture.componentInstance.reintentada.subscribe((e) => reintentadas.push(e));

      boton('Reintentar')!.click();
      pintar();
      expect(deshabilitado(boton('Reintentando')!)).toBe(true);
      const nueva = crearEjecucion({ id: 2, espera: 'Turno' });
      http.expectOne(`${URL_EJECUCIONES}/1/reintentar`).flush(nueva);
      pintar();

      expect(reintentadas).toEqual([nueva]);
      expect(insignia()).toBe('En cola');
      expect(texto()).toBe('En cola: hay otra tarea de Claude en marcha.');
      expect(boton('Cancelar')).toBeDefined();

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      http.expectOne(`${URL_EJECUCIONES}/2`).flush(crearEjecucion({ id: 2, estado: 'Ok' }));
      pintar();
      expect(insignia()).toBe('Hecha');
    });
  });

  describe('con protegerDobleToque', () => {
    beforeEach(() => conAgentes());

    it('al aparecer, «Cancelar» no hace caso hasta pasado un doble toque', () => {
      crear(crearEjecucion(), { protegerDobleToque: true });

      boton('Cancelar')!.click();
      vi.advanceTimersByTime(PAUSA_CANCELAR_MS - 1);
      boton('Cancelar')!.click();
      http.expectNone(`${URL_EJECUCIONES}/1/cancelar`);

      vi.advanceTimersByTime(1);
      boton('Cancelar')!.click();
      http
        .expectOne(`${URL_EJECUCIONES}/1/cancelar`)
        .flush(crearEjecucion({ estado: 'Cancelada' }));
    });

    it('tras reintentar, el «Cancelar» de la nueva tampoco cuenta el segundo toque', () => {
      crear(crearEjecucion({ estado: 'Error', error: 'Se interrumpió.' }), {
        protegerDobleToque: true,
      });
      vi.advanceTimersByTime(PAUSA_CANCELAR_MS);
      boton('Reintentar')!.click();
      http.expectOne(`${URL_EJECUCIONES}/1/reintentar`).flush(crearEjecucion({ id: 2 }));
      pintar();

      boton('Cancelar')!.click();
      http.expectNone(`${URL_EJECUCIONES}/2/cancelar`);
      vi.advanceTimersByTime(PAUSA_CANCELAR_MS);
      boton('Cancelar')!.click();
      http
        .expectOne(`${URL_EJECUCIONES}/2/cancelar`)
        .flush(crearEjecucion({ id: 2, estado: 'Cancelada' }));
    });
  });

  describe('seguimiento', () => {
    beforeEach(() => conAgentes());

    it('sigue la que llega sin terminar, pinta la versión nueva y avisa una vez al terminar', () => {
      crear(enCurso(), { topeUsd: 15 });
      const terminadas: EjecucionAgente[] = [];
      fixture.componentInstance.terminada.subscribe((e) => terminadas.push(e));

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      const hecha = crearEjecucion({
        estado: 'Ok',
        inicio: '2026-11-23T10:00:00Z',
        fin: '2026-11-23T10:01:00Z',
        costeUsd: 0,
      });
      http.expectOne(`${URL_EJECUCIONES}/1`).flush(hecha);
      pintar();

      expect(insignia()).toBe('Hecha');
      expect(texto()).toBe('Hecha en 1 min · no ha usado nada de tu tope de hoy');
      expect(terminadas).toEqual([hecha]);
      expect(elemento().querySelector('mat-progress-bar')).toBeNull();

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 3);
      http.expectNone(`${URL_EJECUCIONES}/1`);
      expect(terminadas.length).toBe(1);
    });

    it('con la entrada sin terminar y el mapa ya terminada: avisa al momento, una vez, sin pedir nada', () => {
      const agentes = TestBed.inject(AgentesService);
      agentes.ultimaDe('AnalizarEncaje', 12).subscribe();
      const hecha = crearEjecucion({ estado: 'Ok' });
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush([hecha]);
      const terminadas: EjecucionAgente[] = [];

      fixture = TestBed.createComponent(EstadoEjecucion);
      fixture.componentInstance.terminada.subscribe((e) => terminadas.push(e));
      fixture.componentRef.setInput('ejecucion', crearEjecucion());
      pintar();
      pintar();

      expect(insignia()).toBe('Hecha');
      expect(terminadas).toEqual([hecha]);
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 2);
      http.expectNone(`${URL_EJECUCIONES}/1`);
    });

    it('la que ya llega terminada no avisa', () => {
      const terminadas: EjecucionAgente[] = [];
      fixture = TestBed.createComponent(EstadoEjecucion);
      fixture.componentInstance.terminada.subscribe((e) => terminadas.push(e));
      fixture.componentRef.setInput('ejecucion', crearEjecucion({ estado: 'Ok' }));
      pintar();

      expect(terminadas).toEqual([]);
    });

    it('perdida en la compacta: el texto corto', () => {
      crear(enCurso(), { compacta: true });

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      http
        .expectOne(`${URL_EJECUCIONES}/1`)
        .flush({ title: 'No existe.' }, { status: 404, statusText: 'Not Found' });
      pintar();

      expect(texto()).toBe(TEXTO_PERDIDA_CORTO);
    });

    it('si la API ya no la encuentra, lo dice, quita los botones, para el reloj y emite perdida', () => {
      crear(enCurso());
      const perdidas: number[] = [];
      fixture.componentInstance.perdida.subscribe((id) => perdidas.push(id));

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      http
        .expectOne(`${URL_EJECUCIONES}/1`)
        .flush({ title: 'No existe.' }, { status: 404, statusText: 'Not Found' });
      pintar();

      expect(insignia()).toBe('No se encuentra');
      expect(anuncio().textContent!.trim()).toBe(TEXTO_PERDIDA);
      expect(texto()).toBe(TEXTO_PERDIDA);
      expect(elemento().querySelector('button')).toBeNull();
      expect(elemento().querySelector('mat-progress-bar')).toBeNull();
      expect(perdidas).toEqual([1]);
      expect(vi.getTimerCount()).toBe(0);
    });
  });
});
