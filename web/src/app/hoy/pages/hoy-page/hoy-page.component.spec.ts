import { BreakpointObserver } from '@angular/cdk/layout';
import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { crearDetalle } from '../../../candidaturas/utils/candidatura-de-prueba';
import { crearSistemaInfo } from '../../../core/utils/sistema-de-prueba';
import { DialogoRecordatorio } from '../../../recordatorios/components/dialogo-recordatorio/dialogo-recordatorio';
import { RecordatoriosService } from '../../../recordatorios/services/recordatorios.service';
import {
  crearPanel,
  crearRecordatorio,
  crearRecordatorioAgenda,
  crearResumenHoy,
} from '../../../recordatorios/utils/recordatorio-de-prueba';
import { AvisosService } from '../../../shared/services/avisos.service';
import { FechaActualService } from '../../../shared/services/fecha-actual.service';
import { PanelHoy } from '../../interfaces/panel-hoy.interface';
import HoyPageComponent from './hoy-page.component';

describe('HoyPageComponent', () => {
  let fixture: ComponentFixture<HoyPageComponent>;
  let http: HttpTestingController;
  let abrirDialogo: ReturnType<typeof vi.fn>;
  let avisos: Record<'exito' | 'error' | 'deshacible', ReturnType<typeof vi.fn>>;
  let ahora: ReturnType<typeof signal<Date>>;

  const VENCIDO = crearRecordatorioAgenda({
    id: 1,
    titulo: 'Publicar en LinkedIn: post de Supabase',
    fechaHora: '2026-11-03T08:00:00Z',
  });
  const SEGUIMIENTO = crearRecordatorioAgenda({
    id: 2,
    candidaturaId: 7,
    tipo: 'Seguimiento',
    titulo: 'Seguimiento: Desarrollador .NET en Consultora Ejemplo',
    fechaHora: '2026-11-13T08:00:00Z',
    candidatura: {
      id: 7,
      empresa: 'Consultora Ejemplo',
      puesto: 'Desarrollador .NET',
      estado: 'Enviada',
      enlace: null,
    },
  });
  const POST_17 = crearRecordatorioAgenda({
    id: 3,
    titulo: 'Publicar en LinkedIn: post de Angular',
    fechaHora: '2026-11-17T08:00:00Z',
  });
  const DICIEMBRE = crearRecordatorioAgenda({
    id: 4,
    tipo: 'Plazo',
    titulo: 'Renovar la demanda',
    fechaHora: '2026-12-01T08:00:00Z',
  });
  const HECHO = crearRecordatorioAgenda({
    id: 5,
    tipo: 'Otro',
    titulo: 'Llamar a la gestoría',
    hecho: true,
    fechaHecho: '2026-11-13T08:30:00Z',
  });

  const COMPLETO = crearPanel({
    paraHoy: [VENCIDO, SEGUIMIENTO],
    proximos: [POST_17],
    masAdelante: [DICIEMBRE],
    hechosHoy: [HECHO],
    sinRespuesta: [
      {
        id: 20,
        empresa: 'Acme',
        puesto: 'Dev Angular',
        fechaEnvio: '2026-11-01T09:00:00Z',
        diasDesdeEnvio: 12,
      },
    ],
    sinRespuestaAntiguas: 9,
  });

  /** Pantalla de escritorio (desde lg): con el panel de la derecha. Por defecto, el móvil */
  let grande = false;
  /** La URL con la que se entra, para probar «?ver=» */
  let urlInicial: string | null = null;

  beforeEach(() => {
    grande = false;
    urlInicial = null;
  });

  /**
   * Crea la página y deja la petición del panel esperando. `antesDeEntrar` corre con los servicios ya
   * listos y antes de crear la página (para dejar el panel de una visita anterior)
   */
  async function crearSinResponder(antesDeEntrar?: () => void): Promise<void> {
    abrirDialogo = vi.fn(() => ({ afterClosed: () => of(undefined) }));
    avisos = { exito: vi.fn(), error: vi.fn(), deshacible: vi.fn() };
    // Viernes 13/11/2026 a las 10:00 de Madrid
    ahora = signal(new Date('2026-11-13T09:00:00Z'));
    TestBed.configureTestingModule({
      imports: [HoyPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: { open: abrirDialogo } },
        { provide: AvisosService, useValue: avisos },
        { provide: FechaActualService, useValue: { ahora } },
        {
          provide: BreakpointObserver,
          useValue: {
            isMatched: () => grande,
            observe: () => of({ matches: grande, breakpoints: {} }),
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    if (urlInicial !== null) {
      await TestBed.inject(Router).navigateByUrl(urlInicial);
    }
    antesDeEntrar?.();
    fixture = TestBed.createComponent(HoyPageComponent);
    await fixture.whenStable();
    // El indicador de la API pregunta al entrar
    http.expectOne('/api/sistema/info').flush(crearSistemaInfo());
  }

  async function responderPanel(panel: PanelHoy): Promise<void> {
    http.expectOne('/api/panel/hoy').flush(panel);
    await fixture.whenStable();
  }

  async function crear(panel: PanelHoy = COMPLETO): Promise<void> {
    await crearSinResponder();
    await responderPanel(panel);
  }

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function texto(): string {
    return elemento().textContent!.replace(/\s+/g, ' ');
  }

  /** Los títulos que se ven: los encabezados ocultos de las plegadas repiten el de su summary */
  function titulos(): string[] {
    return [...elemento().querySelectorAll('h2:not(.sr-only), summary')].map((t) =>
      t.textContent!.replace(/\s+/g, ' ').trim(),
    );
  }

  function rellenos(): HTMLElement[] {
    return [...elemento().querySelectorAll<HTMLElement>('[mat-flat-button]')];
  }

  function boton(etiqueta: string): HTMLButtonElement {
    return elemento().querySelector<HTMLButtonElement>(`button[aria-label="${etiqueta}"]`)!;
  }

  function botonPorTexto(contenido: string): HTMLButtonElement {
    return [...elemento().querySelectorAll('button')].find((b) =>
      b.textContent?.includes(contenido),
    )!;
  }

  function opcionesDelMenu(): HTMLButtonElement[] {
    return [...document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')];
  }

  /** Un parámetro de la URL («?ver=2», «?tipo=Entrevista», «?dia=2026-11-18»), o null si no está */
  function parametro(nombre: string): string | null {
    const consulta = TestBed.inject(Location).path().split('?')[1] ?? '';
    return new URLSearchParams(consulta).get(nombre);
  }

  function deshacerDelAviso(): () => boolean {
    return avisos.deshacible.mock.calls[0][1] as () => boolean;
  }

  describe('carga y error', () => {
    it('mientras carga enseña la barra y nada más', async () => {
      await crearSinResponder();

      expect(elemento().querySelector('mat-progress-bar')).not.toBeNull();
      expect(elemento().querySelector('h2')).toBeNull();
      expect(texto()).not.toContain('Nada pendiente para hoy.');

      await responderPanel(crearPanel());
      expect(elemento().querySelector('mat-progress-bar')).toBeNull();
    });

    it('si falla lo anuncia, y «Reintentar» vuelve a pedirlo', async () => {
      await crearSinResponder();
      http.expectOne('/api/panel/hoy').flush(null, { status: 503, statusText: 'x' });
      await fixture.whenStable();

      const alerta = elemento().querySelector('[role="alert"]')!;
      expect(alerta.textContent).toContain('No se puede conectar con RadarEmpleo');
      expect(texto()).not.toContain('Nada pendiente para hoy.');

      botonPorTexto('Reintentar').click();
      await fixture.whenStable();
      // El botón va a desaparecer: el foco no se queda en él
      expect(document.activeElement).toBe(elemento().querySelector('section[tabindex="-1"]'));
      await responderPanel(COMPLETO);

      expect(elemento().querySelector('[role="alert"]')).toBeNull();
      expect(titulos()).toContain('Para hoy · 2');
    });
  });

  describe('sin nada', () => {
    it('enseña la taza, el texto y las dos salidas, las dos con borde', async () => {
      await crear(crearPanel());

      expect(texto()).toContain('Nada pendiente para hoy.');
      expect(texto()).toContain('De momento, café tranquilo.');
      expect(elemento().querySelector('svg[viewBox="0 0 120 120"]')).not.toBeNull();
      const enlaces = [...elemento().querySelectorAll<HTMLAnchorElement>('a[href]')];
      expect(enlaces.map((a) => [a.textContent!.trim(), a.getAttribute('href')])).toEqual([
        ['Buscar nuevas ofertas', '/rastreos'],
        ['Ir al tablero', '/tablero'],
      ]);
      expect(enlaces.every((a) => a.hasAttribute('mat-stroked-button'))).toBe(true);
      expect(titulos()).toEqual([]);
    });

    it('«Nuevo recordatorio» sigue a mano y es el único relleno', async () => {
      await crear(crearPanel());

      expect(rellenos().map((b) => b.textContent!.trim())).toEqual(['Nuevo recordatorio']);
    });
  });

  describe('con recordatorios', () => {
    it('pinta las secciones con sus totales, en orden', async () => {
      await crear();

      expect(titulos()).toEqual([
        // En el móvil, el resumen va arriba y plegado
        'Resumen',
        'Para hoy · 2',
        'Próximos 7 días · 1',
        // 1 de entre 7 y 30 días y 9 de más de un mes: el título las cuenta todas
        'Enviadas sin respuesta · 10',
        'Más adelante · 1',
        'Hechos hoy · 1',
      ]);
      expect(texto()).not.toContain('Nada pendiente para hoy.');
    });

    it('lo secundario va plegado', async () => {
      await crear();

      const plegables = [...elemento().querySelectorAll('details')];
      expect(plegables).toHaveLength(4);
      plegables.forEach((p) => expect(p.open).toBe(false));
    });

    it('el único relleno de la pantalla es «Nuevo recordatorio»', async () => {
      await crear();

      expect(rellenos().map((b) => b.textContent!.trim())).toEqual(['Nuevo recordatorio']);
    });

    it('los «Hecho» de «Para hoy» llevan el contorno con acento; los demás, el normal', async () => {
      await crear();

      const hechos = [...elemento().querySelectorAll('button[aria-label^="Hecho:"]')];
      expect(
        hechos.map((b) => [b.getAttribute('aria-label'), b.classList.contains('boton-acento')]),
      ).toEqual([
        ['Hecho: Publicar en LinkedIn: post de Supabase', true],
        ['Hecho: Seguimiento: Desarrollador .NET en Consultora Ejemplo', true],
        ['Hecho: Publicar en LinkedIn: post de Angular', false],
        ['Hecho: Renovar la demanda', false],
      ]);
      expect(hechos.every((b) => b.hasAttribute('mat-stroked-button'))).toBe(true);
    });

    it('lo vencido lo dice con texto', async () => {
      await crear();

      expect(texto()).toContain('Vencido · mar 3 nov');
    });

    it('lo de «Para hoy» y lo de «Próximos 7 días» se puede posponer desde la tarjeta', async () => {
      await crear();

      const posponer = [...elemento().querySelectorAll('button[aria-label^="Posponer:"]')];
      expect(posponer.map((b) => b.getAttribute('aria-label'))).toEqual([
        'Posponer: Publicar en LinkedIn: post de Supabase',
        'Posponer: Seguimiento: Desarrollador .NET en Consultora Ejemplo',
        'Posponer: Publicar en LinkedIn: post de Angular',
      ]);
    });

    it('posponer algo de los próximos días lo cuenta desde su día, y se puede deshacer', async () => {
      await crear();

      boton('Posponer: Publicar en LinkedIn: post de Angular').click();
      await fixture.whenStable();
      expect(opcionesDelMenu()[0].textContent).toContain('Un día después');
      opcionesDelMenu()[0].click();

      const peticion = http.expectOne('/api/recordatorios/3/posponer');
      expect(peticion.request.body).toEqual({ dia: '2026-11-18' });
      const pospuesto = { ...POST_17, fechaHora: '2026-11-18T08:00:00Z' };
      peticion.flush(pospuesto);
      await responderPanel({ ...COMPLETO, proximos: [pospuesto] });

      expect(avisos.deshacible).toHaveBeenCalledWith(
        'Pospuesto al mié 18 nov.',
        expect.any(Function),
      );
    });

    it('el de una candidatura enlaza a su ficha: se ve el puesto y la empresa va debajo', async () => {
      await crear();

      const enlace = elemento().querySelector<HTMLAnchorElement>('a[href="/candidaturas/7"]')!;
      // Sin «Seguimiento:» ni «en Consultora Ejemplo»: ya lo dicen la insignia y la línea de abajo
      expect(enlace.textContent?.trim()).toBe('Desarrollador .NET');
      expect(enlace.getAttribute('aria-label')).toBe(
        'Seguimiento: Desarrollador .NET en Consultora Ejemplo',
      );
      expect(texto()).not.toContain('Seguimiento: Desarrollador .NET');
      expect(texto()).toContain('Consultora Ejemplo');
    });

    it('si la candidatura tiene la URL de la oferta, la tarjeta la abre en otra pestaña', async () => {
      const conOferta = {
        ...SEGUIMIENTO,
        candidatura: { ...SEGUIMIENTO.candidatura!, enlace: 'https://ejemplo.test/oferta/1' },
      };
      await crear({ ...COMPLETO, paraHoy: [VENCIDO, conOferta] });

      const ofertas = [...elemento().querySelectorAll<HTMLAnchorElement>('a[data-oferta]')];
      expect(ofertas).toHaveLength(1);
      expect(ofertas[0].getAttribute('href')).toBe('https://ejemplo.test/oferta/1');
      expect(ofertas[0].getAttribute('aria-label')).toBe(
        'Abrir la oferta de Desarrollador .NET en otra pestaña',
      );
    });

    it('en «Hechos hoy» se puede dejar pendiente otra vez', async () => {
      await crear();

      expect(boton('Pendiente otra vez: Llamar a la gestoría')).not.toBeNull();
    });

    it('las enviadas sin respuesta enlazan a su ficha y cuentan las de más de un mes', async () => {
      await crear();

      const fila = elemento().querySelector<HTMLAnchorElement>('a[href="/candidaturas/20"]')!;
      expect(fila.textContent!.replace(/\s+/g, ' ').trim()).toBe(
        'Acme · Dev Angular · enviada hace 12 días',
      );
      expect(fila.classList.contains('min-h-12')).toBe(true);
      expect(texto()).toContain('Y 9 más llevan más de un mes sin respuesta.');
    });

    it('con solo enviadas antiguas, sale la línea sin «Y … más» y no el estado vacío', async () => {
      await crear(crearPanel({ sinRespuestaAntiguas: 1 }));

      expect(titulos()).toEqual([
        'Resumen',
        'Para hoy · 0',
        'Próximos 7 días · 0',
        'Enviadas sin respuesta · 1',
      ]);
      expect(texto()).toContain('Lleva más de un mes sin respuesta.');
      expect(texto()).not.toContain('Y 1 más');
      expect(texto()).not.toContain('café tranquilo');
    });

    it('las plegadas vacías no se pintan, y las dos de arriba dicen que no hay nada', async () => {
      await crear(crearPanel({ masAdelante: [DICIEMBRE] }));

      expect(titulos()).toEqual([
        'Resumen',
        'Para hoy · 0',
        'Próximos 7 días · 0',
        'Más adelante · 1',
      ]);
      // Sin nada en los próximos 7 días, el aviso no da fecha
      expect(texto()).toContain('Todo despejado por hoy.');
      expect(texto()).not.toContain('Lo próximo es el');
      expect(texto()).toContain('Nada en los próximos 7 días.');
      // «Hecho» en lo futuro no lleva acento: no es lo que toca hoy
      expect(elemento().querySelector('.boton-acento')).toBeNull();
    });

    it('sin nada para hoy, «lo siguiente» es lo primero de los próximos días', async () => {
      await crear(crearPanel({ proximos: [POST_17], masAdelante: [DICIEMBRE] }));

      expect(texto()).toContain('Todo despejado por hoy. Lo próximo es el mar 17 nov (1 tarea).');
    });

    it('no pinta nada de «Esperan tu OK»', async () => {
      await crear();

      expect(texto()).not.toContain('Esperan tu OK');
    });
  });

  describe('«Hecho»', () => {
    it('lo manda con un toque, avisa con «Deshacer» y lleva el foco al siguiente «Hecho»', async () => {
      await crear();

      boton('Hecho: Publicar en LinkedIn: post de Supabase').click();
      const peticion = http.expectOne('/api/recordatorios/1/hecho');
      expect(peticion.request.body).toEqual({ hecho: true });
      peticion.flush({ ...crearRecordatorio(VENCIDO), hecho: true });
      await responderPanel({
        ...COMPLETO,
        paraHoy: [SEGUIMIENTO],
        hechosHoy: [{ ...VENCIDO, hecho: true }, HECHO],
      });

      expect(avisos.deshacible).toHaveBeenCalledWith(
        'Hecho: «Publicar en LinkedIn: post de Supabase».',
        expect.any(Function),
      );
      expect(titulos()).toContain('Para hoy · 1');
      const siguiente = boton('Hecho: Seguimiento: Desarrollador .NET en Consultora Ejemplo');
      expect(document.activeElement).toBe(siguiente);
    });

    it('«Deshacer» manda hecho:false', async () => {
      await crear();
      boton('Hecho: Publicar en LinkedIn: post de Supabase').click();
      http.expectOne('/api/recordatorios/1/hecho').flush({ ...VENCIDO, hecho: true });
      await responderPanel({ ...COMPLETO, paraHoy: [SEGUIMIENTO] });

      expect(deshacerDelAviso()()).toBe(true);

      const peticion = http.expectOne('/api/recordatorios/1/hecho');
      expect(peticion.request.body).toEqual({ hecho: false });
      peticion.flush(VENCIDO);
      await responderPanel(COMPLETO);
      expect(titulos()).toContain('Para hoy · 2');
    });

    it('al marcar el último de la lista, el foco va al anterior', async () => {
      await crear();

      boton('Hecho: Seguimiento: Desarrollador .NET en Consultora Ejemplo').click();
      http.expectOne('/api/recordatorios/2/hecho').flush({ ...SEGUIMIENTO, hecho: true });
      await responderPanel({ ...COMPLETO, paraHoy: [VENCIDO] });

      expect(document.activeElement).toBe(boton('Hecho: Publicar en LinkedIn: post de Supabase'));
    });

    it('si no queda ninguno, el foco va al título de la sección', async () => {
      await crear(crearPanel({ paraHoy: [VENCIDO], proximos: [POST_17] }));

      boton('Hecho: Publicar en LinkedIn: post de Supabase').click();
      http.expectOne('/api/recordatorios/1/hecho').flush({ ...VENCIDO, hecho: true });
      await responderPanel(crearPanel({ proximos: [POST_17] }));

      expect(document.activeElement).toBe(elemento().querySelector('#titulo-paraHoy'));
    });

    it('si la API lo rechaza con un 409, enseña el mensaje y vuelve a pedir el panel', async () => {
      await crear();

      boton('Hecho: Publicar en LinkedIn: post de Supabase').click();
      http
        .expectOne('/api/recordatorios/1/hecho')
        .flush(
          { detail: 'Otra pestaña acaba de cambiar este recordatorio.' },
          { status: 409, statusText: 'Conflict' },
        );
      await responderPanel(COMPLETO);

      expect(avisos.error).toHaveBeenCalledWith('Otra pestaña acaba de cambiar este recordatorio.');
      expect(avisos.deshacible).not.toHaveBeenCalled();
    });

    it('si el panel nuevo no llega, el foco no salta luego con el que llegue más tarde', async () => {
      await crear();

      boton('Hecho: Publicar en LinkedIn: post de Supabase').click();
      http.expectOne('/api/recordatorios/1/hecho').flush({ ...VENCIDO, hecho: true });
      http.expectOne('/api/panel/hoy').flush('Fallo', { status: 500, statusText: 'Error' });
      await fixture.whenStable();
      const enfocado = document.activeElement;

      // Más tarde llega otro panel, por ejemplo al cambiar el día
      ahora.set(new Date('2026-11-13T23:30:00Z'));
      await fixture.whenStable();
      await responderPanel({ ...COMPLETO, hoy: '2026-11-14', paraHoy: [SEGUIMIENTO] });

      expect(document.activeElement).toBe(enfocado);
    });

    it('un doble clic solo manda una petición', async () => {
      await crear();
      const hecho = boton('Hecho: Publicar en LinkedIn: post de Supabase');

      hecho.click();
      hecho.click();

      http.expectOne('/api/recordatorios/1/hecho').flush({ ...VENCIDO, hecho: true });
      await responderPanel({ ...COMPLETO, paraHoy: [SEGUIMIENTO] });
    });
  });

  describe('«Posponer»', () => {
    it('«Posponer» y «Mañana» mandan el día siguiente al del panel, y el aviso deja deshacerlo', async () => {
      // Un jueves: mañana es viernes
      await crear({ ...COMPLETO, hoy: '2026-11-12' });

      boton('Posponer: Publicar en LinkedIn: post de Supabase').click();
      await fixture.whenStable();
      expect(opcionesDelMenu()[0].textContent).toContain('Mañana');
      opcionesDelMenu()[0].click();

      const peticion = http.expectOne('/api/recordatorios/1/posponer');
      expect(peticion.request.body).toEqual({ dia: '2026-11-13' });
      const pospuesto = { ...VENCIDO, fechaHora: '2026-11-13T08:00:00Z' };
      peticion.flush(pospuesto);
      await responderPanel({ ...COMPLETO, paraHoy: [SEGUIMIENTO], proximos: [pospuesto, POST_17] });

      expect(avisos.deshacible).toHaveBeenCalledWith(
        'Pospuesto al vie 13 nov.',
        expect.any(Function),
      );
      expect(titulos()).toContain('Próximos 7 días · 2');

      // «Deshacer» lo devuelve a su día original (el 3, no «hoy»)
      deshacerDelAviso()();
      const vuelta = http.expectOne('/api/recordatorios/1/posponer');
      expect(vuelta.request.body).toEqual({ dia: '2026-11-03' });
      vuelta.flush(VENCIDO);
      await responderPanel(COMPLETO);
    });

    it('un viernes, la primera opción es «El lunes» en lugar de «Mañana»', async () => {
      // El panel es del viernes 13: mañana es sábado
      await crear();

      boton('Posponer: Publicar en LinkedIn: post de Supabase').click();
      await fixture.whenStable();
      expect(opcionesDelMenu()[0].textContent!.replace(/\s+/g, ' ').trim()).toBe(
        'El lunes lun 16 nov',
      );
      opcionesDelMenu()[0].click();

      const peticion = http.expectOne('/api/recordatorios/1/posponer');
      expect(peticion.request.body).toEqual({ dia: '2026-11-16' });
      peticion.flush({ ...VENCIDO, fechaHora: '2026-11-16T08:00:00Z' });
      await responderPanel(COMPLETO);
    });

    it('el día de «Mañana» sale del panel, no del reloj del navegador', async () => {
      await crear({ ...COMPLETO, hoy: '2026-12-31' });

      boton('Posponer: Publicar en LinkedIn: post de Supabase').click();
      await fixture.whenStable();
      opcionesDelMenu()[0].click();

      const peticion = http.expectOne('/api/recordatorios/1/posponer');
      expect(peticion.request.body).toEqual({ dia: '2027-01-01' });
      peticion.flush(VENCIDO);
      await responderPanel(COMPLETO);
    });
  });

  describe('diálogos', () => {
    it('«Nuevo recordatorio» abre el diálogo de uno suelto, como publicación de LinkedIn', async () => {
      await crear();

      botonPorTexto('Nuevo recordatorio').click();

      const [componente, opciones] = abrirDialogo.mock.calls[0];
      expect(componente).toBe(DialogoRecordatorio);
      expect(opciones.data).toEqual({ tipoInicial: 'PublicacionLinkedIn', candidatura: undefined });
    });

    it('«Cambiar fecha u hora…» abre el diálogo con el recordatorio y su candidatura', async () => {
      await crear();

      boton('Posponer: Seguimiento: Desarrollador .NET en Consultora Ejemplo').click();
      await fixture.whenStable();
      opcionesDelMenu().at(-1)!.click();

      expect(abrirDialogo.mock.calls[0][1].data).toEqual({
        recordatorio: SEGUIMIENTO,
        candidatura: SEGUIMIENTO.candidatura,
      });
    });

    it('borrar pregunta antes y, al confirmar, lo borra y lleva el foco al vecino', async () => {
      await crear();
      abrirDialogo.mockReturnValue({ afterClosed: () => of(true) });

      boton('Más acciones: Publicar en LinkedIn: post de Supabase').click();
      await fixture.whenStable();
      opcionesDelMenu()
        .find((o) => o.textContent?.includes('Borrar'))!
        .click();

      expect(abrirDialogo.mock.calls[0][1].data.titulo).toBe('Borrar recordatorio');
      const peticion = http.expectOne('/api/recordatorios/1');
      expect(peticion.request.method).toBe('DELETE');
      peticion.flush(null, { status: 204, statusText: 'No Content' });
      await responderPanel({ ...COMPLETO, paraHoy: [SEGUIMIENTO] });

      expect(avisos.exito).toHaveBeenCalledWith('Recordatorio borrado.');
      expect(document.activeElement).toBe(
        boton('Hecho: Seguimiento: Desarrollador .NET en Consultora Ejemplo'),
      );
    });
  });

  describe('resumen', () => {
    const RESUMEN = crearResumenHoy({
      candidaturasActivas: 12,
      entrevistasSemana: 2,
      respuesta: { enviadas: 8, conRespuesta: 2, porcentaje: 25 },
      semana: crearResumenHoy().semana.map((d) => ({
        ...d,
        recordatorios: { '2026-11-11': 2, '2026-11-13': 1 }[d.dia] ?? 0,
      })),
    });

    function cifras(): Record<string, string> {
      return Object.fromEntries(
        [...elemento().querySelectorAll('dl > div')].map((caja) => [
          caja.querySelector('dt')!.textContent!.trim(),
          caja.querySelector('dd')!.textContent!.replace(/\s+/g, ' ').trim(),
        ]),
      );
    }

    it('en el móvil va arriba, plegado, y no hay panel al lado', async () => {
      await crear({ ...COMPLETO, resumen: RESUMEN });

      const plegable = elemento().querySelector('details')!;
      expect(plegable.querySelector('summary')!.textContent).toContain('Resumen');
      expect(plegable.open).toBe(false);
      expect(plegable.querySelector('app-resumen-hoy')).not.toBeNull();
      expect(elemento().querySelector('aside')).toBeNull();
    });

    it('en escritorio va en un panel a la derecha, sin plegar', async () => {
      grande = true;
      await crear({ ...COMPLETO, resumen: RESUMEN });

      const panel = elemento().querySelector('aside')!;
      expect(panel.querySelector('h2')!.textContent).toContain('Resumen');
      expect(panel.getAttribute('aria-labelledby')).toBe(panel.querySelector('h2')!.id);
      expect(panel.classList.contains('sticky')).toBe(true);
      expect(panel.closest('details')).toBeNull();
      // Las tres plegadas de siempre: el resumen ya no es una de ellas
      expect(elemento().querySelectorAll('details')).toHaveLength(3);
    });

    it('enseña las tres cifras con su rótulo', async () => {
      grande = true;
      await crear({ ...COMPLETO, resumen: RESUMEN });

      expect(cifras()).toEqual({
        'Candidaturas activas': '12',
        'Entrevistas esta semana': '2',
        'Tasa de respuesta': '25 %',
      });
      expect(texto()).toContain('2 de 8 enviadas han llegado a entrevista u oferta.');
    });

    it('con una sola respuesta, el verbo va en singular', async () => {
      grande = true;
      await crear({
        ...COMPLETO,
        resumen: { ...RESUMEN, respuesta: { enviadas: 1, conRespuesta: 1, porcentaje: 100 } },
      });

      expect(texto()).toContain('1 de 1 enviada ha llegado a entrevista u oferta.');
    });

    it('sin enviadas, la tasa es una raya con su explicación', async () => {
      grande = true;
      await crear({ ...COMPLETO, resumen: crearResumenHoy() });

      const tasa = [...elemento().querySelectorAll('dl > div')].find((caja) =>
        caja.textContent!.includes('Tasa de respuesta'),
      )!;
      const raya = tasa.querySelector('dd [aria-hidden="true"]')!;
      expect(raya.textContent).toBe('—');
      expect(tasa.querySelector('.sr-only')!.textContent).toBe('Sin datos');
      expect(tasa.textContent).toContain('Todavía no hay candidaturas enviadas.');
      expect(tasa.textContent).not.toContain('%');
    });

    it('la semana va de lunes a domingo, con hoy destacado y una marca donde hay pendientes', async () => {
      grande = true;
      await crear({ ...COMPLETO, resumen: RESUMEN });

      const dias = [...elemento().querySelectorAll<HTMLElement>('aside ol button')];
      expect(dias.map((d) => d.getAttribute('aria-label'))).toEqual([
        'lunes 9 de noviembre: sin tareas',
        'martes 10 de noviembre: sin tareas',
        'miércoles 11 de noviembre: 2 tareas',
        'jueves 12 de noviembre: sin tareas',
        'viernes 13 de noviembre, hoy: 1 tarea',
        'sábado 14 de noviembre: sin tareas',
        'domingo 15 de noviembre: sin tareas',
      ]);
      // Son botones sin pulsar: ninguno filtra todavía
      expect(dias.map((d) => d.getAttribute('aria-pressed'))).toEqual(Array(7).fill('false'));
      expect(elemento().querySelector('aside ol [role="img"]')).toBeNull();
      expect(dias.map((d) => d.hasAttribute('data-hoy'))).toEqual([
        false,
        false,
        false,
        false,
        true,
        false,
        false,
      ]);
      const conMarca = dias.map((d) =>
        d.querySelector('[data-marca]')!.classList.contains('bg-enlace'),
      );
      expect(conMarca).toEqual([false, false, true, false, true, false, false]);
    });

    it('«Candidaturas activas» lleva al tablero, con la cifra a la vista', async () => {
      grande = true;
      await crear({ ...COMPLETO, resumen: RESUMEN });

      const enlace = elemento().querySelector<HTMLAnchorElement>('aside dl a')!;
      expect(enlace.getAttribute('href')).toBe('/tablero');
      expect(enlace.textContent?.trim()).toBe('12');
      expect(enlace.getAttribute('aria-label')).toBe('Candidaturas activas: 12. Ir al tablero');
      // La flecha que dice que se puede pulsar es un adorno
      expect(enlace.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('la tasa de respuesta lleva una barra decorativa con su porcentaje', async () => {
      grande = true;
      await crear({ ...COMPLETO, resumen: RESUMEN });

      const barra = elemento().querySelector<HTMLElement>('aside [data-barra]')!;
      expect(barra.getAttribute('aria-hidden')).toBe('true');
      expect(barra.querySelector<HTMLElement>('span')!.style.width).toBe('25%');
    });

    it('sin enviadas no hay barra', async () => {
      grande = true;
      await crear({ ...COMPLETO, resumen: crearResumenHoy() });

      expect(elemento().querySelector('aside [data-barra]')).toBeNull();
    });

    it('sin nada pendiente no sale: manda el estado vacío', async () => {
      grande = true;
      await crear(crearPanel({ resumen: RESUMEN }));

      expect(elemento().querySelector('aside')).toBeNull();
      expect(texto()).toContain('Nada pendiente para hoy.');
    });
  });

  describe('detalle en el panel (escritorio)', () => {
    const CON_NOTAS = { ...SEGUIMIENTO, notas: 'Preguntar por el proceso' };
    const PANEL = { ...COMPLETO, paraHoy: [VENCIDO, CON_NOTAS] };
    const FICHA = crearDetalle({
      id: 7,
      empresa: 'Consultora Ejemplo',
      puesto: 'Desarrollador .NET',
      estado: 'Enviada',
      enlace: 'https://ejemplo.test/oferta/1',
      notas: [
        {
          id: 31,
          candidaturaId: 7,
          texto: 'Me escribió la reclutadora',
          fecha: '2026-11-10T10:00:00Z',
          origen: 'Manual',
        },
      ],
      historial: [
        {
          id: 1,
          de: null,
          a: 'Detectada',
          fecha: '2026-11-02T08:00:00Z',
          comentario: null,
          origen: 'Manual',
        },
        {
          id: 2,
          de: 'Detectada',
          a: 'Enviada',
          fecha: '2026-11-06T08:00:00Z',
          comentario: 'Por InfoJobs',
          origen: 'Manual',
        },
      ],
    });

    function panelLateral(): HTMLElement {
      return elemento().querySelector('aside')!;
    }

    function textoPanel(): string {
      return panelLateral().textContent!.replace(/\s+/g, ' ');
    }

    function tituloDe(titulo: string): HTMLButtonElement {
      return boton(`Ver detalle: ${titulo}`);
    }

    /** El recordatorio elegido según la URL («?ver=2»), o null si no lo dice */
    function ver(): string | null {
      return parametro('ver');
    }

    async function responderFicha(): Promise<void> {
      http.expectOne('/api/candidaturas/7').flush(FICHA);
      await fixture.whenStable();
    }

    async function pulsarEscape(destino: Element = document.body): Promise<KeyboardEvent> {
      const evento = new KeyboardEvent('keydown', {
        key: 'Escape',
        bubbles: true,
        cancelable: true,
      });
      destino.dispatchEvent(evento);
      await fixture.whenStable();
      return evento;
    }

    beforeEach(() => {
      grande = true;
    });

    it('el título es un botón que no está pulsado, y no un enlace a la ficha', async () => {
      await crear(PANEL);

      const titulo = tituloDe(CON_NOTAS.titulo);
      // En la tarjeta, solo el puesto; el nombre del botón sigue llevando el título entero
      expect(titulo.textContent?.trim()).toBe('Desarrollador .NET');
      expect(titulo.getAttribute('aria-pressed')).toBe('false');
      expect(elemento().querySelector('a[href="/candidaturas/7"]')).toBeNull();
      // También lo suelto se puede elegir
      expect(tituloDe(VENCIDO.titulo)).not.toBeNull();
    });

    it('en el móvil el título sigue siendo el enlace y no se elige nada', async () => {
      grande = false;
      await crear(PANEL);

      expect(elemento().querySelector('a[href="/candidaturas/7"]')).not.toBeNull();
      // Los chips y los días del calendario también se pulsan: se mira solo dentro de las tarjetas
      expect(elemento().querySelector('article button[aria-pressed]')).toBeNull();

      elemento().querySelector<HTMLElement>('article')!.click();
      await fixture.whenStable();
      expect(elemento().querySelector('app-detalle-recordatorio')).toBeNull();
      expect(ver()).toBeNull();
    });

    it('al pulsar el título abre el detalle, marca la tarjeta y lleva el foco al encabezado', async () => {
      await crear(PANEL);

      tituloDe(CON_NOTAS.titulo).click();
      await fixture.whenStable();

      const encabezado = panelLateral().querySelector('h2')!;
      expect(encabezado.textContent).toContain('Seguimiento: Desarrollador .NET');
      expect(document.activeElement).toBe(encabezado);
      expect(panelLateral().getAttribute('aria-labelledby')).toBe(encabezado.id);
      expect(tituloDe(CON_NOTAS.titulo).getAttribute('aria-pressed')).toBe('true');
      expect(tituloDe(VENCIDO.titulo).getAttribute('aria-pressed')).toBe('false');
      const marcadas = [...elemento().querySelectorAll('article[data-seleccionada]')];
      expect(marcadas.map((a) => a.getAttribute('aria-label'))).toEqual([
        `Seguimiento: ${CON_NOTAS.titulo}`,
      ]);
      expect(ver()).toBe('2');
      // El resumen deja su sitio al detalle
      expect(panelLateral().querySelector('app-resumen-hoy')).toBeNull();
      await responderFicha();
    });

    it('de una candidatura enseña sus datos, las notas, la oferta, el historial y la ficha', async () => {
      await crear(PANEL);
      tituloDe(CON_NOTAS.titulo).click();
      await fixture.whenStable();

      // Mientras llega la ficha: lo que ya se sabe y la barra de carga
      expect(textoPanel()).toContain('Consultora Ejemplo');
      expect(textoPanel()).toContain('Desarrollador .NET');
      expect(panelLateral().querySelector('[data-estado="Enviada"]')!.textContent).toContain(
        'Enviada',
      );
      expect(textoPanel()).toContain('Preguntar por el proceso');
      expect(panelLateral().querySelector('mat-progress-bar')).not.toBeNull();

      await responderFicha();

      expect(panelLateral().querySelector('mat-progress-bar')).toBeNull();
      expect(textoPanel()).toContain('Me escribió la reclutadora');
      const oferta = panelLateral().querySelector<HTMLAnchorElement>('a[target="_blank"]')!;
      expect(oferta.getAttribute('href')).toBe('https://ejemplo.test/oferta/1');
      expect(oferta.getAttribute('rel')).toBe('noopener noreferrer');
      expect(oferta.textContent!.replace(/\s+/g, ' ')).toContain(
        'Abrir la oferta (se abre en otra pestaña)',
      );
      // El historial, de lo más reciente a lo más antiguo
      const historial = [...panelLateral().querySelectorAll('ol li')].map((li) =>
        li.textContent!.replace(/\s+/g, ' ').trim(),
      );
      expect(historial).toEqual([
        'Detectada → Enviada 6 nov 2026, 09:00 Por InfoJobs',
        'Alta en Detectada 2 nov 2026, 09:00',
      ]);
      const ficha = [...panelLateral().querySelectorAll('a')].find((a) =>
        a.textContent!.includes('Abrir ficha completa'),
      )!;
      expect(ficha.getAttribute('href')).toBe('/candidaturas/7');
      // El único relleno de la pantalla sigue siendo el primer «Hecho»
      expect(rellenos()).toHaveLength(1);
    });

    it('si la ficha no carga lo anuncia dentro del panel, y «Reintentar» la vuelve a pedir', async () => {
      await crear(PANEL);
      tituloDe(CON_NOTAS.titulo).click();
      await fixture.whenStable();
      http.expectOne('/api/candidaturas/7').flush(null, { status: 503, statusText: 'x' });
      await fixture.whenStable();

      const alerta = panelLateral().querySelector('[role="alert"]')!;
      expect(alerta.textContent).toContain('No se puede conectar con RadarEmpleo');
      // Lo que no depende de la ficha sigue ahí
      expect(textoPanel()).toContain('Abrir ficha completa');

      [...alerta.querySelectorAll('button')]
        .find((b) => b.textContent!.includes('Reintentar'))!
        .click();
      await fixture.whenStable();
      expect(document.activeElement).toBe(panelLateral().querySelector('h2'));
      await responderFicha();

      expect(panelLateral().querySelector('[role="alert"]')).toBeNull();
      expect(textoPanel()).toContain('Me escribió la reclutadora');
    });

    it('de uno suelto enseña el título, el tipo, la fecha y las notas, y deja editarlo', async () => {
      const suelto = { ...VENCIDO, notas: 'Con la captura del panel' };
      await crear({ ...COMPLETO, paraHoy: [suelto, SEGUIMIENTO] });

      tituloDe(suelto.titulo).click();
      await fixture.whenStable();

      expect(panelLateral().querySelector('h2')!.textContent).toContain('post de Supabase');
      expect(panelLateral().querySelector('.insignia')!.textContent).toContain(
        'Publicación en LinkedIn',
      );
      expect(panelLateral().querySelector('time')!.textContent).toContain('3 nov 2026, 09:00');
      expect(textoPanel()).toContain('Con la captura del panel');
      expect(textoPanel()).not.toContain('Abrir ficha completa');

      [...panelLateral().querySelectorAll('button')]
        .find((b) => b.textContent!.trim() === 'Editar')!
        .click();
      expect(abrirDialogo.mock.calls[0][0]).toBe(DialogoRecordatorio);
      expect(abrirDialogo.mock.calls[0][1].data).toEqual({
        recordatorio: suelto,
        candidatura: null,
      });
      // Sin candidatura no se pide ninguna ficha: afterEach lo comprueba
    });

    it('un clic en la tarjeta, fuera de sus botones, también la elige', async () => {
      await crear(PANEL);
      const tarjeta = elemento().querySelector<HTMLElement>(
        `article[aria-label="Publicación en LinkedIn: ${VENCIDO.titulo}"]`,
      )!;

      tarjeta.querySelector('time')!.click();
      await fixture.whenStable();

      expect(panelLateral().querySelector('h2')!.textContent).toContain('post de Supabase');
      expect(tarjeta.hasAttribute('data-seleccionada')).toBe(true);
    });

    it('un clic en «⋮» abre su menú y no elige la tarjeta', async () => {
      await crear(PANEL);

      boton('Más acciones: Publicar en LinkedIn: post de Supabase').click();
      await fixture.whenStable();

      expect(opcionesDelMenu().length).toBeGreaterThan(0);
      expect(elemento().querySelector('app-detalle-recordatorio')).toBeNull();
    });

    it('«Volver al resumen» lo cierra y devuelve el foco a la tarjeta', async () => {
      await crear(PANEL);
      tituloDe(VENCIDO.titulo).click();
      await fixture.whenStable();

      botonPorTexto('Volver al resumen').click();
      await fixture.whenStable();

      expect(panelLateral().querySelector('h2')!.textContent).toContain('Resumen');
      expect(document.activeElement).toBe(tituloDe(VENCIDO.titulo));
      expect(tituloDe(VENCIDO.titulo).getAttribute('aria-pressed')).toBe('false');
      expect(ver()).toBeNull();
    });

    it('Esc también vuelve al resumen', async () => {
      await crear(PANEL);
      tituloDe(VENCIDO.titulo).click();
      await fixture.whenStable();

      await pulsarEscape();

      expect(elemento().querySelector('app-detalle-recordatorio')).toBeNull();
      expect(document.activeElement).toBe(tituloDe(VENCIDO.titulo));
    });

    it('con un menú o un diálogo abiertos, Esc es para cerrarlos', async () => {
      await crear(PANEL);
      tituloDe(VENCIDO.titulo).click();
      await fixture.whenStable();
      const fondo = document.createElement('div');
      fondo.className = 'cdk-overlay-backdrop';
      document.body.append(fondo);

      await pulsarEscape();
      fondo.remove();

      expect(elemento().querySelector('app-detalle-recordatorio')).not.toBeNull();
    });

    it('pulsar otra vez el título de la elegida vuelve al resumen', async () => {
      await crear(PANEL);
      tituloDe(VENCIDO.titulo).click();
      await fixture.whenStable();

      tituloDe(VENCIDO.titulo).click();
      await fixture.whenStable();

      expect(elemento().querySelector('app-detalle-recordatorio')).toBeNull();
      expect(ver()).toBeNull();
    });

    it('al pasar de una a otra cambia el detalle y el foco', async () => {
      await crear(PANEL);
      tituloDe(VENCIDO.titulo).click();
      await fixture.whenStable();

      tituloDe(CON_NOTAS.titulo).click();
      await fixture.whenStable();

      const encabezado = panelLateral().querySelector('h2')!;
      expect(encabezado.textContent).toContain('Seguimiento: Desarrollador .NET');
      expect(document.activeElement).toBe(encabezado);
      expect(ver()).toBe('2');
      await responderFicha();
    });

    it('con «?ver=» en la URL, al entrar ya está abierto', async () => {
      urlInicial = '/?ver=1';
      await crear(PANEL);

      expect(panelLateral().querySelector('h2')!.textContent).toContain('post de Supabase');
      expect(tituloDe(VENCIDO.titulo).getAttribute('aria-pressed')).toBe('true');
    });

    it('si «?ver=» apunta a uno de una sección plegada, la abre, y al cerrar el foco vuelve a su tarjeta', async () => {
      urlInicial = `/?ver=${DICIEMBRE.id}`;
      await crear(PANEL);

      const seccion = tituloDe(DICIEMBRE.titulo).closest('details')!;
      expect(seccion.open).toBe(true);
      expect(seccion.querySelector('summary')!.id).toBe('titulo-masAdelante');

      await pulsarEscape();

      expect(seccion.open).toBe(true);
      expect(document.activeElement).toBe(tituloDe(DICIEMBRE.titulo));
    });

    it('los títulos de las secciones plegadas son encabezados de nivel 2, ocultos junto al summary', async () => {
      await crear(PANEL);

      const plegables = [...elemento().querySelectorAll('app-seccion-plegable')];
      expect(plegables.length).toBeGreaterThan(0);
      plegables.forEach((plegable) => {
        const encabezado = plegable.querySelector('h2.sr-only')!;
        expect(encabezado.textContent!.trim()).toBe(
          plegable.querySelector('summary')!.textContent!.trim(),
        );
      });
      expect(elemento().querySelector('summary [role="heading"]')).toBeNull();
    });

    it('si «?ver=» apunta a uno que no está, sale el resumen y se limpia la URL', async () => {
      urlInicial = '/?ver=999';
      await crear(PANEL);

      expect(panelLateral().querySelector('h2')!.textContent).toContain('Resumen');
      expect(ver()).toBeNull();
    });

    it('en el móvil «?ver=» no abre nada', async () => {
      grande = false;
      urlInicial = '/?ver=1';
      await crear(PANEL);

      expect(elemento().querySelector('app-detalle-recordatorio')).toBeNull();
    });

    describe('acciones desde el panel', () => {
      function botonDelPanel(marca: string): HTMLButtonElement | null {
        return panelLateral().querySelector<HTMLButtonElement>(`button[${marca}]`);
      }

      it('«Hecho» lo marca sin salir del panel, con su «Deshacer», y vuelve el resumen', async () => {
        await crear(PANEL);
        tituloDe(VENCIDO.titulo).click();
        await fixture.whenStable();

        const hecho = botonDelPanel('data-hecho')!;
        // Con borde: el relleno de la pantalla sigue siendo «Nuevo recordatorio»
        expect(hecho.hasAttribute('mat-stroked-button')).toBe(true);
        hecho.click();

        const peticion = http.expectOne('/api/recordatorios/1/hecho');
        expect(peticion.request.body).toEqual({ hecho: true });
        peticion.flush({ ...crearRecordatorio(VENCIDO), hecho: true });
        await responderPanel({
          ...PANEL,
          paraHoy: [CON_NOTAS],
          hechosHoy: [{ ...VENCIDO, hecho: true }, HECHO],
        });

        expect(avisos.deshacible).toHaveBeenCalledWith(
          'Hecho: «Publicar en LinkedIn: post de Supabase».',
          expect.any(Function),
        );
        expect(panelLateral().querySelector('h2')!.textContent).toContain('Resumen');
        expect(ver()).toBeNull();
        // El foco no se pierde: pasa al «Hecho» de la tarjeta vecina
        expect(document.activeElement).toBe(
          boton('Hecho: Seguimiento: Desarrollador .NET en Consultora Ejemplo'),
        );

        deshacerDelAviso()();
        const vuelta = http.expectOne('/api/recordatorios/1/hecho');
        expect(vuelta.request.body).toEqual({ hecho: false });
        vuelta.flush(VENCIDO);
        await responderPanel(PANEL);
      });

      it('si se deshace ese «Hecho», vuelve a su detalle en cuanto llega el panel', async () => {
        await crear(PANEL);
        tituloDe(VENCIDO.titulo).click();
        await fixture.whenStable();
        botonDelPanel('data-hecho')!.click();
        http.expectOne('/api/recordatorios/1/hecho').flush({ ...VENCIDO, hecho: true });
        await responderPanel({
          ...PANEL,
          paraHoy: [CON_NOTAS],
          hechosHoy: [{ ...VENCIDO, hecho: true }, HECHO],
        });
        expect(ver()).toBeNull();

        deshacerDelAviso()();
        http.expectOne('/api/recordatorios/1/hecho').flush(VENCIDO);
        await fixture.whenStable();
        // Con el panel de antes aún sale como hecho: espera al nuevo
        expect(ver()).toBeNull();
        await responderPanel(PANEL);

        expect(ver()).toBe('1');
        expect(panelLateral().querySelector('h2')!.textContent).toContain('post de Supabase');
        expect(tituloDe(VENCIDO.titulo).getAttribute('aria-pressed')).toBe('true');
      });

      it('deshacer el «Hecho» de otra tarjeta no abre ningún detalle', async () => {
        await crear(PANEL);
        boton('Hecho: Publicar en LinkedIn: post de Supabase').click();
        http.expectOne('/api/recordatorios/1/hecho').flush({ ...VENCIDO, hecho: true });
        await responderPanel({ ...PANEL, paraHoy: [CON_NOTAS] });

        deshacerDelAviso()();
        http.expectOne('/api/recordatorios/1/hecho').flush(VENCIDO);
        await responderPanel(PANEL);

        expect(ver()).toBeNull();
        expect(panelLateral().querySelector('h2')!.textContent).toContain('Resumen');
      });

      it('uno ya hecho no lleva «Hecho» en el panel', async () => {
        await crear(PANEL);
        tituloDe(HECHO.titulo).click();
        await fixture.whenStable();

        expect(botonDelPanel('data-hecho')).toBeNull();
      });

      it('«Añadir recordatorio» apunta otro para la misma candidatura, sin pasar por la ficha', async () => {
        await crear(PANEL);
        tituloDe(CON_NOTAS.titulo).click();
        await fixture.whenStable();

        const anadir = botonDelPanel('data-anadir')!;
        expect(anadir.textContent).toContain('Añadir recordatorio');
        expect(anadir.hasAttribute('mat-stroked-button')).toBe(true);
        anadir.click();

        const [componente, opciones] = abrirDialogo.mock.calls[0];
        expect(componente).toBe(DialogoRecordatorio);
        // La candidatura está en Enviada: lo normal es otro seguimiento
        expect(opciones.data).toEqual({
          tipoInicial: 'Seguimiento',
          candidatura: CON_NOTAS.candidatura,
        });
        await responderFicha();
      });

      it('con la candidatura en Oferta, el nuevo empieza como plazo', async () => {
        const enOferta = {
          ...CON_NOTAS,
          candidatura: { ...CON_NOTAS.candidatura!, estado: 'Oferta' as const },
        };
        await crear({ ...PANEL, paraHoy: [VENCIDO, enOferta] });
        tituloDe(CON_NOTAS.titulo).click();
        await fixture.whenStable();

        botonDelPanel('data-anadir')!.click();

        expect(abrirDialogo.mock.calls[0][1].data.tipoInicial).toBe('Plazo');
        await responderFicha();
      });

      it('uno suelto no ofrece «Añadir recordatorio»: para eso está el botón de arriba', async () => {
        await crear(PANEL);
        tituloDe(VENCIDO.titulo).click();
        await fixture.whenStable();

        expect(botonDelPanel('data-anadir')).toBeNull();
      });
    });

    it('al marcar como hecho el elegido, vuelve el resumen', async () => {
      await crear(PANEL);
      tituloDe(VENCIDO.titulo).click();
      await fixture.whenStable();

      boton('Hecho: Publicar en LinkedIn: post de Supabase').click();
      http.expectOne('/api/recordatorios/1/hecho').flush({ ...VENCIDO, hecho: true });
      await responderPanel({
        ...PANEL,
        paraHoy: [CON_NOTAS],
        hechosHoy: [{ ...VENCIDO, hecho: true }, HECHO],
      });

      expect(panelLateral().querySelector('h2')!.textContent).toContain('Resumen');
      expect(ver()).toBeNull();
      // El foco sigue yendo al «Hecho» del vecino
      expect(document.activeElement).toBe(
        boton('Hecho: Seguimiento: Desarrollador .NET en Consultora Ejemplo'),
      );
    });

    it('si el elegido desaparece del panel, vuelve el resumen sin errores', async () => {
      await crear(PANEL);
      tituloDe(VENCIDO.titulo).click();
      await fixture.whenStable();
      expect(document.activeElement).toBe(panelLateral().querySelector('h2'));

      // Otra pestaña lo ha borrado y, al cambiar el día, el panel llega sin él
      ahora.set(new Date('2026-11-13T23:30:00Z'));
      await fixture.whenStable();
      await responderPanel({ ...PANEL, hoy: '2026-11-14', paraHoy: [CON_NOTAS] });

      expect(panelLateral().querySelector('h2')!.textContent).toContain('Resumen');
      expect(ver()).toBeNull();
      // El foco estaba en el detalle, que ya no existe: no se queda en <body>
      expect(document.activeElement).toBe(elemento().querySelector('section[tabindex="-1"]'));
    });

    it('si el elegido sigue en el panel con otros datos, el detalle se pone al día', async () => {
      await crear(PANEL);
      tituloDe(VENCIDO.titulo).click();
      await fixture.whenStable();

      ahora.set(new Date('2026-11-13T23:30:00Z'));
      await fixture.whenStable();
      await responderPanel({
        ...PANEL,
        hoy: '2026-11-14',
        paraHoy: [{ ...VENCIDO, titulo: 'Publicar en LinkedIn: post de .NET' }, CON_NOTAS],
      });

      expect(panelLateral().querySelector('h2')!.textContent).toContain('post de .NET');
    });
  });

  describe('«Próximos 7 días», agrupados por día', () => {
    const POST_17_TARDE = crearRecordatorioAgenda({
      id: 8,
      titulo: 'Publicar en LinkedIn: post de .NET',
      fechaHora: '2026-11-17T16:00:00Z',
    });
    const ENTREVISTA_18 = crearRecordatorioAgenda({
      id: 6,
      candidaturaId: 7,
      tipo: 'Entrevista',
      titulo: 'Entrevista en Consultora Ejemplo',
      fechaHora: '2026-11-18T15:00:00Z',
      candidatura: SEGUIMIENTO.candidatura,
    });
    const PANEL = { ...COMPLETO, proximos: [POST_17, POST_17_TARDE, ENTREVISTA_18] };

    function seccion(id: string): HTMLElement {
      return elemento().querySelector<HTMLElement>(`section[aria-labelledby="${id}"]`)!;
    }

    function encabezados(): string[] {
      return [...seccion('titulo-proximos').querySelectorAll('h3[data-dia]')].map((h) =>
        h.textContent!.replace(/\s+/g, ' ').trim(),
      );
    }

    it('cada día lleva su encabezado con el total de tareas, en singular si es una', async () => {
      await crear(PANEL);

      expect(encabezados()).toEqual(['Martes, 17 nov · 2 tareas', 'Miércoles, 18 nov · 1 tarea']);
      expect(titulos()).toContain('Próximos 7 días · 3');
    });

    it('bajo cada encabezado van sus tarjetas, que dicen solo la hora', async () => {
      await crear(PANEL);

      const grupos = [...seccion('titulo-proximos').querySelectorAll('h3[data-dia]')].map((h) =>
        [...h.parentElement!.querySelectorAll('time')].map((t) => t.textContent!.trim()),
      );
      expect(grupos).toEqual([['09:00', '17:00'], ['16:00']]);
    });

    it('«Para hoy» no se agrupa: lo vencido sigue con su día y lo de hoy dice «Hoy»', async () => {
      await crear(PANEL);

      expect(seccion('titulo-paraHoy').querySelector('h3')).toBeNull();
      expect(seccion('titulo-paraHoy').textContent).toContain('Vencido · mar 3 nov');
      const horas = [...seccion('titulo-paraHoy').querySelectorAll('time')].map((t) =>
        t.textContent!.replace(/\s+/g, ' ').trim(),
      );
      expect(horas).toEqual(['09:00', 'Hoy · 09:00']);
    });

    it('el aviso de «Todo despejado» cuenta en «tareas» solo las de ese día', async () => {
      await crear({ ...PANEL, paraHoy: [] });

      // Dos el martes 17 y una el miércoles 18: el número es el del martes
      expect(texto()).toContain('Lo próximo es el mar 17 nov (2 tareas).');
    });

    it('con una sola ese día va en singular, aunque luego vengan más', async () => {
      await crear({ ...PANEL, paraHoy: [], proximos: [POST_17, ENTREVISTA_18] });

      expect(texto()).toContain('Lo próximo es el mar 17 nov (1 tarea).');
    });
  });

  describe('filtros', () => {
    const POST_17_TARDE = crearRecordatorioAgenda({
      id: 8,
      titulo: 'Publicar en LinkedIn: post de .NET',
      fechaHora: '2026-11-17T16:00:00Z',
    });
    const ENTREVISTA_18 = crearRecordatorioAgenda({
      id: 6,
      candidaturaId: 7,
      tipo: 'Entrevista',
      titulo: 'Entrevista en Consultora Ejemplo',
      fechaHora: '2026-11-18T15:00:00Z',
      candidatura: SEGUIMIENTO.candidatura,
    });
    // Pendientes: 3 publicaciones (3, 17 y 17 de noviembre), 1 seguimiento (hoy, 13), 1 entrevista
    // (18) y 1 plazo (1 de diciembre). Hecho hoy: un «Otro»
    const PANEL = { ...COMPLETO, proximos: [POST_17, POST_17_TARDE, ENTREVISTA_18] };

    function grupoChips(): HTMLElement | null {
      return elemento().querySelector<HTMLElement>('[data-filtros]');
    }

    function chips(): HTMLButtonElement[] {
      return [...(grupoChips()?.querySelectorAll('button') ?? [])];
    }

    /** «LinkedIn 3»: el texto del chip y su número, sin lo que solo oye el lector de pantalla */
    function textosChips(): string[] {
      return chips().map(
        (c) =>
          `${c.childNodes[0].textContent!.trim()} ${c.querySelector('[aria-hidden]')!.textContent}`,
      );
    }

    function chip(nombre: string): HTMLButtonElement {
      return chips().find((c) => c.childNodes[0].textContent!.trim() === nombre)!;
    }

    function pulsados(): string[] {
      return chips()
        .filter((c) => c.getAttribute('aria-pressed') === 'true')
        .map((c) => c.childNodes[0].textContent!.trim());
    }

    function dia(numero: string): HTMLButtonElement {
      return [...elemento().querySelectorAll<HTMLButtonElement>('app-resumen-hoy ol button')].find(
        (b) => b.textContent!.includes(numero),
      )!;
    }

    function aviso(): string | null {
      const estado = elemento().querySelector('[data-aviso-dia]');
      return estado ? estado.textContent!.replace(/\s+/g, ' ').trim() : null;
    }

    function tarjetas(): string[] {
      return [...elemento().querySelectorAll('article')].map((a) => a.getAttribute('aria-label')!);
    }

    describe('por tipo', () => {
      it('con un tipo elegido no salen las enviadas sin respuesta: no son recordatorios', async () => {
        await crear(PANEL);
        expect(titulos().some((t) => t.startsWith('Enviadas sin respuesta'))).toBe(true);

        chip('LinkedIn').click();
        await fixture.whenStable();

        expect(titulos().some((t) => t.startsWith('Enviadas sin respuesta'))).toBe(false);
      });

      it('hay un chip por cada tipo con algo pendiente, más «Todas», cada uno con su número', async () => {
        await crear(PANEL);

        // Sin «Otros»: lo único de ese tipo ya está hecho
        expect(textosChips()).toEqual([
          'Todas 6',
          'Seguimientos 1',
          'Entrevistas 1',
          'Plazos 1',
          'LinkedIn 3',
        ]);
        expect(pulsados()).toEqual(['Todas']);
      });

      it('es un grupo con nombre, y cada chip dice su número en «tareas» al lector de pantalla', async () => {
        await crear(PANEL);

        expect(grupoChips()!.getAttribute('role')).toBe('group');
        expect(grupoChips()!.getAttribute('aria-label')).toBe('Filtrar las tareas por tipo');
        expect(chips().every((c) => c.getAttribute('type') === 'button')).toBe(true);
        expect(chip('LinkedIn').querySelector('.sr-only')!.textContent).toContain('3 tareas');
        expect(chip('Plazos').querySelector('.sr-only')!.textContent).toContain('1 tarea');
      });

      it('sin nada pendiente no hay chips', async () => {
        await crear(crearPanel({ hechosHoy: [HECHO] }));

        expect(grupoChips()).toBeNull();
      });

      it('un chip filtra las tres listas de pendientes, y las que se quedan vacías no se pintan', async () => {
        await crear(PANEL);

        chip('LinkedIn').click();
        await fixture.whenStable();

        expect(pulsados()).toEqual(['LinkedIn']);
        expect(titulos()).toEqual([
          'Resumen',
          'Para hoy · 1',
          'Próximos 7 días · 2',
          // Las enviadas sin respuesta no son recordatorios de ningún tipo: con el filtro no salen
        ]);
        expect(tarjetas()).toEqual([
          'Publicación en LinkedIn: Publicar en LinkedIn: post de Supabase',
          'Publicación en LinkedIn: Publicar en LinkedIn: post de Angular',
          'Publicación en LinkedIn: Publicar en LinkedIn: post de .NET',
        ]);
        expect(texto()).not.toContain('Todo despejado');
      });

      it('lo de «Más adelante» que pasa el filtro se enseña abierto', async () => {
        await crear(PANEL);

        chip('Plazos').click();
        await fixture.whenStable();

        expect(titulos()).toEqual(['Resumen', 'Más adelante · 1']);
        const masAdelante = elemento().querySelector('#titulo-masAdelante')!.closest('details')!;
        expect(masAdelante.open).toBe(true);
      });

      it('el filtro va en la URL, y «Todas» o el mismo chip lo quitan', async () => {
        await crear(PANEL);

        chip('Entrevistas').click();
        await fixture.whenStable();
        expect(parametro('tipo')).toBe('Entrevista');

        chip('Todas').click();
        await fixture.whenStable();
        expect(parametro('tipo')).toBeNull();
        expect(pulsados()).toEqual(['Todas']);
        expect(titulos()).toContain('Para hoy · 2');

        chip('Entrevistas').click();
        await fixture.whenStable();
        chip('Entrevistas').click();
        await fixture.whenStable();
        expect(parametro('tipo')).toBeNull();
        expect(pulsados()).toEqual(['Todas']);
      });

      it('al entrar con «?tipo=» ya sale filtrado, sin mirar mayúsculas', async () => {
        urlInicial = '/?tipo=entrevista';
        await crear(PANEL);

        expect(pulsados()).toEqual(['Entrevistas']);
        expect(tarjetas()).toEqual([
          'Entrevista: Entrevista en Consultora Ejemplo, Desarrollador .NET',
        ]);
      });

      it('un tipo de la URL del que no hay nada se quita solo', async () => {
        urlInicial = '/?tipo=Otro';
        await crear(PANEL);

        expect(pulsados()).toEqual(['Todas']);
        expect(parametro('tipo')).toBeNull();
        expect(titulos()).toContain('Para hoy · 2');
      });

      it('al hacer lo último de un tipo, el filtro vuelve a «Todas»', async () => {
        await crear(PANEL);
        chip('Seguimientos').click();
        await fixture.whenStable();

        boton('Hecho: Seguimiento: Desarrollador .NET en Consultora Ejemplo').click();
        http.expectOne('/api/recordatorios/2/hecho').flush({ ...SEGUIMIENTO, hecho: true });
        await responderPanel({ ...PANEL, paraHoy: [VENCIDO] });

        expect(textosChips()).not.toContain('Seguimientos 0');
        expect(pulsados()).toEqual(['Todas']);
        expect(parametro('tipo')).toBeNull();
        expect(titulos()).toContain('Para hoy · 1');
      });

      it('con el panel al lado, filtrar fuera la tarjeta elegida cierra su detalle', async () => {
        grande = true;
        await crear(PANEL);
        boton('Ver detalle: Publicar en LinkedIn: post de Supabase').click();
        await fixture.whenStable();
        expect(elemento().querySelector('app-detalle-recordatorio')).not.toBeNull();

        chip('Plazos').click();
        await fixture.whenStable();

        expect(elemento().querySelector('app-detalle-recordatorio')).toBeNull();
        expect(parametro('ver')).toBeNull();
      });
    });

    describe('por día, desde el calendario', () => {
      it('pulsar un día filtra la lista a ese día, lo avisa y lo apunta en la URL', async () => {
        await crear(PANEL);

        dia('13').click();
        await fixture.whenStable();

        expect(dia('13').getAttribute('aria-pressed')).toBe('true');
        expect(dia('12').getAttribute('aria-pressed')).toBe('false');
        expect(aviso()).toContain('Mostrando el vie 13 nov');
        expect(aviso()).toContain('Quitar filtro');
        expect(parametro('dia')).toBe('2026-11-13');
        // Lo vencido es de otro día, y las enviadas sin respuesta no son de ninguno. Lo hecho hoy que
        // era de ese día sí sale
        expect(titulos()).toEqual(['Resumen', 'Para hoy · 1', 'Hechos hoy · 1']);
        expect(tarjetas()).toEqual([
          'Seguimiento: Seguimiento: Desarrollador .NET en Consultora Ejemplo',
          'Otro: Llamar a la gestoría',
        ]);
      });

      it('el aviso se anuncia en un role="status" que ya estaba, con el día entero', async () => {
        await crear(PANEL);
        const estado = elemento().querySelector('[data-anuncio-filtro][role="status"]')!;
        expect(estado.textContent!.trim()).toBe('');

        dia('13').click();
        await fixture.whenStable();

        expect(elemento().querySelector('[data-anuncio-filtro][role="status"]')).toBe(estado);
        expect(estado.textContent!.trim()).toBe('Mostrando el viernes 13 de noviembre');
        // Lo que se ve, con el día corto, no se lee dos veces
        const visible = elemento().querySelector('[data-aviso-dia] [aria-hidden="true"]')!;
        expect(visible.textContent!.replace(/\s+/g, ' ').trim()).toBe('Mostrando el vie 13 nov');

        botonPorTexto('Quitar filtro').click();
        await fixture.whenStable();
        expect(estado.textContent!.trim()).toBe('');
      });

      it('los números de los chips pasan a ser los de ese día', async () => {
        await crear(PANEL);

        dia('13').click();
        await fixture.whenStable();

        expect(textosChips()).toEqual([
          'Todas 1',
          'Seguimientos 1',
          'Entrevistas 0',
          'Plazos 0',
          'LinkedIn 0',
        ]);
      });

      it('otro clic en el mismo día quita el filtro', async () => {
        await crear(PANEL);
        dia('13').click();
        await fixture.whenStable();

        dia('13').click();
        await fixture.whenStable();

        expect(dia('13').getAttribute('aria-pressed')).toBe('false');
        expect(aviso()).toBeNull();
        expect(parametro('dia')).toBeNull();
        expect(titulos()).toContain('Para hoy · 2');
        expect(titulos()).toContain('Enviadas sin respuesta · 10');
      });

      it('«Quitar filtro» lo quita y deja el foco en los chips', async () => {
        await crear(PANEL);
        dia('13').click();
        await fixture.whenStable();

        elemento().querySelector<HTMLButtonElement>('[data-quitar-dia]')!.click();
        await fixture.whenStable();

        expect(aviso()).toBeNull();
        expect(parametro('dia')).toBeNull();
        expect(document.activeElement).toBe(chip('Todas'));
      });

      it('un día sin nada enseña «Nada para ese día» con su «Quitar filtro»', async () => {
        await crear(PANEL);

        dia('14').click();
        await fixture.whenStable();

        expect(titulos()).toEqual(['Resumen']);
        const vacio = elemento().querySelector('[data-nada-filtrado]')!;
        expect(vacio.textContent).toContain('Nada para ese día.');
        expect(texto()).not.toContain('Todo despejado');
        expect(texto()).not.toContain('café tranquilo');

        vacio.querySelector('button')!.click();
        await fixture.whenStable();
        expect(elemento().querySelector('[data-nada-filtrado]')).toBeNull();
        expect(titulos()).toContain('Para hoy · 2');
      });

      it('se suma al filtro de tipo', async () => {
        urlInicial = '/?dia=2026-11-17';
        await crear(PANEL);
        expect(titulos()).toEqual(['Resumen', 'Próximos 7 días · 2']);
        expect(aviso()).toContain('Mostrando el mar 17 nov');

        chip('Entrevistas').click();
        await fixture.whenStable();

        // El 17 no hay entrevistas: se dice de qué no hay, y los dos filtros siguen en la URL
        expect(elemento().querySelector('[data-nada-filtrado]')!.textContent).toContain(
          'Nada de «Entrevistas» para ese día.',
        );
        expect(parametro('tipo')).toBe('Entrevista');
        expect(parametro('dia')).toBe('2026-11-17');

        chip('LinkedIn').click();
        await fixture.whenStable();
        expect(titulos()).toEqual(['Resumen', 'Próximos 7 días · 2']);
      });

      it('al entrar con «?dia=» de más adelante, esa sección sale abierta', async () => {
        urlInicial = '/?dia=2026-12-01';
        await crear(PANEL);

        expect(titulos()).toEqual(['Resumen', 'Más adelante · 1']);
        expect(elemento().querySelector('#titulo-masAdelante')!.closest('details')!.open).toBe(
          true,
        );
      });

      it('un «?dia=» que no es un día se ignora', async () => {
        urlInicial = '/?dia=mañana';
        await crear(PANEL);

        expect(aviso()).toBeNull();
        expect(titulos()).toContain('Para hoy · 2');
      });

      describe('«Lo siguiente»', () => {
        function siguiente(): HTMLButtonElement | null {
          return elemento().querySelector<HTMLButtonElement>('[data-siguiente]');
        }

        it('si esta semana ya no queda nada, dice el día de lo próximo y lo filtra al pulsarlo', async () => {
          await crear({ ...PANEL, resumen: crearResumenHoy({ siguienteDia: '2026-11-17' }) });

          expect(siguiente()!.textContent!.replace(/\s+/g, ' ').trim()).toBe(
            'Lo siguiente: mar 17 nov →martes 17 de noviembre',
          );
          expect(siguiente()!.querySelector('.sr-only')!.textContent).toBe(
            'martes 17 de noviembre',
          );
          siguiente()!.click();
          await fixture.whenStable();

          expect(parametro('dia')).toBe('2026-11-17');
          expect(siguiente()!.getAttribute('aria-pressed')).toBe('true');
          expect(titulos()).toEqual(['Resumen', 'Próximos 7 días · 2']);
        });

        it('si esta semana aún queda algo, no sale', async () => {
          const semana = crearResumenHoy().semana.map((d) => ({
            ...d,
            recordatorios: d.dia === '2026-11-14' ? 1 : 0,
          }));
          await crear({
            ...PANEL,
            resumen: crearResumenHoy({ semana, siguienteDia: '2026-11-17' }),
          });

          expect(siguiente()).toBeNull();
        });

        it('sin nada más adelante, tampoco', async () => {
          await crear(PANEL);

          expect(siguiente()).toBeNull();
        });
      });
    });

    describe('«Entrevistas esta semana»', () => {
      function cifraEntrevistas(): HTMLButtonElement | null {
        return elemento().querySelector<HTMLButtonElement>('app-resumen-hoy dl button');
      }

      // Una el sábado 14 (esta semana, que acaba el domingo 15) y otra el miércoles 18 (la que viene)
      const ENTREVISTA_14 = crearRecordatorioAgenda({
        ...ENTREVISTA_18,
        id: 9,
        titulo: 'Entrevista en Otra Empresa',
        fechaHora: '2026-11-14T10:00:00Z',
      });
      const CON_ENTREVISTAS = {
        ...PANEL,
        proximos: [ENTREVISTA_14, POST_17, POST_17_TARDE, ENTREVISTA_18],
        resumen: crearResumenHoy({ entrevistasSemana: 1 }),
      };

      it('filtra las entrevistas de esta semana, las mismas que cuenta, y otro clic lo quita', async () => {
        grande = true;
        await crear(CON_ENTREVISTAS);
        expect(cifraEntrevistas()!.textContent?.trim()).toBe('1');
        expect(cifraEntrevistas()!.getAttribute('aria-label')).toBe(
          'Entrevistas pendientes esta semana: 1. Ver solo esas entrevistas',
        );
        expect(cifraEntrevistas()!.getAttribute('aria-pressed')).toBe('false');

        cifraEntrevistas()!.click();
        await fixture.whenStable();

        expect(pulsados()).toEqual(['Entrevistas']);
        expect(parametro('tipo')).toBe('Entrevista');
        expect(parametro('semana')).toBe('esta');
        expect(tarjetas()).toEqual(['Entrevista: Entrevista en Otra Empresa']);
        expect(aviso()).toContain('Mostrando esta semana');
        expect(
          elemento().querySelector('[data-anuncio-filtro][role="status"]')!.textContent,
        ).toContain('Mostrando esta semana');
        expect(cifraEntrevistas()!.getAttribute('aria-pressed')).toBe('true');

        cifraEntrevistas()!.click();
        await fixture.whenStable();
        expect(pulsados()).toEqual(['Todas']);
        expect(parametro('semana')).toBeNull();
        expect(aviso()).toBeNull();
      });

      it('con el filtro de un día puesto, la cifra lo cambia por el de la semana', async () => {
        grande = true;
        await crear(CON_ENTREVISTAS);
        dia('13').click();
        await fixture.whenStable();

        cifraEntrevistas()!.click();
        await fixture.whenStable();

        expect(parametro('dia')).toBeNull();
        expect(parametro('semana')).toBe('esta');
        expect(tarjetas()).toEqual(['Entrevista: Entrevista en Otra Empresa']);
      });

      it('«Quitar filtro» quita la semana y deja el tipo', async () => {
        grande = true;
        await crear(CON_ENTREVISTAS);
        cifraEntrevistas()!.click();
        await fixture.whenStable();

        botonPorTexto('Quitar filtro').click();
        await fixture.whenStable();

        expect(parametro('semana')).toBeNull();
        expect(pulsados()).toEqual(['Entrevistas']);
        expect(tarjetas()).toHaveLength(2);
      });

      it('al entrar con «?semana=esta» ya sale filtrado', async () => {
        grande = true;
        urlInicial = '/?tipo=Entrevista&semana=esta';
        await crear(CON_ENTREVISTAS);

        expect(tarjetas()).toEqual(['Entrevista: Entrevista en Otra Empresa']);
        expect(cifraEntrevistas()!.getAttribute('aria-pressed')).toBe('true');
      });

      it('sin entrevistas pendientes es solo una cifra: no hay nada que filtrar', async () => {
        grande = true;
        await crear({ ...COMPLETO, resumen: crearResumenHoy({ entrevistasSemana: 1 }) });

        expect(cifraEntrevistas()).toBeNull();
      });

      it('con entrevistas solo la semana que viene, tampoco', async () => {
        grande = true;
        await crear(PANEL);

        expect(cifraEntrevistas()).toBeNull();
      });
    });
  });

  describe('al volver a Hoy con el panel de la visita anterior', () => {
    // Apuntada desde la ficha después de la visita anterior: el panel viejo no la tiene
    const NUEVA = crearRecordatorioAgenda({
      id: 9,
      tipo: 'Entrevista',
      titulo: 'Entrevista por teléfono',
      fechaHora: '2026-11-14T10:00:00Z',
    });

    it('no quita de la URL el tipo ni el elegido hasta que llega el panel de esta visita', async () => {
      grande = true;
      urlInicial = '/?tipo=Entrevista&ver=9';
      await crearSinResponder(() => {
        TestBed.inject(RecordatoriosService).cargarPanel();
        http.expectOne('/api/panel/hoy').flush(COMPLETO);
      });

      expect(parametro('tipo')).toBe('Entrevista');
      expect(parametro('ver')).toBe('9');

      await responderPanel({ ...COMPLETO, proximos: [NUEVA] });

      expect(parametro('tipo')).toBe('Entrevista');
      expect(parametro('ver')).toBe('9');
      expect(elemento().querySelector('aside h2')!.textContent).toContain(
        'Entrevista por teléfono',
      );
    });

    it('si el panel de esta visita tampoco lo tiene, entonces sí los quita', async () => {
      grande = true;
      urlInicial = '/?tipo=Entrevista&ver=9';
      await crearSinResponder(() => {
        TestBed.inject(RecordatoriosService).cargarPanel();
        http.expectOne('/api/panel/hoy').flush(COMPLETO);
      });

      await responderPanel({ ...COMPLETO });

      expect(parametro('tipo')).toBeNull();
      expect(parametro('ver')).toBeNull();
    });
  });

  describe('al cambiar el día con la pestaña abierta', () => {
    it('vuelve a pedir el panel, sin barra de carga', async () => {
      await crear();

      ahora.set(new Date('2026-11-13T23:30:00Z')); // ya es sábado 14 en Madrid
      await fixture.whenStable();

      expect(elemento().querySelector('mat-progress-bar')).toBeNull();
      await responderPanel({ ...COMPLETO, hoy: '2026-11-14', paraHoy: [VENCIDO] });
      expect(titulos()).toContain('Para hoy · 1');
    });

    it('«Posponer» cuenta desde el día nuevo', async () => {
      await crear();

      // El viernes, «mañana» es fin de semana y la primera opción es «El lunes»
      ahora.set(new Date('2026-11-14T23:30:00Z')); // ya es domingo 15 en Madrid
      await fixture.whenStable();
      await responderPanel({ ...COMPLETO, hoy: '2026-11-15', paraHoy: [VENCIDO] });
      boton('Posponer: Publicar en LinkedIn: post de Supabase').click();
      await fixture.whenStable();

      const primera = opcionesDelMenu()[0].textContent!.replace(/\s+/g, ' ').trim();
      expect(primera).toBe('Mañana lun 16 nov');
    });

    it('si sigue siendo el mismo día, no pide nada', async () => {
      await crear();

      ahora.set(new Date('2026-11-13T15:00:00Z'));
      await fixture.whenStable();
      // afterEach comprueba que no ha salido ninguna petición
    });
  });
});
