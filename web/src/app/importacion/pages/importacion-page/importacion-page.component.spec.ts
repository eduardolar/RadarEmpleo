import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AvisosService } from '../../../shared/services/avisos.service';
import { FilaVistaPrevia, VistaPreviaImportacion } from '../../interfaces/importacion.interface';
import {
  crearFila,
  crearFilaLote,
  crearResultado,
  crearVista,
} from '../../utils/importacion-de-prueba';
import {
  AVISO_RADAR,
  EXPLICACION_PARA_REVISAR,
  EXPLICACION_SIN_LEER,
  EXPLICACION_YA_EXISTEN,
} from '../../utils/resumen-importacion';
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

  async function crear(): Promise<void> {
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
  }

  afterEach(() => http.verify());

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

      expect(elemento().querySelector('h1')?.textContent).toContain('Traer del registro');
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

    it('«Volver a leer» pide otra vez la vista previa, por si se ha tocado el archivo', async () => {
      await abrir(AL_DIA);
      expect(estado()).not.toContain('Abajo tienes');

      boton('Volver a leer', elemento().querySelector('[role="status"]')!)!.click();
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
        b.textContent?.includes('Volver a leer'),
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

    it('mientras guarda, «Volver a leer» no borra la vista previa que se está guardando', async () => {
      await abrirCon([NUEVA]);
      await pulsarAplicar();

      const volverALeer = boton('Volver a leer')!;
      expect(volverALeer.getAttribute('aria-disabled')).toBe('true');
      volverALeer.click();
      http.expectNone(URL_PREVISUALIZAR);

      http.expectOne(URL_APLICAR).flush(crearResultado());
      await responderVista(AL_DIA);
    });

    it('mientras guarda, tampoco el «Volver a leer» de las líneas sin leer', async () => {
      await abrir(crearVista([NUEVA], { sinLeer: [{ linea: 12, motivo: 'Falta la empresa.' }] }));
      await pulsarAplicar();

      const volverALeer = boton('Volver a leer', grupo('No se han podido leer'))!;
      expect(volverALeer.getAttribute('aria-disabled')).toBe('true');
      volverALeer.click();
      http.expectNone(URL_PREVISUALIZAR);

      http.expectOne(URL_APLICAR).flush(crearResultado());
      await responderVista(AL_DIA);
    });

    it('«Volver a leer» tras aplicar quita el resultado anterior: el título es de la lectura nueva', async () => {
      const conflicto = crearFila({ clave: 'c', resultado: 'Conflicto' });
      await abrirCon([NUEVA, conflicto]);
      await pulsarAplicar();
      http.expectOne(URL_APLICAR).flush(crearResultado({ creadas: 1, paraRevisar: 1 }));
      await responderVista(crearVista([conflicto]));
      expect(estado()).toContain('Importación hecha');

      boton('Volver a leer', elemento().querySelector('[role="status"]')!)!.click();
      await responderVista(crearVista([conflicto]));

      expect(estado()).toContain('No queda nada por añadir');
      expect(estado()).not.toContain('Importación hecha');
      expect(estado()).not.toContain('Se ha añadido');
      expect(enlace('Ver el tablero')).toBeUndefined();
    });

    it('desde las líneas sin leer se puede volver a leer el archivo', async () => {
      await abrir(crearVista([], { sinLeer: [{ linea: 12, motivo: 'Falta la empresa.' }] }));

      boton('Volver a leer', grupo('No se han podido leer'))!.click();
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

    it('no enseña el enlace del lote: aún no es el definitivo', async () => {
      const vista = crearVista([NUEVA]);
      await abrir({
        ...vista,
        lote: { ...vista.lote, filas: [crearFilaLote({ enlace: 'www.ejemplo.com/oferta' })] },
      });

      expect(elemento().textContent).not.toContain('www.ejemplo.com');
    });

    it('avisa de los envíos escritos como texto', async () => {
      const vista = crearVista([NUEVA]);
      await abrir({ ...vista, resumen: { ...vista.resumen, enviosEnTexto: 41 } });

      expect(elemento().textContent).toContain(
        'Hay unos 41 envíos escritos como texto; esos se podrán traer más adelante.',
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
      expect(peticion.request.body.lote).toBe(vista.lote);
      expect(peticion.request.body.claves).toEqual(['nueva', 'avanza']);
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
      const detail =
        'Estas candidaturas ya se han importado desde otra pestaña. Vuelvo a leer el registro.';
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
});
