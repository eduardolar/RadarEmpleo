import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { EjecucionAgente } from '../../../agentes/interfaces/ejecucion.interface';
import { INTERVALO_SEGUIMIENTO_MS } from '../../../agentes/services/agentes.service';
import { crearEjecucion, crearEstadoAgentes } from '../../../agentes/utils/ejecucion-de-prueba';
import { crearSistemaInfo } from '../../../core/utils/sistema-de-prueba';
import { AvisosService } from '../../../shared/services/avisos.service';
import { FilaVistaPrevia, VistaPreviaImportacion } from '../../interfaces/importacion.interface';
import { crearFila, crearResultado, crearVista } from '../../utils/importacion-de-prueba';
import {
  AVISO_RADAR,
  EXPLICACION_PARA_REVISAR,
  EXPLICACION_SIN_LEER,
  EXPLICACION_YA_EXISTEN,
} from '../../utils/resumen-importacion';
import { ImportacionService } from '../../services/importacion.service';
import ImportacionPageComponent from './importacion-page.component';

const URL_PREVISUALIZAR = '/api/importacion/tabla/previsualizar';
const URL_APLICAR = '/api/importacion/lote/aplicar';

describe('ImportacionPageComponent', () => {
  let fixture: ComponentFixture<ImportacionPageComponent>;
  let http: HttpTestingController;
  let avisos: { exito: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  const NUEVA = crearFila({ clave: 'nueva', resultado: 'Nueva' });
  const AVANZA = crearFila({
    clave: 'avanza',
    resultado: 'AvanzaEstado',
    estadoRegistro: 'Entrevista',
    estadoActual: 'Enviada',
  });
  const AL_DIA = crearVista([crearFila({ clave: 'existe', resultado: 'YaExiste' })]);

  /** Con `conAgentes`, /api/sistema/info se queda sin responder: lo responde `conAgentes()` */
  async function crear(conAgentes = false): Promise<void> {
    avisos = { exito: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      imports: [ImportacionPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AvisosService, useValue: avisos },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ImportacionPageComponent);
    await fixture.whenStable();
    if (!conAgentes) {
      http.expectOne('/api/sistema/info').flush(crearSistemaInfo());
    }
  }

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function responderVista(vista: VistaPreviaImportacion): Promise<void> {
    http.expectOne(URL_PREVISUALIZAR).flush(vista);
    await fixture.whenStable();
  }

  async function abrir(vista: VistaPreviaImportacion): Promise<void> {
    await crear();
    await responderVista(vista);
  }

  async function abrirCon(filas: FilaVistaPrevia[]): Promise<void> {
    await abrir(crearVista(filas));
  }

  function botonesAplicar(): HTMLButtonElement[] {
    return [...elemento().querySelectorAll('button')].filter((b) =>
      /Añadir|Actualizar|Guardando/.test(b.textContent ?? ''),
    );
  }

  function botonAplicar(): HTMLButtonElement | undefined {
    return botonesAplicar()[0];
  }

  function boton(texto: string, dentro: ParentNode = elemento()): HTMLButtonElement | undefined {
    return [...dentro.querySelectorAll('button')].find((b) => b.textContent?.includes(texto));
  }

  function enlace(texto: string): HTMLAnchorElement | undefined {
    return [...elemento().querySelectorAll('a')].find((a) => a.textContent?.includes(texto));
  }

  function seccion(titulo: string): HTMLElement | undefined {
    return [...elemento().querySelectorAll<HTMLElement>('section section')].find((s) =>
      s.querySelector('h2')?.textContent?.includes(titulo),
    );
  }

  function estado(): string {
    return elemento().querySelector('[role="status"]')?.textContent ?? '';
  }

  function grupo(titulo: string): HTMLDetailsElement | undefined {
    return [...elemento().querySelectorAll('details')].find((d) =>
      d.querySelector('summary')?.textContent?.includes(titulo),
    );
  }

  async function pulsarAplicar(): Promise<void> {
    botonAplicar()!.click();
    await fixture.whenStable();
  }

  describe('al abrir', () => {
    it('lee el registro sin que haya que pulsar nada y mientras tanto no ofrece el botón', async () => {
      await crear();

      expect(elemento().querySelector('mat-progress-bar')).not.toBeNull();
      expect(estado()).toContain('Leyendo tu registro...');
      expect(botonAplicar()).toBeUndefined();
      expect(estado()).not.toContain('Todo al día');

      await responderVista(crearVista([NUEVA]));

      expect(elemento().querySelector('mat-progress-bar')).toBeNull();
      expect(estado()).not.toContain('Leyendo');
    });

    it('enseña el título y que el archivo no se cambia', async () => {
      await abrirCon([]);

      expect(elemento().querySelector('h1')?.textContent).toContain('Importar del registro');
      expect(elemento().textContent).toContain('ofertas-enviadas.md');
      expect(elemento().textContent).toContain('El archivo no se cambia');
    });

    it('tiene un enlace para volver a Más', async () => {
      await abrirCon([]);

      expect(enlace('Volver a Más')?.getAttribute('href')).toBe('/mas');
    });
  });

  describe('el botón', () => {
    it('va en plural con varias nuevas', async () => {
      await abrirCon([NUEVA, { ...NUEVA, clave: 'otra' }]);

      expect(botonAplicar()?.textContent).toContain('Añadir 2 candidaturas');
    });

    it('va en singular con una', async () => {
      await abrirCon([NUEVA]);

      expect(botonAplicar()?.textContent).toContain('Añadir 1 candidatura');
      expect(botonAplicar()?.textContent).not.toContain('candidaturas');
    });

    it('dice «actualizar» si solo cambian de estado', async () => {
      await abrirCon([AVANZA]);

      expect(botonAplicar()?.textContent).toContain('Actualizar 1 candidatura');
    });

    it('dice las dos cosas si hay nuevas y cambios de estado', async () => {
      await abrirCon([NUEVA, AVANZA]);

      expect(botonAplicar()?.textContent).toContain('Añadir 1 candidatura y actualizar 1');
    });

    it('con pocas filas sale solo arriba y con muchas se repite al final de la lista', async () => {
      const filas = (total: number) =>
        Array.from({ length: total }, (_, i) => ({ ...NUEVA, clave: `n${i}` }));

      await abrirCon(filas(3));
      expect(botonesAplicar()).toHaveLength(1);
      fixture.destroy();

      fixture = TestBed.createComponent(ImportacionPageComponent);
      await fixture.whenStable();
      await responderVista(crearVista(filas(4)));
      expect(botonesAplicar()).toHaveLength(2);
      expect(botonesAplicar()[1].textContent).toContain('Añadir 4 candidaturas');
    });

    it('el de abajo hace lo mismo que el de arriba', async () => {
      const filas = Array.from({ length: 4 }, (_, i) => ({ ...NUEVA, clave: `n${i}` }));
      await abrirCon(filas);

      botonesAplicar()[1].click();
      await fixture.whenStable();

      const peticion = http.expectOne(URL_APLICAR);
      expect(peticion.request.body.claves).toEqual(['n0', 'n1', 'n2', 'n3']);
      botonesAplicar().forEach((b) => expect(b.getAttribute('aria-disabled')).toBe('true'));
      peticion.flush(crearResultado({ creadas: 4 }));
      await responderVista(AL_DIA);
    });

    it('se desactiva mientras guarda y un doble clic manda un solo POST', async () => {
      await abrirCon([NUEVA]);

      await pulsarAplicar();

      expect(botonAplicar()?.getAttribute('aria-disabled')).toBe('true');
      expect(botonAplicar()?.textContent).toContain('Guardando...');

      await pulsarAplicar();

      http.expectOne(URL_APLICAR).flush(crearResultado());
      await responderVista(AL_DIA);
    });
  });

  describe('sin nada que hacer', () => {
    it('dice «Todo al día» y no hay botón', async () => {
      await abrir(AL_DIA);

      expect(estado()).toContain('Todo al día');
      expect(botonAplicar()).toBeUndefined();
    });

    it('con conflictos no está «al día»: no queda nada por añadir, pero sí que revisar', async () => {
      await abrirCon([
        crearFila({
          resultado: 'Conflicto',
          motivo: 'Ya tienes una parecida',
          estadoActual: 'Enviada',
        }),
      ]);

      expect(estado()).toContain('No queda nada por añadir');
      expect(estado()).not.toContain('Todo al día');
      expect(estado()).toContain('Abajo tienes las que conviene revisar.');
      expect(botonAplicar()).toBeUndefined();
      expect(grupo('Para revisar')?.textContent).toContain('Ya tienes una parecida');
    });

    it('con líneas sin leer tampoco está «al día»', async () => {
      await abrir(
        crearVista([crearFila({ resultado: 'YaExiste' })], {
          sinLeer: [{ linea: 12, motivo: 'Falta la empresa.' }],
        }),
      );

      expect(estado()).toContain('No queda nada por añadir');
      expect(estado()).not.toContain('Todo al día');
    });

    it('«Leer el archivo otra vez» pide otra vez la vista previa, por si se ha tocado el archivo', async () => {
      await abrir(AL_DIA);
      expect(estado()).not.toContain('Abajo tienes');

      boton('Leer el archivo otra vez', elemento().querySelector('[role="status"]')!)!.click();
      await responderVista(crearVista([NUEVA]));

      expect(botonAplicar()?.textContent).toContain('Añadir 1 candidatura');
      expect(document.activeElement).toBe(elemento().querySelector('section'));
    });

    it('también con la tabla vacía', async () => {
      await abrirCon([]);

      expect(estado()).toContain('Todo al día');
      expect(elemento().querySelectorAll('details')).toHaveLength(0);
    });
  });

  describe('la vista previa', () => {
    it('enseña el resumen y las filas que se van a guardar, con la fecha del registro', async () => {
      await abrirCon([NUEVA, AVANZA, crearFila({ clave: 'existe', resultado: 'YaExiste' })]);

      const texto = elemento().textContent;
      expect(texto).toContain('1 nueva');
      expect(texto).toContain('1 cambia de estado');
      expect(texto).toContain('1 ya la tienes');
      expect(texto).toContain('3 ago 2026');
      expect(texto).toContain('De Enviada a Entrevista');
    });

    it('separa las que se añaden de las que cambian de estado, con su total', async () => {
      await abrirCon([NUEVA, { ...NUEVA, clave: 'otra' }, AVANZA]);

      expect(seccion('Se van a añadir (2)')?.querySelectorAll('li')).toHaveLength(2);
      const avanzan = seccion('Cambian de estado (1)');
      expect(avanzan?.querySelectorAll('li')).toHaveLength(1);
      expect(avanzan?.textContent).toContain('No se borra nada.');
    });

    it('sin cambios de estado no sale esa sección', async () => {
      await abrirCon([NUEVA]);

      expect(seccion('Se van a añadir')).toBeDefined();
      expect(seccion('Cambian de estado')).toBeUndefined();
    });

    it('enlaza con la candidatura que ya está en la app', async () => {
      await abrirCon([
        NUEVA,
        crearFila({ clave: 'existe', resultado: 'YaExiste', candidaturaId: 7 }),
      ]);

      const enlaces = [...elemento().querySelectorAll('li a')];
      expect(enlaces).toHaveLength(1);
      expect(enlaces[0].getAttribute('href')).toBe('/candidaturas/7');
      expect(enlaces[0].getAttribute('aria-label')).toBe(
        'Ver la que tienes: Desarrollador Angular en Acme',
      );
    });

    it('cada grupo dice qué pasa con sus filas', async () => {
      await abrir(
        crearVista(
          [
            crearFila({ clave: 'a', resultado: 'YaExiste' }),
            crearFila({ clave: 'b', resultado: 'Conflicto' }),
          ],
          { sinLeer: [{ linea: 12, motivo: 'La fila tiene 3 celdas y la cabecera 4.' }] },
        ),
      );

      expect(grupo('Ya las tienes')?.textContent).toContain(EXPLICACION_YA_EXISTEN);
      expect(grupo('Para revisar')?.textContent).toContain(EXPLICACION_PARA_REVISAR);
      expect(grupo('No se han podido leer')?.textContent).toContain(EXPLICACION_SIN_LEER);
    });

    it('con filas que guardar también se puede volver a leer, sin abrir ningún grupo', async () => {
      await abrirCon([NUEVA]);

      const volverALeer = [...elemento().querySelectorAll('button')].filter((b) =>
        b.textContent?.includes('Leer el archivo otra vez'),
      );
      expect(volverALeer).toHaveLength(1);
      expect(volverALeer[0].closest('details')).toBeNull();
      expect(volverALeer[0].getAttribute('mat-stroked-button')).not.toBeNull();
      expect(volverALeer[0].className).toContain('[--mat-button-outlined-container-height:48px]');

      volverALeer[0].click();
      await responderVista(crearVista([NUEVA, { ...NUEVA, clave: 'otra' }]));

      expect(botonAplicar()?.textContent).toContain('Añadir 2 candidaturas');
      expect(document.activeElement).toBe(elemento().querySelector('section'));
    });

    it('mientras guarda, «Leer el archivo otra vez» no borra la vista previa que se está guardando', async () => {
      await abrirCon([NUEVA]);
      await pulsarAplicar();

      const volverALeer = boton('Leer el archivo otra vez')!;
      expect(volverALeer.getAttribute('aria-disabled')).toBe('true');
      volverALeer.click();
      http.expectNone(URL_PREVISUALIZAR);

      http.expectOne(URL_APLICAR).flush(crearResultado());
      await responderVista(AL_DIA);
    });

    it('mientras guarda, tampoco el «Leer el archivo otra vez» de las líneas sin leer', async () => {
      await abrir(crearVista([NUEVA], { sinLeer: [{ linea: 12, motivo: 'Falta la empresa.' }] }));
      await pulsarAplicar();

      const volverALeer = boton('Leer el archivo otra vez', grupo('No se han podido leer'))!;
      expect(volverALeer.getAttribute('aria-disabled')).toBe('true');
      volverALeer.click();
      http.expectNone(URL_PREVISUALIZAR);

      http.expectOne(URL_APLICAR).flush(crearResultado());
      await responderVista(AL_DIA);
    });

    it('«Leer el archivo otra vez» tras aplicar quita el resultado anterior: el título es de la lectura nueva', async () => {
      const conflicto = crearFila({ clave: 'c', resultado: 'Conflicto' });
      await abrirCon([NUEVA, conflicto]);
      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush(crearResultado({ creadas: 1, paraRevisar: 1 }));
      await responderVista(crearVista([conflicto]));
      expect(estado()).toContain('Importación hecha');

      boton('Leer el archivo otra vez', elemento().querySelector('[role="status"]')!)!.click();
      await responderVista(crearVista([conflicto]));

      expect(estado()).toContain('No queda nada por añadir');
      expect(estado()).not.toContain('Importación hecha');
      expect(estado()).not.toContain('Se ha añadido');
      expect(enlace('Ver el tablero')).toBeUndefined();
    });

    it('desde las líneas sin leer se puede volver a leer el archivo', async () => {
      await abrir(crearVista([], { sinLeer: [{ linea: 12, motivo: 'Falta la empresa.' }] }));

      boton('Leer el archivo otra vez', grupo('No se han podido leer'))!.click();
      await responderVista(AL_DIA);

      expect(grupo('No se han podido leer')).toBeUndefined();
    });

    it('los grupos salen plegados y con su total', async () => {
      await abrir(
        crearVista(
          [
            crearFila({ clave: 'a', resultado: 'YaExiste' }),
            crearFila({ clave: 'b', resultado: 'Conflicto' }),
          ],
          { sinLeer: [{ linea: 12, motivo: 'La fila tiene 3 celdas y la cabecera 4.' }] },
        ),
      );

      const grupos = ['Ya las tienes (1)', 'Para revisar (1)', 'No se han podido leer (1)'];
      for (const titulo of grupos) {
        expect(grupo(titulo)?.open).toBe(false);
      }
      expect(grupo('No se han podido leer')?.textContent).toContain(
        'Línea 12: La fila tiene 3 celdas y la cabecera 4.',
      );
    });

    it('enseña dos filas sin leer con la misma línea', async () => {
      await abrir(
        crearVista([], {
          sinLeer: [
            { linea: 0, motivo: 'No encuentro esta línea en el registro: «Uno».' },
            { linea: 0, motivo: 'No encuentro esta línea en el registro: «Dos».' },
          ],
        }),
      );

      const filas = grupo('No se han podido leer (2)')?.querySelectorAll('li');
      expect(filas).toHaveLength(2);
      expect(filas?.[1].textContent).toContain('«Dos»');
    });

    it('una fila repetida en el registro sale dos veces aunque comparta clave', async () => {
      await abrirCon([
        crearFila({ clave: 'misma', resultado: 'YaExiste', puesto: 'Primera' }),
        crearFila({ clave: 'misma', resultado: 'YaExiste', puesto: 'Segunda' }),
      ]);

      expect(grupo('Ya las tienes')?.querySelectorAll('li')).toHaveLength(2);
    });

    it('enseña los avisos de cada fila tal como llegan', async () => {
      await abrirCon([
        crearFila({
          avisos: ['La fecha es aproximada.', 'El enlace no es válido: se importará sin enlace.'],
        }),
      ]);

      expect(elemento().textContent).toContain('La fecha es aproximada.');
      expect(elemento().textContent).toContain('El enlace no es válido: se importará sin enlace.');
    });

    it('avisa de los envíos escritos como texto', async () => {
      const vista = crearVista([NUEVA]);
      await abrir({ ...vista, resumen: { ...vista.resumen, enviosEnTexto: 41 } });

      expect(elemento().textContent).toContain(
        'Hay unos 41 envíos escritos como texto; esos se podrán importar más adelante.',
      );
    });
  });

  describe('si la vista previa falla', () => {
    const DETALLE = 'No se encuentra la carpeta Curriculum.';

    async function abrirConError(): Promise<void> {
      await crear();
      http
        .expectOne(URL_PREVISUALIZAR)
        .flush({ detail: DETALLE }, { status: 409, statusText: 'Conflict' });
      await fixture.whenStable();
    }

    it('enseña el mensaje de la API tal cual y ni botón ni «Todo al día»', async () => {
      await abrirConError();

      expect(elemento().querySelector('[role="alert"]')?.textContent).toContain(DETALLE);
      expect(botonAplicar()).toBeUndefined();
      expect(estado()).not.toContain('Todo al día');
    });

    it('«Reintentar» vuelve a leer el registro sin perder el foco', async () => {
      await abrirConError();

      [...elemento().querySelectorAll('button')]
        .find((b) => b.textContent?.includes('Reintentar'))!
        .click();
      await responderVista(crearVista([NUEVA]));

      expect(elemento().querySelector('[role="alert"]')).toBeNull();
      expect(botonAplicar()).toBeDefined();
      expect(document.activeElement).toBe(elemento().querySelector('section'));
    });
  });

  describe('al aplicar', () => {
    it('manda solo lo aplicable, enseña el resultado y vuelve a pedir la vista previa', async () => {
      const vista = crearVista([NUEVA, AVANZA, crearFila({ clave: 'c', resultado: 'Conflicto' })]);
      await abrir(vista);

      await pulsarAplicar();

      const peticion = http.expectOne(URL_APLICAR);
      expect(peticion.request.body).toStrictEqual({
        origen: 'Tabla',
        ejecucionId: null,
        hashRegistro: vista.hashRegistro,
        claves: ['nueva', 'avanza'],
      });
      peticion.flush(crearResultado({ creadas: 1, avanzadas: 1, paraRevisar: 1 }));
      await fixture.whenStable();

      expect(estado()).toContain('Se ha añadido 1 candidatura y se ha actualizado 1.');
      expect(estado()).toContain('Queda 1 para revisar.');
      expect(botonAplicar()).toBeUndefined();

      await responderVista(AL_DIA);

      expect(estado()).toContain('Todo al día');
      expect(estado()).toContain('Se ha añadido 1 candidatura');
      expect(botonAplicar()).toBeUndefined();
      expect(enlace('Ver el tablero')?.getAttribute('href')).toBe('/tablero');
    });

    it('si quedan filas para revisar, dice «Importación hecha» y no «Todo al día»', async () => {
      await abrirCon([NUEVA, crearFila({ clave: 'c', resultado: 'Conflicto' })]);

      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush(crearResultado({ creadas: 1, paraRevisar: 1 }));
      await responderVista(crearVista([crearFila({ clave: 'c', resultado: 'Conflicto' })]));

      expect(estado()).toContain('Importación hecha');
      expect(estado()).not.toContain('Todo al día');
      expect(estado()).toContain('Se ha añadido 1 candidatura. Queda 1 para revisar.');
      expect(grupo('Para revisar (1)')).toBeDefined();
    });

    it('el foco no se pierde en <body> cuando desaparece el botón', async () => {
      await abrirCon([NUEVA]);
      botonAplicar()!.focus();

      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush(crearResultado());
      await fixture.whenStable();

      expect(document.activeElement).toBe(elemento().querySelector('section'));
      await responderVista(AL_DIA);
    });

    it('si otra aplicación se ha adelantado (200 con 0) no parece un fallo', async () => {
      await abrirCon([NUEVA]);

      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush(crearResultado({ creadas: 0, sinCambios: 1 }));
      await responderVista(AL_DIA);

      expect(estado()).toContain('No había nada nuevo que añadir.');
      expect(estado()).toContain('Todo al día');
      expect(enlace('Ver el tablero')).toBeUndefined();
      expect(avisos.error).not.toHaveBeenCalled();
    });

    it('con radarActualizado false da un aviso suave, no un error', async () => {
      await abrirCon([NUEVA]);

      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush(crearResultado({ radarActualizado: false }));
      await responderVista(AL_DIA);

      expect(estado()).toContain('Se ha añadido 1 candidatura.');
      expect(estado()).toContain(AVISO_RADAR);
      expect(elemento().querySelector('[role="alert"]')).toBeNull();
      expect(avisos.error).not.toHaveBeenCalled();
    });

    it('con el radar actualizado no dice nada de la copia', async () => {
      await abrirCon([NUEVA]);

      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush(crearResultado());
      await responderVista(AL_DIA);

      expect(estado()).not.toContain(AVISO_RADAR);
    });

    it('un 400 enseña el detail y deja volver a probar', async () => {
      const detail = 'El puesto no puede tener más de 300 caracteres.';
      await abrirCon([NUEVA]);

      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush({ detail }, { status: 400, statusText: 'Error' });
      await fixture.whenStable();

      expect(avisos.error).toHaveBeenCalledExactlyOnceWith(detail);
      expect(botonAplicar()?.getAttribute('aria-disabled')).not.toBe('true');
      expect(botonAplicar()?.textContent).toContain('Añadir 1 candidatura');
      expect(estado()).toBe('');
      http.expectNone(URL_PREVISUALIZAR);
    });

    it('un 409 enseña el detail y vuelve a leer el registro solo, sin quitar el aviso', async () => {
      const detail = 'El registro ha cambiado desde que lo leíste. Vuelvo a leerlo.';
      await abrirCon([NUEVA]);

      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush({ detail }, { status: 409, statusText: 'Conflict' });
      await fixture.whenStable();

      expect(avisos.error).toHaveBeenCalledExactlyOnceWith(detail);
      expect(botonAplicar()).toBeUndefined();
      expect(estado()).toContain('Leyendo tu registro...');

      await responderVista(AL_DIA);

      expect(estado()).toContain('Todo al día');
      expect(botonAplicar()).toBeUndefined();
      expect(avisos.error).toHaveBeenCalledTimes(1);
      expect(avisos.exito).not.toHaveBeenCalled();
    });

    it('si lo importado se guarda pero falla la lectura posterior, el resultado se queda', async () => {
      await abrirCon([NUEVA]);

      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush(crearResultado());
      http
        .expectOne(URL_PREVISUALIZAR)
        .flush({ detail: 'No se encuentra el registro.' }, { status: 409, statusText: 'Conflict' });
      await fixture.whenStable();

      expect(estado()).toContain('Se ha añadido 1 candidatura.');
      expect(elemento().querySelector('[role="alert"]')?.textContent).toContain(
        'No se encuentra el registro.',
      );
      expect(botonAplicar()).toBeUndefined();
    });

    it('si se sale de la página mientras guarda, se termina y avisa con un aviso general', async () => {
      await abrirCon([NUEVA]);

      await pulsarAplicar();
      fixture.destroy();
      http.expectOne(URL_APLICAR).flush(crearResultado({ creadas: 1 }));

      expect(avisos.exito).toHaveBeenCalledExactlyOnceWith('Se ha añadido 1 candidatura.');
      http.expectOne(URL_PREVISUALIZAR).flush(AL_DIA);
    });

    it('al volver a la página no sigue enseñando el resultado anterior', async () => {
      await abrirCon([NUEVA]);
      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush(crearResultado());
      await responderVista(AL_DIA);

      fixture.destroy();
      fixture = TestBed.createComponent(ImportacionPageComponent);
      await fixture.whenStable();
      await responderVista(AL_DIA);

      expect(estado()).not.toContain('Se ha añadido');
      expect(estado()).toContain('Todo al día');
    });
  });

  describe('extraer el texto libre con Claude', () => {
    const URL_EJECUCIONES = '/api/agentes/ejecuciones';
    const URL_EXTRAER = '/api/agentes/extraer-registro';
    const urlCombinada = (id: number) => `/api/importacion/extraccion/${id}/previsualizar`;
    const AHORA = new Date('2026-11-23T10:01:20Z');
    /** Un temporizador que se programa dentro de otro corre 1 ms después: así llega el pintado */
    const MARGEN_PINTADO_MS = 1;
    const REVISA =
      'Revisa la empresa: Claude no la ve escrita en esa línea. Si está mal, corrígela en tu archivo antes de añadir.';
    const NO_VALE =
      'Tu archivo ha cambiado y lo que leyó Claude ya no vale. Vuelve a extraer el texto libre si lo necesitas.';
    const LINEA = 'Tabla + 1 leída del texto libre con Claude · 1 parte no se ha podido leer';

    const extraccion = (cambios: Partial<EjecucionAgente> = {}) =>
      crearEjecucion({
        id: 7,
        tarea: 'ExtraerRegistro',
        candidaturaId: null,
        candidatura: null,
        ...cambios,
      });

    function conResumen(
      vista: VistaPreviaImportacion,
      cambios: Partial<VistaPreviaImportacion['resumen']>,
    ): VistaPreviaImportacion {
      return { ...vista, resumen: { ...vista.resumen, ...cambios } };
    }

    const TABLA = conResumen(crearVista([NUEVA]), { enviosEnTexto: 3 });
    const COMBINADA = conResumen(
      crearVista(
        [
          NUEVA,
          crearFila({ clave: 'gamma', empresa: 'Gamma', avisos: [REVISA], delTextoLibre: true }),
        ],
        {
          origen: 'RegistroConClaude',
          ejecucionId: 7,
          fuera: [
            {
              linea: 12,
              empresa: 'Acme',
              puesto: 'Backend',
              fecha: '2026-09-12',
              aviso: 'Ya está en la tabla del registro',
            },
          ],
          dudas: ['<b>¿Gamma o Gama?</b>'],
          partesPerdidas: 1,
        },
      ),
      { delTextoLibre: 1 },
    );

    /** Con reloj simulado no se puede usar whenStable: el pintado espera también a un temporizador */
    async function pasan(ms: number): Promise<void> {
      await vi.advanceTimersByTimeAsync(ms);
    }

    /** Como al entrar en frío (F5): la tabla, /api/sistema/info, el estado y la última extracción */
    async function conAgentes(ultima: EjecucionAgente | null = null, vista = TABLA): Promise<void> {
      await crear(true);
      vi.useFakeTimers();
      vi.setSystemTime(AHORA);
      http.expectOne(URL_PREVISUALIZAR).flush(vista);
      http.expectOne('/api/sistema/info').flush(crearSistemaInfo({ agentesDisponibles: true }));
      await pasan(0);
      http.expectOne('/api/agentes/estado').flush(crearEstadoAgentes());
      const ultimas = http.expectOne((r) => r.url === URL_EJECUCIONES);
      expect(ultimas.request.params.toString()).toBe('tarea=ExtraerRegistro&limite=1');
      ultimas.flush(ultima ? [ultima] : []);
      await pasan(MARGEN_PINTADO_MS);
    }

    const botonExtraer = () => boton('Extraer el texto libre con Claude');
    const botonLeido = () => boton('Ver lo que ha leído');
    const tarjeta = () => elemento().querySelector('app-estado-ejecucion');

    async function lanzar(): Promise<void> {
      botonExtraer()!.click();
      const peticion = http.expectOne(URL_EXTRAER);
      expect(peticion.request.body).toEqual({});
      peticion.flush(extraccion());
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

    async function responder(url: string, vista: VistaPreviaImportacion): Promise<void> {
      const peticion = http.expectOne(url);
      expect(peticion.request.method).toBe('POST');
      peticion.flush(vista);
      await pasan(MARGEN_PINTADO_MS);
    }

    async function noVale(url: string): Promise<void> {
      http.expectOne(url).flush({ detail: NO_VALE }, { status: 409, statusText: 'Conflict' });
      await pasan(0);
    }

    async function terminarBien(): Promise<void> {
      await conAgentes();
      await lanzar();
      await seguimiento(7, extraccion({ estado: 'Ok', fin: '2026-11-23T10:01:00Z' }));
    }

    async function abrirCombinada(): Promise<void> {
      await terminarBien();
      await responder(urlCombinada(7), COMBINADA);
    }

    it('sin agentes disponibles no sale y el aviso sigue diciendo «más adelante»', async () => {
      await abrir(TABLA);

      expect(botonExtraer()).toBeUndefined();
      expect(elemento().textContent).toContain('se podrán importar más adelante');
    });

    it('con agentes va con borde, dice que solo lee y lo que gasta, y el único relleno es «Aplicar»', async () => {
      await conAgentes();

      expect(botonExtraer()!.hasAttribute('mat-stroked-button')).toBe(true);
      const gasto = elemento().querySelector(
        `#${botonExtraer()!.getAttribute('aria-describedby')}`,
      );
      expect(gasto?.textContent?.trim()).toBe(
        'Claude solo lee: verás lo que encuentre antes de añadir nada. Gasta un poco de tu tope diario de Claude.',
      );
      expect(elemento().textContent).toContain('fuera de la tabla: Claude puede leerlos.');
      const rellenos = elemento().querySelectorAll('[mat-flat-button]');
      expect(rellenos).toHaveLength(1);
      expect(rellenos[0].textContent).toContain('Añadir 1 candidatura');
    });

    it('sin envíos en texto no sale', async () => {
      await conAgentes(null, crearVista([NUEVA]));

      expect(botonExtraer()).toBeUndefined();
    });

    it('al pulsar, la tarjeta sale con el foco, dice que se puede salir y un doble toque no la cancela', async () => {
      await conAgentes();

      await lanzar();

      expect(botonExtraer()).toBeUndefined();
      expect(document.activeElement?.getAttribute('aria-label')).toBe(
        'Tarea de Claude: Lectura del registro',
      );
      expect(elemento().textContent).toContain('Puedes salir: la tarea sigue');
      boton('Cancelar la tarea')!.click();
      http.expectNone(`${URL_EJECUCIONES}/7/cancelar`);
    });

    it('si no se puede lanzar, lo avisa y el botón sigue', async () => {
      await conAgentes();

      botonExtraer()!.click();
      http
        .expectOne(URL_EXTRAER)
        .flush({ detail: 'Se ha llegado al tope.' }, { status: 409, statusText: 'Conflict' });
      await pasan(MARGEN_PINTADO_MS);

      expect(avisos.error).toHaveBeenCalledExactlyOnceWith('Se ha llegado al tope.');
      expect(botonExtraer()).toBeDefined();
      expect(tarjeta()).toBeNull();
    });

    it('al terminar en Ok pide la vista combinada y la enseña con su línea, que se lleva el foco', async () => {
      await terminarBien();

      const peticion = http.expectOne(urlCombinada(7));
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({});
      expect(estado()).toContain('Leyendo tu registro...');
      expect(tarjeta()).toBeNull();
      peticion.flush(COMBINADA);
      await pasan(MARGEN_PINTADO_MS);

      expect(document.activeElement?.textContent?.trim()).toBe(LINEA);
    });

    it('si al salir la vista combinada el foco está en otro sitio, no se mueve', async () => {
      await terminarBien();
      const volver = enlace('Volver a Más')!;
      volver.focus();

      await responder(urlCombinada(7), COMBINADA);

      expect(document.activeElement).toBe(volver);
    });

    it('si termina en Ok mientras guarda, pide la combinada una sola vez y después del aplicar', async () => {
      await conAgentes();
      await lanzar();
      await pulsarAplicarConReloj();

      await seguimiento(7, extraccion({ estado: 'Ok' }));
      http.expectNone(urlCombinada(7));
      expect(botonAplicar()?.textContent).toContain('Guardando...');

      http.expectOne(URL_APLICAR).flush(crearResultado());
      await responder(urlCombinada(7), COMBINADA);

      http.expectNone(URL_PREVISUALIZAR);
      expect(elemento().textContent).toContain(LINEA);
    });

    async function pulsarAplicarConReloj(): Promise<void> {
      botonAplicar()!.click();
      await pasan(MARGEN_PINTADO_MS);
    }

    it('marca las leídas con Claude, con el aviso de la empresa, las de fuera plegadas y las dudas abiertas, y como texto', async () => {
      await abrirCombinada();

      expect(boton('Ver solo la tabla')).toBeDefined();
      const filas = seccion('Se van a añadir (2)')!.querySelectorAll('li');
      expect(filas[0].textContent).not.toContain('Leída con Claude');
      expect(filas[1].textContent).toContain('Leída con Claude');
      expect(filas[1].textContent).toContain(REVISA);
      expect(botonExtraer()).toBeUndefined();
      expect(elemento().querySelectorAll('[mat-flat-button]')).toHaveLength(1);

      const fuera = grupo('Se quedan fuera (1)')!;
      expect(fuera.open).toBe(false);
      expect(fuera.textContent).toContain('No tienes que hacer nada salvo que el motivo esté mal.');
      expect(fuera.querySelector('li')?.textContent?.trim()).toBe(
        'Línea 12 · Acme · Backend · 12 sept 2026: Ya está en la tabla del registro.',
      );
      const dudas = grupo('Dudas de Claude (1)')!;
      expect(dudas.open).toBe(true);
      expect(dudas.querySelector('li')?.textContent?.trim()).toBe('<b>¿Gamma o Gama?</b>');
      expect(dudas.querySelector('b')).toBeNull();
    });

    it('«Aplicar» manda el origen, la ejecución, el hash y las claves, y vuelve a leer la combinada', async () => {
      await abrirCombinada();

      botonAplicar()!.click();
      const peticion = http.expectOne(URL_APLICAR);
      expect(peticion.request.body).toEqual({
        origen: 'RegistroConClaude',
        ejecucionId: 7,
        hashRegistro: 'a'.repeat(64),
        claves: ['nueva', 'gamma'],
      });
      peticion.flush(crearResultado({ creadas: 2 }));
      await responder(urlCombinada(7), conResumen(COMBINADA, { nuevas: 0, yaExisten: 2 }));

      expect(estado()).toContain('Se han añadido 2 candidaturas.');
    });

    it('«Leer el archivo otra vez» en la vista combinada vuelve a pedir la combinada', async () => {
      await abrirCombinada();

      boton('Leer el archivo otra vez')!.click();
      await responder(urlCombinada(7), COMBINADA);

      expect(elemento().textContent).toContain(LINEA);
    });

    it('un 409 al aplicar con la extracción caducada avisa una vez, vuelve a la tabla y lleva el foco al botón', async () => {
      await abrirCombinada();

      botonAplicar()!.click();
      http
        .expectOne(URL_APLICAR)
        .flush({ detail: NO_VALE }, { status: 409, statusText: 'Conflict' });
      await noVale(urlCombinada(7));
      await responder(URL_PREVISUALIZAR, TABLA);

      expect(avisos.error).toHaveBeenCalledExactlyOnceWith(NO_VALE);
      expect(elemento().textContent).not.toContain(LINEA);
      expect(botonLeido()).toBeUndefined();
      expect(document.activeElement).toBe(botonExtraer());
    });

    it('si al pedir la vista combinada la extracción ya no vale, vuelve a la tabla, lo avisa y el foco va al botón', async () => {
      await terminarBien();

      await noVale(urlCombinada(7));
      await responder(URL_PREVISUALIZAR, TABLA);

      expect(avisos.error).toHaveBeenCalledExactlyOnceWith(NO_VALE);
      expect(elemento().querySelector('[role="alert"]')).toBeNull();
      expect(document.activeElement).toBe(botonExtraer());
    });

    it('si sale de la página antes de saber que ya no vale, no avisa', async () => {
      await terminarBien();
      fixture.destroy();

      await noVale(urlCombinada(7));
      http.expectOne(URL_PREVISUALIZAR).flush(TABLA);

      expect(avisos.error).not.toHaveBeenCalled();
    });

    it('sin conexión con la vista combinada ofrece «Reintentar» y «Ver solo la tabla»', async () => {
      await terminarBien();

      http.expectOne(urlCombinada(7)).error(new ProgressEvent('error'));
      await pasan(MARGEN_PINTADO_MS);
      const alerta = elemento().querySelector<HTMLElement>('[role="alert"]')!;
      expect(boton('Reintentar', alerta)).toBeDefined();

      boton('Ver solo la tabla', alerta)!.click();
      await responder(URL_PREVISUALIZAR, TABLA);
      expect(botonLeido()).toBeDefined();
    });

    it('«Ver solo la tabla» lleva el foco al inicio y sigue ofreciendo lo leído', async () => {
      await abrirCombinada();

      boton('Ver solo la tabla')!.click();
      await responder(URL_PREVISUALIZAR, TABLA);

      expect(document.activeElement?.getAttribute('aria-label')).toBe('Importar del registro');
      expect(elemento().textContent).not.toContain(LINEA);
      expect(elemento().textContent).toContain(
        'Claude terminó de leer el texto libre (hace un momento).',
      );
      expect(botonExtraer()).toBeUndefined();

      botonLeido()!.click();
      await responder(urlCombinada(7), COMBINADA);
      expect(elemento().textContent).toContain(LINEA);
    });

    it('mientras ofrece lo leído, «Extraer otra vez» va como texto, con el gasto, y lanza otra', async () => {
      await abrirCombinada();
      boton('Ver solo la tabla')!.click();
      await responder(URL_PREVISUALIZAR, TABLA);

      const otraVez = boton('Extraer otra vez')!;
      expect(otraVez.hasAttribute('mat-button')).toBe(true);
      expect(
        elemento().querySelector(`#${otraVez.getAttribute('aria-describedby')}`)?.textContent,
      ).toContain('Gasta un poco de tu tope diario de Claude.');

      otraVez.click();
      http.expectOne(URL_EXTRAER).flush(extraccion({ id: 9 }));
      await pasan(MARGEN_PINTADO_MS);

      expect(botonLeido()).toBeUndefined();
      expect(tarjeta()).not.toBeNull();
    });

    it('al cerrar la página sin estar guardando deja de apuntar la extracción', async () => {
      await abrirCombinada();

      fixture.destroy();

      expect(TestBed.inject(ImportacionService).extraccionId()).toBeNull();
    });

    it('al entrar retoma la extracción en marcha', async () => {
      await conAgentes(extraccion({ estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' }));

      expect(tarjeta()).not.toBeNull();
      expect(botonExtraer()).toBeUndefined();
    });

    it('al entrar, una de hoy ya terminada que sigue valiendo se ofrece sin cambiar la vista', async () => {
      await conAgentes(extraccion({ estado: 'Ok', fin: '2026-11-23T09:49:00Z' }));

      await responder(urlCombinada(7), COMBINADA);

      expect(tarjeta()).toBeNull();
      expect(elemento().textContent).toContain(
        'Claude terminó de leer el texto libre (hace 12 min).',
      );
      expect(elemento().textContent).not.toContain(LINEA);
      expect(botonExtraer()).toBeUndefined();
      expect(botonLeido()).toBeDefined();
    });

    it('al entrar, una terminada que ya no vale deja el botón de extraer, sin aviso', async () => {
      await conAgentes(extraccion({ estado: 'Ok', fin: '2026-11-23T09:49:00Z' }));

      await noVale(urlCombinada(7));

      expect(botonLeido()).toBeUndefined();
      expect(botonExtraer()).toBeDefined();
      expect(avisos.error).not.toHaveBeenCalled();
    });

    it('al entrar, una de otro día no se ofrece ni se comprueba', async () => {
      await conAgentes(extraccion({ estado: 'Ok', fin: '2026-11-22T18:00:00Z' }));

      http.expectNone(urlCombinada(7));
      expect(botonExtraer()).toBeDefined();
    });

    it('en Error la tarjeta ofrece «Reintentar» y, con la nueva en Ok, pide su vista combinada', async () => {
      await conAgentes();
      await lanzar();
      await seguimiento(7, extraccion({ estado: 'Error', error: 'Se interrumpió.' }));
      expect(botonExtraer()).toBeUndefined();

      boton('Reintentar', tarjeta()!)!.click();
      http.expectOne(`${URL_EJECUCIONES}/7/reintentar`).flush(extraccion({ id: 8 }));
      await seguimiento(8, extraccion({ id: 8, estado: 'Ok' }));

      await responder(urlCombinada(8), { ...COMBINADA, ejecucionId: 8 });
      expect(elemento().textContent).toContain(LINEA);
    });

    it('si la API ya no encuentra la tarea, vuelve el botón', async () => {
      await conAgentes();
      await lanzar();

      await seguimiento(7, null);

      expect(botonExtraer()).toBeDefined();
    });
  });
});
