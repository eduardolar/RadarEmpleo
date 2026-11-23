import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { EjecucionAgente } from '../../../agentes/interfaces/ejecucion.interface';
import { INTERVALO_SEGUIMIENTO_MS } from '../../../agentes/services/agentes.service';
import { crearEjecucion, crearEstadoAgentes } from '../../../agentes/utils/ejecucion-de-prueba';
import { SistemaService } from '../../../core/services/sistema.service';
import { crearSistemaInfo } from '../../../core/utils/sistema-de-prueba';
import { DialogoRecordatorio } from '../../../recordatorios/components/dialogo-recordatorio/dialogo-recordatorio';
import { crearRecordatorio } from '../../../recordatorios/utils/recordatorio-de-prueba';
import { AccionAviso, AvisosService } from '../../../shared/services/avisos.service';
import {
  CandidaturaDetalle,
  EstadoCandidatura,
  Nota,
} from '../../interfaces/candidatura.interface';
import { CambioEstadoService } from '../../services/cambio-estado.service';
import { CandidaturasService } from '../../services/candidaturas.service';
import { crearDetalle } from '../../utils/candidatura-de-prueba';
import { estadosPosibles } from '../../utils/transiciones-permitidas';
import FichaPageComponent from './ficha-page.component';

describe('FichaPageComponent', () => {
  let fixture: ComponentFixture<FichaPageComponent>;
  let http: HttpTestingController;
  let abrirDialogo: ReturnType<typeof vi.fn>;
  let avisos: Record<'exito' | 'error' | 'deshacible', ReturnType<typeof vi.fn>>;

  const NOTA: Nota = {
    id: 9,
    candidaturaId: 1,
    texto: 'Llamar el lunes',
    fecha: '2026-10-30T09:00:00Z',
    origen: 'Manual',
  };

  async function crear(detalle: Partial<CandidaturaDetalle>): Promise<void> {
    abrirDialogo = vi.fn(() => ({ afterClosed: () => of(undefined) }));
    avisos = { exito: vi.fn(), error: vi.fn(), deshacible: vi.fn() };
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

      (avisos.deshacible.mock.calls[0][2] as AccionAviso).alPulsar();
      await fixture.whenStable();

      expect(document.activeElement).toBe(campoNota());
    });

    it('«Deshacer» del aviso devuelve la ficha al estado anterior con lo que dice la API', async () => {
      await crear({ estado: 'Preparada' });
      vi.useFakeTimers();
      boton('Marcar enviada').click();
      responderEstado('Enviada');
      await pasan(MARGEN_PINTADO_MS);
      expect(estadoActual().textContent).toContain('Enviada');

      // Pasada la pausa del doble clic, que también frena el deshacer
      await pasan(500 + MARGEN_PINTADO_MS);
      (document.activeElement as HTMLElement | null)?.blur();
      (avisos.deshacible.mock.calls[0][1] as () => boolean)();
      http.expectOne('/api/candidaturas/1/estado/deshacer').flush(
        crearDetalle({
          id: 1,
          estado: 'Preparada',
          estadosPosibles: estadosPosibles('Preparada'),
        }),
      );
      await pasan(MARGEN_PINTADO_MS);

      expect(estadoActual().textContent).toContain('Preparada');
      expect(boton('Marcar enviada')).toBeDefined();
      expect(avisos.exito).toHaveBeenCalledWith('Cambio deshecho.');
      // El foco vuelve al bloque «Cambiar estado», no a <body>
      expect(document.activeElement).toBe(
        elemento().querySelector('[aria-labelledby="titulo-acciones"]'),
      );
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

  describe('recordatorios', () => {
    const SEGUIMIENTO = crearRecordatorio({
      id: 4,
      candidaturaId: 1,
      tipo: 'Seguimiento',
      titulo: 'Seguimiento: Desarrollador Angular en Acme',
      fechaHora: '2026-11-20T08:00:00Z',
    });

    function seccion(): HTMLElement {
      return elemento().querySelector<HTMLElement>('[aria-labelledby="titulo-recordatorios"]')!;
    }

    function botonRecordatorio(etiqueta: string): HTMLButtonElement {
      return seccion().querySelector<HTMLButtonElement>(`button[aria-label="${etiqueta}"]`)!;
    }

    function botonAnadir(): HTMLButtonElement | undefined {
      return [...seccion().querySelectorAll('button')].find((b) =>
        b.textContent?.includes('Añadir recordatorio'),
      );
    }

    it('los enseña como tarjetas, sin posponer ni relleno', async () => {
      await crear({ estado: 'Enviada', recordatorios: [SEGUIMIENTO] });

      expect(seccion().querySelectorAll('app-tarjeta-recordatorio')).toHaveLength(1);
      expect(seccion().textContent).toContain('Seguimiento: Desarrollador Angular en Acme');
      expect(seccion().querySelector('[mat-flat-button]')).toBeNull();
      expect(seccion().querySelector('button[aria-label^="Posponer"]')).toBeNull();
      // Ya se está en la ficha: el título no enlaza a ella
      expect(seccion().querySelector('a')).toBeNull();
    });

    it('sin recordatorios mantiene el texto de siempre', async () => {
      await crear({});

      expect(seccion().textContent).toContain('No hay recordatorios.');
    });

    it('«Añadir recordatorio» abre el diálogo con la candidatura, como seguimiento', async () => {
      await crear({ estado: 'Enviada' });

      botonAnadir()!.click();

      const [componente, opciones] = abrirDialogo.mock.calls[0];
      expect(componente).toBe(DialogoRecordatorio);
      expect(opciones.data.tipoInicial).toBe('Seguimiento');
      expect(opciones.data.candidatura).toEqual(
        expect.objectContaining({ id: 1, empresa: 'Acme', puesto: 'Desarrollador Angular' }),
      );
    });

    it('con la candidatura en entrevista, empieza como entrevista', async () => {
      await crear({ estado: 'Entrevista' });

      botonAnadir()!.click();

      expect(abrirDialogo.mock.calls[0][1].data.tipoInicial).toBe('Entrevista');
    });

    it('«Apuntar la entrevista» del aviso abre el diálogo como entrevista, una sola vez', async () => {
      await crear({ estado: 'Enviada' });
      vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      TestBed.inject(CambioEstadoService).mover(
        { id: 1, empresa: 'Acme', puesto: 'Desarrollador Angular', estado: 'Enviada' },
        'Entrevista',
      );
      http.expectOne('/api/candidaturas/1/estado').flush(
        crearDetalle({
          id: 1,
          estado: 'Entrevista',
          estadosPosibles: estadosPosibles('Entrevista'),
        }),
      );
      await fixture.whenStable();
      expect(abrirDialogo).not.toHaveBeenCalled();

      const accion = avisos.deshacible.mock.calls[0][2] as AccionAviso;
      expect(accion.texto).toBe('Apuntar la entrevista');
      accion.alPulsar();
      await fixture.whenStable();

      expect(abrirDialogo).toHaveBeenCalledTimes(1);
      const [componente, opciones] = abrirDialogo.mock.calls[0];
      expect(componente).toBe(DialogoRecordatorio);
      expect(opciones.data.tipoInicial).toBe('Entrevista');
      expect(opciones.data.candidatura).toEqual(expect.objectContaining({ id: 1 }));
      expect(TestBed.inject(CambioEstadoService).entrevistaPedida()).toBeNull();
    });

    it('con una oferta en la mano, empieza como plazo', async () => {
      await crear({ estado: 'Oferta' });

      botonAnadir()!.click();

      expect(abrirDialogo.mock.calls[0][1].data.tipoInicial).toBe('Plazo');
    });

    it('va con borde: el relleno de la ficha es el del cambio de estado', async () => {
      await crear({ estado: 'Enviada' });

      expect(botonAnadir()!.hasAttribute('mat-stroked-button')).toBe(true);
    });

    it.each(['Descartada', 'Cerrada'] as const)(
      'en una %s no se puede añadir y dice por qué no salen en Hoy',
      async (estado) => {
        await crear({ estado, recordatorios: [SEGUIMIENTO] });

        expect(botonAnadir()).toBeUndefined();
        expect(seccion().textContent).toContain(
          `No sale en Inicio porque la candidatura está ${estado.toLowerCase()}.`,
        );
      },
    );

    it('en una descartada con todo hecho no hace falta la explicación', async () => {
      await crear({ estado: 'Descartada', recordatorios: [{ ...SEGUIMIENTO, hecho: true }] });

      expect(seccion().textContent).not.toContain('No sale en Inicio');
    });

    it('en una candidatura en curso no sale la explicación', async () => {
      await crear({ estado: 'Enviada', recordatorios: [SEGUIMIENTO] });

      expect(seccion().textContent).not.toContain('No sale en Inicio');
    });

    it('marcar como hecho pone al día la lista sin recargar la ficha', async () => {
      await crear({ estado: 'Enviada', recordatorios: [SEGUIMIENTO] });

      botonRecordatorio('Hecho: Seguimiento: Desarrollador Angular en Acme').click();
      const peticion = http.expectOne('/api/recordatorios/4/hecho');
      expect(peticion.request.body).toEqual({ hecho: true });
      peticion.flush({ ...SEGUIMIENTO, hecho: true, fechaHecho: '2026-11-13T09:00:00Z' });
      await fixture.whenStable();

      expect(
        botonRecordatorio('Pendiente otra vez: Seguimiento: Desarrollador Angular en Acme'),
      ).not.toBeNull();
      expect(avisos.deshacible).toHaveBeenCalledWith(
        'Hecho: «Seguimiento: Desarrollador Angular en Acme».',
        expect.any(Function),
      );
      // afterEach comprueba que no se ha pedido el panel de Hoy (no se ha pasado por él)
    });

    it('«Pendiente otra vez» lo reabre', async () => {
      await crear({ estado: 'Enviada', recordatorios: [{ ...SEGUIMIENTO, hecho: true }] });

      botonRecordatorio('Pendiente otra vez: Seguimiento: Desarrollador Angular en Acme').click();
      const peticion = http.expectOne('/api/recordatorios/4/hecho');
      expect(peticion.request.body).toEqual({ hecho: false });
      peticion.flush(SEGUIMIENTO);
      await fixture.whenStable();

      expect(botonRecordatorio('Hecho: Seguimiento: Desarrollador Angular en Acme')).not.toBeNull();
    });

    it('editar abre el diálogo con el recordatorio', async () => {
      await crear({ estado: 'Enviada', recordatorios: [SEGUIMIENTO] });

      botonRecordatorio('Más acciones: Seguimiento: Desarrollador Angular en Acme').click();
      await fixture.whenStable();
      opcionesDelMenu()[0].click();

      expect(abrirDialogo.mock.calls[0][1].data.recordatorio).toEqual(SEGUIMIENTO);
    });

    it('borrar pide confirmación, lo quita de la lista y lleva el foco al título', async () => {
      await crear({ estado: 'Enviada', recordatorios: [SEGUIMIENTO] });
      abrirDialogo.mockReturnValue({ afterClosed: () => of(true) });

      botonRecordatorio('Más acciones: Seguimiento: Desarrollador Angular en Acme').click();
      await fixture.whenStable();
      opcionesDelMenu()[1].click();

      expect(abrirDialogo.mock.calls[0][1].data.titulo).toBe('Borrar recordatorio');
      http.expectOne('/api/recordatorios/4').flush(null, { status: 204, statusText: 'No Content' });
      await fixture.whenStable();

      expect(seccion().querySelector('app-tarjeta-recordatorio')).toBeNull();
      expect(avisos.exito).toHaveBeenCalledWith('Recordatorio borrado.');
      expect(document.activeElement).toBe(seccion().querySelector('h2'));
    });

    it('si no se confirma, no borra nada', async () => {
      await crear({ estado: 'Enviada', recordatorios: [SEGUIMIENTO] });
      abrirDialogo.mockReturnValue({ afterClosed: () => of(false) });

      botonRecordatorio('Más acciones: Seguimiento: Desarrollador Angular en Acme').click();
      await fixture.whenStable();
      opcionesDelMenu()[1].click();

      expect(seccion().querySelectorAll('app-tarjeta-recordatorio')).toHaveLength(1);
    });
  });

  describe('analizar el encaje con Claude', () => {
    const URL_EJECUCIONES = '/api/agentes/ejecuciones';
    const analisis = (cambios: Partial<EjecucionAgente> = {}) =>
      crearEjecucion({ id: 5, candidaturaId: 1, ...cambios });

    /** Con reloj simulado desde aquí: el seguimiento pide cada 5 s */
    async function conAgentes(ultima: EjecucionAgente | null = null): Promise<void> {
      vi.useFakeTimers();
      TestBed.inject(SistemaService).cargarInfo();
      http.expectOne('/api/sistema/info').flush(crearSistemaInfo({ agentesDisponibles: true }));
      await pasan(0);
      http.expectOne('/api/agentes/estado').flush(crearEstadoAgentes());
      const ultimas = http.expectOne((r) => r.url === URL_EJECUCIONES);
      expect(ultimas.request.params.toString()).toBe(
        'tarea=AnalizarEncaje&limite=1&candidaturaId=1',
      );
      ultimas.flush(ultima ? [ultima] : []);
      await pasan(MARGEN_PINTADO_MS);
    }

    function botonTexto(texto: string): HTMLButtonElement | undefined {
      return [...elemento().querySelectorAll('button')].find((b) => b.textContent?.includes(texto));
    }

    const botonAnalizar = () => botonTexto('Analizar encaje con Claude');
    const tarjeta = () => elemento().querySelector('app-estado-ejecucion');

    async function lanzar(): Promise<void> {
      botonAnalizar()!.click();
      const peticion = http.expectOne('/api/agentes/analizar/1');
      expect(peticion.request.body).toEqual({});
      peticion.flush(analisis());
      await pasan(MARGEN_PINTADO_MS);
    }

    async function seguimiento(id: number, respuesta: EjecucionAgente | null): Promise<void> {
      await pasan(INTERVALO_SEGUIMIENTO_MS);
      const peticion = http.expectOne(`${URL_EJECUCIONES}/${id}`);
      if (respuesta) {
        peticion.flush(respuesta);
      } else {
        peticion.flush({ detail: 'No existe.' }, { status: 404, statusText: 'Not Found' });
      }
      await pasan(MARGEN_PINTADO_MS);
    }

    it('sin agentes disponibles no sale; con ellos va con borde y el único relleno es el del estado', async () => {
      await crear({ estado: 'Enviada' });
      expect(botonAnalizar()).toBeUndefined();

      await conAgentes();

      expect(botonAnalizar()!.hasAttribute('mat-stroked-button')).toBe(true);
      expect(elemento().querySelectorAll('[mat-flat-button]')).toHaveLength(1);
    });

    it.each(['Descartada', 'Cerrada'] as const)('en una %s no sale', async (estado) => {
      await crear({ estado, motivoDescarte: estado === 'Descartada' ? 'Ingles' : null });
      await conAgentes();

      expect(botonAnalizar()).toBeUndefined();
    });

    it('al pulsar, la tarjeta sale en su sitio con el foco y un doble toque no la cancela', async () => {
      await crear({ estado: 'Enviada', empresa: 'Acme' });
      await conAgentes();

      await lanzar();

      expect(botonAnalizar()).toBeUndefined();
      expect(document.activeElement?.getAttribute('aria-label')).toBe(
        'Tarea de Claude: Análisis del encaje · Acme',
      );
      botonTexto('Cancelar la tarea')!.click();
      http.expectNone(`${URL_EJECUCIONES}/5/cancelar`);
    });

    it('al terminar en Ok recarga la ficha, avisa con el encaje y vuelve el botón con el foco', async () => {
      await crear({ estado: 'Enviada' });
      await conAgentes();
      await lanzar();

      await seguimiento(5, analisis({ estado: 'Ok', resumen: 'Encaje medio. Sin pega.' }));
      http
        .expectOne('/api/candidaturas/1')
        .flush(crearDetalle({ id: 1, estado: 'Enviada', encaje: 'Medio' }));
      await pasan(MARGEN_PINTADO_MS);

      expect(avisos.exito).toHaveBeenCalledWith('Encaje actualizado: Medio.');
      expect(elemento().textContent).toContain('Encaje medio. Sin pega.');
      expect(document.activeElement).toBe(botonAnalizar());
    });

    it('con la oferta cerrada no recarga ni descarta: propone descartar con el motivo ya elegido', async () => {
      await crear({ estado: 'Enviada' });
      await conAgentes();
      await lanzar();

      await seguimiento(5, analisis({ estado: 'Ok', estadoOferta: 'Cerrada' }));

      http.expectNone('/api/candidaturas/1');
      expect(avisos.exito).toHaveBeenCalledWith(
        'La oferta ya no está publicada: no se ha analizado nada.',
      );
      expect(botonAnalizar()).toBeUndefined();
      const descartar = botonTexto('Descartar: oferta cerrada')!;
      expect(descartar.classList).toContain('boton-peligro-borde');
      expect(document.activeElement).toBe(descartar);

      descartar.click();
      expect(abrirDialogo.mock.calls[0][1].data).toMatchObject({
        destino: 'Descartada',
        motivo: 'Cerrada',
      });
      http.expectNone('/api/candidaturas/1/estado');
    });

    it('al entrar retoma la que sigue en marcha', async () => {
      await crear({ estado: 'Enviada' });
      await conAgentes(analisis({ estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' }));

      expect(tarjeta()).not.toBeNull();
      expect(botonAnalizar()).toBeUndefined();
    });

    it('al entrar, una ya terminada no sale ni avisa', async () => {
      await crear({ estado: 'Enviada' });
      await conAgentes(analisis({ estado: 'Ok' }));

      expect(tarjeta()).toBeNull();
      expect(botonAnalizar()).toBeDefined();
      expect(avisos.exito).not.toHaveBeenCalled();
    });

    it('si la API ya no la encuentra, vuelve el botón de lanzar', async () => {
      await crear({ estado: 'Enviada' });
      await conAgentes();
      await lanzar();

      await seguimiento(5, null);

      expect(botonAnalizar()).toBeDefined();
    });

    it('tras reintentar desde la tarjeta sigue a la nueva hasta el final', async () => {
      await crear({ estado: 'Enviada' });
      await conAgentes();
      await lanzar();
      await seguimiento(5, analisis({ estado: 'Error', error: 'Se interrumpió.' }));
      expect(botonAnalizar()).toBeUndefined();

      botonTexto('Reintentar')!.click();
      http.expectOne(`${URL_EJECUCIONES}/5/reintentar`).flush(analisis({ id: 6 }));
      await seguimiento(6, analisis({ id: 6, estado: 'Ok' }));

      http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1, encaje: 'Alto' }));
      await pasan(MARGEN_PINTADO_MS);
      expect(avisos.exito).toHaveBeenCalledWith('Encaje actualizado: Alto.');
    });
  });
});
