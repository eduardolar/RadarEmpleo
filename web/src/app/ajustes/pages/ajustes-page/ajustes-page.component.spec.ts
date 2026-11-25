import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AgentesService } from '../../../agentes/services/agentes.service';
import { crearEstadoAgentes, crearGasto } from '../../../agentes/utils/ejecucion-de-prueba';
import { TEXTO_RECHAZADOS } from '../../../agentes/utils/textos-ejecucion';
import { SistemaService } from '../../../core/services/sistema.service';
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

  afterEach(() => {
    http.verify();
    document.body.replaceChildren();
  });

  function pintar() {
    const fixture = TestBed.createComponent(AjustesPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  const texto = (elemento: HTMLElement) => elemento.textContent!.replace(/\s+/g, ' ');

  const URL_ESTADO = '/api/agentes/estado';
  const PROHIBIDO = { status: 403, statusText: 'Forbidden' };
  const INFO_CON_AGENTES = crearSistemaInfo({ agentesDisponibles: true, agentesHabilitados: true });

  /** Con agentes disponibles: responde /api/sistema/info y devuelve el bloque de agentes */
  function pintarConAgentes() {
    const fixture = pintar();
    http.expectOne('/api/sistema/info').flush(INFO_CON_AGENTES);
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
      expect(bloque().querySelector('[role="status"]')?.textContent).toContain(
        'Calculando el uso de hoy…',
      );

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
      expect(contenido).toContain('Lo fija la app; por ahora no se cambia desde aquí.');
      expect(contenido).toContain('2.1.185');
      expect(contenido).not.toContain('Distinta de la probada');
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
      expect(bloque().querySelector('dd:last-of-type .font-mono')!.textContent).toBe('2.1.190');
      expect(contenido).toContain(
        'Distinta de la probada (2.1.185). Debería funcionar; si alguna tarea falla, prueba con la 2.1.185.',
      );
    });

    it('sin versión conocida: «No se ha podido comprobar»', () => {
      const { fixture, bloque } = pintarConAgentes();
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes({ versionClaude: null }));
      fixture.detectChanges();

      expect(bloque().querySelector('dd:last-of-type')!.textContent!.trim()).toBe(
        'No se ha podido comprobar',
      );
    });

    it('si falla el estado, avisa sin el código y «Reintentar» espera, lo vuelve a pedir y lleva el foco al título', () => {
      const { fixture, bloque } = pintarConAgentes();
      http.expectOne(URL_ESTADO).flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      fixture.detectChanges();

      const aviso = bloque().querySelector<HTMLElement>('p[role="alert"]')!;
      expect(aviso.textContent!.trim()).toBe(
        'No se ha podido consultar el uso de hoy. Vuelve a probar en un momento.',
      );
      expect(aviso.querySelector('button')).toBeNull();
      const boton = bloque().querySelector<HTMLButtonElement>('button')!;
      boton.click();
      fixture.detectChanges();
      expect(boton.textContent!.trim()).toBe('Reintentando…');
      expect(boton.getAttribute('aria-disabled')).toBe('true');
      boton.click();

      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
      fixture.detectChanges();

      expect(bloque().querySelector('[role="alert"]')).toBeNull();
      expect(texto(bloque())).toContain('Hoy aún no has usado Claude');
      expect(document.activeElement).toBe(bloque().querySelector('h2'));
    });

    it('si el reintento vuelve a fallar, el aviso se queda y el botón vuelve a «Reintentar»', () => {
      const { fixture, bloque } = pintarConAgentes();
      http.expectOne(URL_ESTADO).flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      fixture.detectChanges();

      const boton = bloque().querySelector<HTMLButtonElement>('button')!;
      boton.focus();
      boton.click();
      fixture.detectChanges();
      http.expectOne(URL_ESTADO).flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      fixture.detectChanges();

      expect(bloque().querySelector('p[role="alert"]')).not.toBeNull();
      expect(boton.textContent!.trim()).toBe('Reintentar');
      expect(document.activeElement).toBe(boton);
    });

    it('si el reintento da 403, pasa a «No disponibles ahora» y el foco va al título, no a <body>', () => {
      const { fixture, bloque } = pintarConAgentes();
      http.expectOne(URL_ESTADO).flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });
      fixture.detectChanges();

      const boton = bloque().querySelector<HTMLButtonElement>('button')!;
      boton.focus();
      boton.click();
      fixture.detectChanges();
      http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
      fixture.detectChanges();

      expect(boton.isConnected).toBe(false);
      expect(texto(bloque())).toContain('No disponibles ahora');
      expect(bloque().querySelector('[role="alert"]')).toBeNull();
      expect(document.activeElement).toBe(bloque().querySelector('h2'));
    });

    it('si «Volver a comprobar» vuelve a dar 403, el botón sigue ahí con el foco', () => {
      const { fixture, bloque } = pintarConAgentes();
      http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
      fixture.detectChanges();

      const boton = bloque().querySelector<HTMLButtonElement>('button')!;
      boton.focus();
      boton.click();
      http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
      fixture.detectChanges();

      expect(boton.isConnected).toBe(true);
      expect(document.activeElement).toBe(boton);
    });

    it('un 403 en el estado: «No disponibles ahora» y «Volver a comprobar», sin aviso de error', () => {
      const { fixture, bloque } = pintarConAgentes();
      http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
      fixture.detectChanges();

      expect(texto(bloque())).toContain('No disponibles ahora');
      expect(texto(bloque())).toContain(TEXTO_RECHAZADOS);
      expect(bloque().querySelector('[role="alert"]')).toBeNull();
      expect(bloque().querySelector('a')).toBeNull();
      expect(bloque().querySelector('button')!.textContent!.trim()).toBe('Volver a comprobar');
      http.expectNone(URL_ESTADO);
    });

    it('«Volver a comprobar» lo pide otra vez y, si contesta bien, lleva el foco al título', () => {
      const { fixture, bloque } = pintarConAgentes();
      http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
      fixture.detectChanges();

      bloque().querySelector<HTMLButtonElement>('button')!.click();
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
      fixture.detectChanges();

      expect(texto(bloque())).toContain('Uso de hoy');
      expect(document.activeElement).toBe(bloque().querySelector('h2'));
    });

    describe('con un 403 de una visita anterior', () => {
      beforeEach(() => {
        TestBed.inject(SistemaService).cargarInfo();
        http.expectOne('/api/sistema/info').flush(INFO_CON_AGENTES);
        TestBed.inject(AgentesService).cargarEstado();
        http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
      });

      function bloqueAlEntrar() {
        const fixture = pintar();
        return {
          fixture,
          bloque: () =>
            (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
              'section[aria-labelledby="titulo-agentes"]',
            )!,
        };
      }

      it('al entrar pregunta una vez y, si vuelve a dar 403, no insiste', () => {
        const { fixture, bloque } = bloqueAlEntrar();
        http.expectOne(URL_ESTADO).flush(null, PROHIBIDO);
        fixture.detectChanges();

        expect(texto(bloque())).toContain('No disponibles ahora');
        http.expectNone(URL_ESTADO);
      });

      it('al entrar pregunta una vez y, si contesta bien, enseña el uso', () => {
        const { fixture, bloque } = bloqueAlEntrar();
        http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
        fixture.detectChanges();

        expect(texto(bloque())).toContain('Uso de hoy');
        http.expectNone(URL_ESTADO);
      });
    });
  });
});
