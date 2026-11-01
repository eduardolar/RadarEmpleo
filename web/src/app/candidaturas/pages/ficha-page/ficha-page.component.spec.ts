import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { AccionAviso, AvisosService } from '../../../shared/services/avisos.service';
import {
  CandidaturaDetalle,
  EstadoCandidatura,
  Nota,
} from '../../interfaces/candidatura.interface';
import { CandidaturasService } from '../../services/candidaturas.service';
import { crearDetalle } from '../../utils/candidatura-de-prueba';
import { estadosPosibles } from '../../utils/transiciones-permitidas';
import FichaPageComponent from './ficha-page.component';

describe('FichaPageComponent', () => {
  let fixture: ComponentFixture<FichaPageComponent>;
  let http: HttpTestingController;
  let abrirDialogo: ReturnType<typeof vi.fn>;
  let avisos: { exito: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  const NOTA: Nota = {
    id: 9,
    candidaturaId: 1,
    texto: 'Llamar el lunes',
    fecha: '2026-10-30T09:00:00Z',
    origen: 'Manual',
  };

  async function crear(detalle: Partial<CandidaturaDetalle>): Promise<void> {
    abrirDialogo = vi.fn(() => ({ afterClosed: () => of(undefined) }));
    avisos = { exito: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      imports: [FichaPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: { open: abrirDialogo } },
        { provide: AvisosService, useValue: avisos },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FichaPageComponent);
    fixture.componentRef.setInput('id', 1);
    await fixture.whenStable();
    const estado = detalle.estado ?? 'Detectada';
    http.expectOne('/api/candidaturas/1').flush(
      crearDetalle({
        id: 1,
        estado,
        estadosPosibles: estadosPosibles(estado),
        ...detalle,
      }),
    );
    await fixture.whenStable();
  }

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    http.verify();
  });

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function botonesDeEstado(): HTMLButtonElement[] {
    return [...elemento().querySelectorAll<HTMLButtonElement>('[role="group"] button')];
  }

  function destacados(): string[] {
    return botonesDeEstado()
      .filter((b) => b.hasAttribute('mat-flat-button'))
      .map((b) => b.textContent!.trim());
  }

  function boton(texto: string): HTMLButtonElement {
    return botonesDeEstado().find((b) => b.textContent?.includes(texto))!;
  }

  /** El menú se pinta fuera de la ficha, en la capa de Material */
  function opcionesDelMenu(): HTMLButtonElement[] {
    return [...document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')];
  }

  function estadoActual(): HTMLElement {
    return elemento().querySelector<HTMLElement>('header [tabindex="-1"]')!;
  }

  function campoNota(): HTMLTextAreaElement {
    return elemento().querySelector<HTMLTextAreaElement>('[formcontrolname="texto"]')!;
  }

  function responderEstado(estado: EstadoCandidatura): void {
    http
      .expectOne('/api/candidaturas/1/estado')
      .flush(crearDetalle({ id: 1, estado, estadosPosibles: estadosPosibles(estado) }));
  }

  /** Con reloj simulado no se puede usar whenStable: el pintado espera también a un temporizador */
  async function pasan(ms: number): Promise<void> {
    await vi.advanceTimersByTimeAsync(ms);
  }

  /** Un temporizador que se programa dentro de otro corre 1 ms después: así llega el pintado */
  const MARGEN_PINTADO_MS = 1;

  it('cuenta los días en el estado desde fechaEstado aunque se haya editado hoy', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-30T10:00:00Z'));

    await crear({
      fechaEstado: '2026-10-27T10:00:00Z',
      fechaUltimoCambio: '2026-10-30T09:00:00Z',
    });

    expect(elemento().textContent).toContain('En este estado: 3 días');
  });

  it('el contador de días se actualiza al volver a la pestaña otro día', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-30T10:00:00Z'));
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    await crear({ fechaEstado: '2026-10-30T09:00:00Z' });
    expect(elemento().textContent).toContain('En este estado: hoy');

    vi.setSystemTime(new Date('2026-11-01T10:00:00Z'));
    document.dispatchEvent(new Event('visibilitychange'));
    await fixture.whenStable();

    expect(elemento().textContent).toContain('En este estado: 2 días');
  });

  it.each([
    ['Detectada', ['Marcar preparada']],
    ['Preparada', ['Marcar enviada']],
    ['Enviada', ['Pasar a Entrevista']],
    ['Entrevista', ['Pasar a Oferta']],
    ['Oferta', []],
  ] as const)('en %s destaca un solo botón, el siguiente paso', async (estado, esperados) => {
    await crear({ estado });

    expect(destacados()).toEqual(esperados);
  });

  it('deja pocos botones a la vista y lo menos habitual en «Más opciones»', async () => {
    await crear({ estado: 'Enviada' });

    expect(botonesDeEstado().map((b) => b.textContent!.trim())).toEqual([
      'Pasar a Entrevista',
      'Descartar',
      'Más opciones',
    ]);
    expect(boton('Descartar').hasAttribute('mat-stroked-button')).toBe(true);

    boton('Más opciones').click();
    await fixture.whenStable();

    expect(opcionesDelMenu().map((b) => b.textContent!.trim())).toEqual([
      'Pasar a Oferta',
      'Cerrar candidatura',
    ]);
  });

  it('una opción de «Más opciones» cambia el estado igual que un botón', async () => {
    await crear({ estado: 'Enviada' });

    boton('Más opciones').click();
    await fixture.whenStable();
    opcionesDelMenu()[0].click();
    responderEstado('Oferta');
    await fixture.whenStable();

    expect(estadoActual().textContent).toContain('Oferta');
  });

  it('sin nada que esconder no enseña «Más opciones»', async () => {
    await crear({ estado: 'Cerrada' });

    expect(boton('Más opciones')).toBeUndefined();
  });

  it('avanzar va en un toque y lleva el foco al estado nuevo', async () => {
    await crear({ estado: 'Preparada' });

    boton('Marcar enviada').click();
    await fixture.whenStable();

    expect(abrirDialogo).not.toHaveBeenCalled();
    const peticion = http.expectOne('/api/candidaturas/1/estado');
    expect(peticion.request.body).toEqual({ estado: 'Enviada' });
    peticion.flush(
      crearDetalle({ id: 1, estado: 'Enviada', estadosPosibles: estadosPosibles('Enviada') }),
    );
    await fixture.whenStable();

    expect(estadoActual().textContent).toContain('Enviada');
    expect(document.activeElement).toBe(estadoActual());
    expect(destacados()).toEqual(['Pasar a Entrevista']);
  });

  it('un segundo clic mientras espera no manda otro cambio', async () => {
    await crear({ estado: 'Preparada' });

    boton('Marcar enviada').click();
    await fixture.whenStable();
    boton('Marcar enviada').click();

    const peticion = http.expectOne('/api/candidaturas/1/estado');
    expect(boton('Marcar enviada').getAttribute('aria-disabled')).toBe('true');
    peticion.flush(
      crearDetalle({ id: 1, estado: 'Enviada', estadosPosibles: estadosPosibles('Enviada') }),
    );
  });

  describe('doble clic en un botón de estado', () => {
    it.each([
      ['Detectada', 'Preparada', 'Marcar preparada', 'Marcar enviada'],
      ['Preparada', 'Enviada', 'Marcar enviada', 'Pasar a Entrevista'],
      ['Enviada', 'Entrevista', 'Pasar a Entrevista', 'Pasar a Oferta'],
      ['Descartada', 'Detectada', 'Reabrir', 'Marcar preparada'],
    ] as const)(
      'desde %s: si la API responde entre los dos clics, «%s» no se encadena con el paso siguiente',
      async (estado, destino, primero, segundo) => {
        await crear({ estado });
        vi.useFakeTimers();

        boton(primero).click();
        responderEstado(destino);
        await pasan(100);

        // El segundo clic cae en el botón del paso siguiente, que acaba de salir en ese sitio
        boton(segundo).click();
        await pasan(0);

        http.expectNone('/api/candidaturas/1/estado');
        expect(estadoActual().textContent).toContain(destino);
      },
    );

    it('el bloqueo se ve en los botones y se levanta a los 500 ms', async () => {
      await crear({ estado: 'Preparada' });
      vi.useFakeTimers();

      boton('Marcar enviada').click();
      responderEstado('Enviada');
      await pasan(499);
      expect(boton('Pasar a Entrevista').getAttribute('aria-disabled')).toBe('true');
      boton('Pasar a Entrevista').click();
      http.expectNone('/api/candidaturas/1/estado');

      await pasan(1 + MARGEN_PINTADO_MS);
      expect(boton('Pasar a Entrevista').getAttribute('aria-disabled')).not.toBe('true');
      boton('Pasar a Entrevista').click();
      await pasan(0);

      responderEstado('Entrevista');
      await pasan(0);
      expect(estadoActual().textContent).toContain('Entrevista');
    });

    it('si el cambio falla, también bloquea un momento y luego deja reintentar', async () => {
      await crear({ estado: 'Preparada' });
      vi.useFakeTimers();

      boton('Marcar enviada').click();
      http
        .expectOne('/api/candidaturas/1/estado')
        .flush({ detail: 'Ha fallado.' }, { status: 500, statusText: 'Server Error' });
      await pasan(100);
      boton('Marcar enviada').click();
      http.expectNone('/api/candidaturas/1/estado');

      await pasan(400 + MARGEN_PINTADO_MS);
      expect(boton('Marcar enviada').getAttribute('aria-disabled')).not.toBe('true');
      boton('Marcar enviada').click();
      await pasan(0);

      responderEstado('Enviada');
    });
  });

  it('Descartar pasa por el diálogo y, si se cancela, no manda nada', async () => {
    await crear({ estado: 'Enviada' });

    boton('Descartar').click();

    expect(abrirDialogo).toHaveBeenCalledTimes(1);
    http.expectNone('/api/candidaturas/1/estado');
  });

  it('en una candidatura descartada ofrece «Reabrir» sin destacar nada', async () => {
    await crear({ estado: 'Descartada', motivoDescarte: 'Ingles' });

    expect(destacados()).toEqual([]);
    expect(boton('Reabrir')).toBeTruthy();
  });

  it('si la ficha no existe lo dice y lleva al tablero', async () => {
    abrirDialogo = vi.fn();
    TestBed.configureTestingModule({
      imports: [FichaPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: { open: abrirDialogo } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FichaPageComponent);
    fixture.componentRef.setInput('id', 99);
    await fixture.whenStable();

    http
      .expectOne('/api/candidaturas/99')
      .flush({ title: 'No se ha encontrado.' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(elemento().textContent).toContain('Esta candidatura no existe o se ha borrado.');
    expect(elemento().querySelector('[role="alert"] a')?.getAttribute('href')).toBe('/tablero');
  });

  it('si la han borrado desde otro sitio, enseña «no existe» sin los datos debajo', async () => {
    await crear({ puesto: 'Puesto que ya no existe' });

    TestBed.inject(CandidaturasService).cargarDetalle(1);
    http
      .expectOne('/api/candidaturas/1')
      .flush({ title: 'No se ha encontrado.' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(elemento().textContent).toContain('Esta candidatura no existe o se ha borrado.');
    expect(elemento().textContent).not.toContain('Puesto que ya no existe');
    expect(elemento().querySelector('header')).toBeNull();
  });

  it('si se borró al cambiar de estado, pide la ficha de nuevo y dice que no existe', async () => {
    await crear({ estado: 'Preparada', puesto: 'Puesto que ya no existe' });

    boton('Marcar enviada').click();
    http
      .expectOne('/api/candidaturas/1/estado')
      .flush({ title: 'No se ha encontrado.' }, { status: 404, statusText: 'Not Found' });
    http
      .expectOne('/api/candidaturas/1')
      .flush({ title: 'No se ha encontrado.' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(elemento().textContent).toContain('Esta candidatura no existe o se ha borrado.');
    expect(elemento().textContent).not.toContain('Puesto que ya no existe');
  });

  describe('sin conexión con la ficha ya vista', () => {
    async function fallarRecarga(): Promise<void> {
      TestBed.inject(CandidaturasService).cargarDetalle(1);
      http.expectOne('/api/candidaturas/1').error(new ProgressEvent('error'));
      await fixture.whenStable();
    }

    it('avisa de que lo que se ve es lo último que se cargó', async () => {
      await crear({ puesto: 'Dev guardado' });

      await fallarRecarga();

      const alerta = elemento().querySelector('[role="alert"]');
      expect(alerta?.textContent).toContain('No se ha podido actualizar esta ficha.');
      expect(alerta?.textContent).toContain('No se puede conectar con RadarEmpleo');
      expect(alerta?.textContent).toContain('puede no estar al día');
      expect(elemento().querySelector('h1')?.textContent).toContain('Dev guardado');
    });

    it('si es la primera vez que se abre, no habla de datos que no hay', async () => {
      await crear({});
      TestBed.inject(CandidaturasService).cargarDetalle(2);
      http.expectOne('/api/candidaturas/2').error(new ProgressEvent('error'));
      await fixture.whenStable();

      const alerta = elemento().querySelector('[role="alert"]');
      expect(alerta?.textContent).toContain('No se puede conectar con RadarEmpleo');
      expect(alerta?.textContent).not.toContain('puede no estar al día');
    });

    it('«Reintentar» quita el aviso en cuanto la ficha carga', async () => {
      await crear({});
      await fallarRecarga();

      [...elemento().querySelectorAll('button')]
        .find((b) => b.textContent?.includes('Reintentar'))!
        .click();
      http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));
      await fixture.whenStable();

      expect(elemento().querySelector('[role="alert"]')).toBeNull();
    });
  });

  it('los textos largos pueden partirse y las columnas encogerse (URL en la pega, una nota...)', async () => {
    await crear({ pega: `https://acme.com/${'a'.repeat(300)}` });

    const raiz = elemento().querySelector('[tabindex="-1"]');
    const columnas = elemento().querySelectorAll('.grid > section');

    // La maquetación no se puede medir en jsdom: esto solo evita que se quiten las clases
    expect(raiz?.classList.contains('[overflow-wrap:anywhere]')).toBe(true);
    expect(columnas).toHaveLength(2);
    columnas.forEach((columna) => expect(columna.classList.contains('min-w-0')).toBe(true));
  });

  describe('notas', () => {
    function botonBorrarNota(): HTMLButtonElement {
      return elemento().querySelector<HTMLButtonElement>('ol button')!;
    }

    function tituloHistorial(): HTMLElement {
      return elemento().querySelector<HTMLElement>('#titulo-historial')!;
    }

    async function escribirYEnviarNota(): Promise<void> {
      const campo = campoNota();
      campo.value = 'Llamar el lunes';
      campo.dispatchEvent(new Event('input'));
      elemento().querySelector<HTMLButtonElement>('form button[type="submit"]')!.click();
      await fixture.whenStable();
    }

    it('«Añadir nota» del aviso lleva el foco al campo de la nota', async () => {
      await crear({ estado: 'Preparada' });
      vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      boton('Marcar enviada').click();
      responderEstado('Enviada');
      await fixture.whenStable();
      expect(document.activeElement).toBe(estadoActual());

      (avisos.exito.mock.calls[0][1] as AccionAviso).alPulsar();
      await fixture.whenStable();

      expect(document.activeElement).toBe(campoNota());
    });

    it('al borrar una nota, el foco va al título del historial', async () => {
      await crear({ notas: [NOTA] });
      abrirDialogo.mockReturnValue({ afterClosed: () => of(true) });
      botonBorrarNota().focus();

      botonBorrarNota().click();
      http.expectOne('/api/notas/9').flush(null, { status: 204, statusText: 'No Content' });
      await fixture.whenStable();

      expect(elemento().querySelector('ol button')).toBeNull();
      expect(document.activeElement).toBe(tituloHistorial());
      expect(avisos.exito).toHaveBeenCalledWith('Nota borrada.');
    });

    it('si no se puede borrar, la nota sigue ahí y el foco no se mueve', async () => {
      await crear({ notas: [NOTA] });
      abrirDialogo.mockReturnValue({ afterClosed: () => of(true) });
      const borrar = botonBorrarNota();
      borrar.focus();

      borrar.click();
      http
        .expectOne('/api/notas/9')
        .flush({ detail: 'No se pudo.' }, { status: 500, statusText: 'Server Error' });
      await fixture.whenStable();

      expect(document.activeElement).toBe(borrar);
      expect(avisos.error).toHaveBeenCalled();
    });

    it('salir mientras se guarda una nota no la cancela ni da errores', async () => {
      await crear({});
      await escribirYEnviarNota();

      fixture.destroy();
      const peticion = http.expectOne('/api/candidaturas/1/notas');
      expect(peticion.cancelled).toBe(false);
      peticion.flush(NOTA, { status: 201, statusText: 'Created' });

      expect(avisos.exito).toHaveBeenCalledWith('Nota añadida.');
    });

    it('si falla con la ficha cerrada, el error por campo sale como aviso', async () => {
      await crear({});
      await escribirYEnviarNota();

      fixture.destroy();
      http
        .expectOne('/api/candidaturas/1/notas')
        .flush(
          { detail: 'La nota es demasiado larga.', errors: { texto: ['Demasiado larga.'] } },
          { status: 400, statusText: 'Bad Request' },
        );

      expect(avisos.error).toHaveBeenCalledWith('La nota es demasiado larga.');
    });

    it('borrar una nota y salir antes de la respuesta no intenta mover el foco', async () => {
      await crear({ notas: [NOTA] });
      abrirDialogo.mockReturnValue({ afterClosed: () => of(true) });
      botonBorrarNota().click();

      fixture.destroy();
      const peticion = http.expectOne('/api/notas/9');
      expect(peticion.cancelled).toBe(false);
      peticion.flush(null, { status: 204, statusText: 'No Content' });

      expect(avisos.exito).toHaveBeenCalledWith('Nota borrada.');
    });
  });

  it('borrar la candidatura y salir antes de la respuesta no devuelve al tablero', async () => {
    await crear({});
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    abrirDialogo.mockReturnValue({ afterClosed: () => of(true) });
    [...elemento().querySelectorAll('button')]
      .find((b) => b.getAttribute('aria-label') === 'Borrar candidatura')!
      .click();

    fixture.destroy();
    http.expectOne('/api/candidaturas/1').flush(null, { status: 204, statusText: 'No Content' });

    expect(avisos.exito).toHaveBeenCalledWith('Candidatura borrada.');
    expect(navegar).not.toHaveBeenCalled();
  });
});
