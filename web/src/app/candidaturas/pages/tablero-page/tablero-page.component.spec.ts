import { CdkDrag, CdkDragDrop, CdkDropList } from '@angular/cdk/drag-drop';
import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AvisosService } from '../../../shared/services/avisos.service';
import { CandidaturaResumen, EstadoCandidatura } from '../../interfaces/candidatura.interface';
import { crearDetalle, crearResumen } from '../../utils/candidatura-de-prueba';
import TableroPageComponent from './tablero-page.component';

type EventoSoltar = CdkDragDrop<EstadoCandidatura, EstadoCandidatura, CandidaturaResumen>;

describe('TableroPageComponent', () => {
  let fixture: ComponentFixture<TableroPageComponent>;
  let http: HttpTestingController;
  let abrirDialogo: ReturnType<typeof vi.fn>;

  async function crear(escritorio = false): Promise<void> {
    abrirDialogo = vi.fn(() => ({ afterClosed: () => of(undefined) }));
    TestBed.configureTestingModule({
      imports: [TableroPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: { open: abrirDialogo } },
        { provide: AvisosService, useValue: { exito: vi.fn(), error: vi.fn() } },
        {
          provide: BreakpointObserver,
          useValue: {
            isMatched: () => escritorio,
            observe: () => of({ matches: escritorio, breakpoints: {} }),
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TableroPageComponent);
    await fixture.whenStable();
  }

  afterEach(() => http.verify());

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function lista(): ReturnType<HttpTestingController['expectOne']> {
    return http.expectOne((r) => r.method === 'GET' && r.url === '/api/candidaturas');
  }

  function cambioDeEstado(): ReturnType<HttpTestingController['expectOne']> {
    return http.expectOne('/api/candidaturas/1/estado');
  }

  async function pulsar(texto: string): Promise<void> {
    [...elemento().querySelectorAll('button')].find((b) => b.textContent?.includes(texto))!.click();
    await fixture.whenStable();
  }

  async function cargarTablero(candidaturas: CandidaturaResumen[]): Promise<void> {
    await crear(true);
    lista().flush(candidaturas);
    await fixture.whenStable();
  }

  function enlaceTarjeta(id = 1): HTMLElement {
    return elemento().querySelector<HTMLElement>(`[data-candidatura="${id}"] a`)!;
  }

  function columnaDe(id = 1): string | null | undefined {
    return elemento()
      .querySelector(`[data-candidatura="${id}"]`)
      ?.closest('section[data-estado]')
      ?.getAttribute('data-estado');
  }

  function inicio(): HTMLElement {
    return elemento().querySelector('section')!;
  }

  function mover(candidatura: CandidaturaResumen, destino: EstadoCandidatura): void {
    fixture.componentInstance['mover'](candidatura, destino);
  }

  function soltar(
    candidatura: CandidaturaResumen,
    de: EstadoCandidatura,
    a: EstadoCandidatura,
    isPointerOverContainer: boolean,
  ): void {
    // Lo único que mira soltar() de los contenedores es si son el mismo y qué estado llevan
    const contenedor = (estado: EstadoCandidatura) => ({ data: estado });
    const origen = contenedor(de);
    fixture.componentInstance['soltar']({
      item: { data: candidatura },
      previousContainer: origen,
      container: de === a ? origen : contenedor(a),
      isPointerOverContainer,
    } as unknown as EventoSoltar);
  }

  it('si no hay candidaturas lo dice y ofrece una sola forma de crear la primera', async () => {
    await crear();
    lista().flush([]);
    await fixture.whenStable();

    const vacio = elemento().querySelector('p.text-lg')?.parentElement;

    expect(vacio?.textContent).toContain('Todavía no hay candidaturas.');
    expect(vacio?.querySelectorAll('button, a')).toHaveLength(1);
    expect(vacio?.querySelector('button')?.textContent).toContain('Nueva candidatura');
  });

  it('si falla la carga explica qué hacer y «Reintentar» pide la lista sin perder el foco', async () => {
    await crear();
    lista().error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(elemento().querySelector('[role="alert"]')?.textContent).toContain(
      'Comprueba que está abierto en tu ordenador',
    );

    await pulsar('Reintentar');
    lista().flush([crearResumen()]);
    await fixture.whenStable();

    expect(elemento().querySelector('[role="alert"]')).toBeNull();
    expect(elemento().querySelector('app-tarjeta-candidatura')).not.toBeNull();
    expect(document.activeElement).toBe(inicio());
  });

  describe('en el PC', () => {
    const TARJETA = crearResumen({ id: 1, estado: 'Preparada' });

    it('pinta las siete columnas con la tarjeta en la suya', async () => {
      await cargarTablero([TARJETA]);

      expect(elemento().querySelectorAll('section[data-estado]')).toHaveLength(7);
      expect(columnaDe()).toBe('Preparada');
    });

    describe('columnas plegadas', () => {
      function columna(estado: EstadoCandidatura): HTMLElement {
        return elemento().querySelector<HTMLElement>(`section[data-estado="${estado}"]`)!;
      }

      /** El de plegar o desplegar la columna (el «⋮» de las tarjetas también lleva aria-expanded) */
      function botonDe(estado: EstadoCandidatura): HTMLButtonElement | null {
        return columna(estado).querySelector<HTMLButtonElement>('button[aria-label*="la columna"]');
      }

      it('Oferta, Descartada, Cerrada y las vacías salen plegadas; las demás, abiertas', async () => {
        await cargarTablero([TARJETA, crearResumen({ id: 2, estado: 'Oferta' })]);

        for (const estado of ['Oferta', 'Descartada', 'Cerrada', 'Detectada'] as const) {
          expect(botonDe(estado)?.getAttribute('aria-expanded')).toBe('false');
        }
        // Abierta y con tarjetas: no se puede plegar
        expect(botonDe('Preparada')).toBeNull();
        // Plegada, la tarjeta no se pinta, pero la columna dice cuántas tiene
        expect(columna('Oferta').querySelector('[data-candidatura]')).toBeNull();
        expect(botonDe('Oferta')?.getAttribute('aria-label')).toBe(
          'Desplegar la columna Oferta (1 candidatura)',
        );
      });

      it('al pulsarla se despliega y se puede volver a plegar', async () => {
        await cargarTablero([crearResumen({ id: 2, estado: 'Oferta' })]);

        botonDe('Oferta')!.click();
        await fixture.whenStable();

        expect(columnaDe(2)).toBe('Oferta');
        expect(botonDe('Oferta')?.getAttribute('aria-expanded')).toBe('true');

        botonDe('Oferta')!.click();
        await fixture.whenStable();

        expect(columna('Oferta').querySelector('[data-candidatura]')).toBeNull();
      });

      it('al mover una tarjeta a una columna plegada, esa columna se despliega', async () => {
        await cargarTablero([TARJETA]);

        mover(TARJETA, 'Oferta');
        await fixture.whenStable();

        expect(columnaDe()).toBe('Oferta');
        expect(botonDe('Oferta')?.getAttribute('aria-expanded')).toBe('true');
        cambioDeEstado().flush(crearDetalle({ id: 1, estado: 'Oferta' }));
        lista().flush([{ ...TARJETA, estado: 'Oferta' }]);
      });

      it('al soltar en una columna plegada, también', async () => {
        await cargarTablero([TARJETA]);
        expect(botonDe('Enviada')?.getAttribute('aria-expanded')).toBe('false');

        soltar(TARJETA, 'Preparada', 'Enviada', true);
        await fixture.whenStable();

        // Ya tiene una tarjeta, así que queda abierta (y sin botón de plegar) con la tarjeta a la vista
        expect(columnaDe()).toBe('Enviada');
        expect(botonDe('Enviada')).toBeNull();
        cambioDeEstado().flush(crearDetalle({ id: 1, estado: 'Enviada' }));
        lista().flush([{ ...TARJETA, estado: 'Enviada' }]);
      });
    });

    it('en táctil hay que mantener pulsado para arrastrar: deslizar desplaza la página', async () => {
      await cargarTablero([TARJETA]);

      const arrastre = fixture.debugElement.query(By.directive(CdkDrag)).injector.get(CdkDrag);

      expect(arrastre.dragStartDelay).toEqual({ touch: 250, mouse: 0 });
    });

    describe('soltar', () => {
      it('sobre otra columna cambia el estado', async () => {
        await cargarTablero([TARJETA]);

        soltar(TARJETA, 'Preparada', 'Enviada', true);

        expect(cambioDeEstado().request.body).toEqual({ estado: 'Enviada' });
      });

      it('fuera de las columnas no mueve nada, aunque el CDK entregue la última visitada', async () => {
        await cargarTablero([TARJETA]);
        fixture.componentInstance['arrastrando'].set(TARJETA);

        soltar(TARJETA, 'Preparada', 'Enviada', false);

        http.expectNone('/api/candidaturas/1/estado');
        expect(fixture.componentInstance['arrastrando']()).toBeNull();
        await fixture.whenStable();
        expect(columnaDe()).toBe('Preparada');
      });

      it('en la misma columna no hace nada', async () => {
        await cargarTablero([TARJETA]);

        soltar(TARJETA, 'Preparada', 'Preparada', true);

        http.expectNone('/api/candidaturas/1/estado');
      });
    });

    describe('sePuedeSoltar', () => {
      function sePuede(de: EstadoCandidatura, a: EstadoCandidatura): boolean {
        return fixture.componentInstance['sePuedeSoltar'](
          { data: crearResumen({ estado: de }) } as CdkDrag<CandidaturaResumen>,
          { data: a } as CdkDropList<EstadoCandidatura>,
        );
      }

      it('deja entrar solo en las columnas permitidas', async () => {
        await cargarTablero([]);

        expect(sePuede('Preparada', 'Enviada')).toBe(true);
        expect(sePuede('Enviada', 'Preparada')).toBe(false);
        expect(sePuede('Enviada', 'Enviada')).toBe(false);
        expect(sePuede('Descartada', 'Enviada')).toBe(false);
        expect(sePuede('Descartada', 'Detectada')).toBe(true);
      });
    });

    describe('«Mover a…»', () => {
      it('lleva el foco a la tarjeta en su columna nueva', async () => {
        await cargarTablero([TARJETA]);

        mover(TARJETA, 'Enviada');
        await fixture.whenStable();

        expect(columnaDe()).toBe('Enviada');
        expect(document.activeElement).toBe(enlaceTarjeta());
        cambioDeEstado().flush(crearDetalle({ id: 1, estado: 'Enviada' }));
        lista().flush([{ ...TARJETA, estado: 'Enviada' }]);
      });

      it('un doble clic manda un solo POST, también si la API responde entre los dos', async () => {
        await cargarTablero([TARJETA]);

        mover(TARJETA, 'Enviada');
        mover(TARJETA, 'Enviada');
        cambioDeEstado().flush(crearDetalle({ id: 1, estado: 'Enviada' }));
        lista().flush([{ ...TARJETA, estado: 'Enviada' }]);

        mover(TARJETA, 'Enviada');

        http.expectNone('/api/candidaturas/1/estado');
      });

      it('si se cancela el diálogo, no queda un foco pendiente', async () => {
        await cargarTablero([TARJETA]);

        mover(TARJETA, 'Descartada');

        expect(abrirDialogo).toHaveBeenCalledTimes(1);
        expect(fixture.componentInstance['enfocarTrasMover']()).toBeNull();
      });

      it('si se ignora por haber otro movimiento en marcha, tampoco queda pendiente', async () => {
        await cargarTablero([TARJETA]);
        mover(TARJETA, 'Enviada');

        mover(TARJETA, 'Oferta');

        expect(fixture.componentInstance['enfocarTrasMover']()).toBeNull();
        cambioDeEstado().flush(crearDetalle({ id: 1, estado: 'Enviada' }));
        lista().flush([{ ...TARJETA, estado: 'Enviada' }]);
      });

      describe('si la API lo rechaza', () => {
        const RECHAZO = { detail: 'No se puede.' };

        it('la tarjeta vuelve y el foco va al inicio del tablero, no a <body>', async () => {
          await cargarTablero([TARJETA]);
          enlaceTarjeta().focus();

          mover(TARJETA, 'Enviada');
          await fixture.whenStable();
          expect(document.activeElement).toBe(enlaceTarjeta());

          cambioDeEstado().flush(RECHAZO, { status: 500, statusText: 'Server Error' });
          await fixture.whenStable();

          expect(columnaDe()).toBe('Preparada');
          expect(document.activeElement).toBe(inicio());
        });

        it('si mientras tanto se ha ido a otro sitio, no le quita el foco', async () => {
          await cargarTablero([TARJETA]);
          mover(TARJETA, 'Enviada');
          await fixture.whenStable();
          const buscador = elemento().querySelector<HTMLInputElement>('input[type="search"]')!;
          buscador.focus();

          cambioDeEstado().flush(RECHAZO, { status: 500, statusText: 'Server Error' });
          await fixture.whenStable();

          expect(document.activeElement).toBe(buscador);
        });

        it('no pasa nada si ya se ha salido del tablero', async () => {
          await cargarTablero([TARJETA]);
          mover(TARJETA, 'Enviada');

          fixture.destroy();

          expect(() =>
            cambioDeEstado().flush(RECHAZO, { status: 500, statusText: 'Server Error' }),
          ).not.toThrow();
        });
      });
    });

    it('dos tarjetas a la vez no se esperan entre sí', async () => {
      const otra = crearResumen({ id: 2, estado: 'Preparada' });
      await cargarTablero([TARJETA, otra]);

      mover(TARJETA, 'Enviada');
      mover(otra, 'Oferta');

      cambioDeEstado().flush(crearDetalle({ id: 1, estado: 'Enviada' }));
      http.expectOne('/api/candidaturas/2/estado').flush(crearDetalle({ id: 2, estado: 'Oferta' }));
      lista().flush([
        { ...TARJETA, estado: 'Enviada' },
        { ...otra, estado: 'Oferta' },
      ]);
    });
  });

  describe('en el móvil', () => {
    it('no hay columnas ni arrastre: es una lista', async () => {
      await crear(false);
      lista().flush([crearResumen()]);
      await fixture.whenStable();

      expect(elemento().querySelector('section[data-estado]')).toBeNull();
      expect(fixture.debugElement.query(By.directive(CdkDrag))).toBeNull();
      expect(elemento().querySelector('[data-candidatura="1"]')).not.toBeNull();
    });

    it('si la tarjeta sale de la lista filtrada, el foco va al filtro activo', async () => {
      await crear(false);
      const tarjeta = crearResumen({ id: 1, estado: 'Preparada' });
      lista().flush([tarjeta]);
      await fixture.whenStable();
      await pulsar('Preparada');

      mover(tarjeta, 'Enviada');
      await fixture.whenStable();

      expect(elemento().querySelector('[data-candidatura="1"]')).toBeNull();
      expect(document.activeElement).toBe(elemento().querySelector('[aria-pressed="true"]'));
      cambioDeEstado().flush(crearDetalle({ id: 1, estado: 'Enviada' }));
      lista().flush([{ ...tarjeta, estado: 'Enviada' }]);
    });
  });
});
