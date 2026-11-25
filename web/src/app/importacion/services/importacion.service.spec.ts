import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { crearFila, crearResultado, crearVista } from '../utils/importacion-de-prueba';
import { ImportacionService } from './importacion.service';

const URL_PREVISUALIZAR = '/api/importacion/tabla/previsualizar';
const URL_APLICAR = '/api/importacion/lote/aplicar';

describe('ImportacionService', () => {
  let service: ImportacionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ImportacionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function previsualizacion(): ReturnType<HttpTestingController['expectOne']> {
    return http.expectOne(URL_PREVISUALIZAR);
  }

  function cargarVista(vista = crearVista([crearFila()])): void {
    service.previsualizar();
    previsualizacion().flush(vista);
  }

  describe('previsualizar', () => {
    it('hace un POST sin cuerpo y marca «cargando» mientras espera', () => {
      const vista = crearVista([crearFila()]);

      service.previsualizar();

      const peticion = previsualizacion();
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toBeNull();
      expect(service.cargando()).toBe(true);

      peticion.flush(vista);

      expect(service.cargando()).toBe(false);
      expect(service.vistaPrevia()).toEqual(vista);
      expect(service.error()).toBeNull();
    });

    it('un 409 deja el mensaje de la API tal cual', () => {
      service.previsualizar();
      previsualizacion().flush(
        {
          title: 'No se puede hacer esa operación',
          detail: 'No se encuentra la carpeta Curriculum.',
        },
        { status: 409, statusText: 'Conflict' },
      );

      expect(service.cargando()).toBe(false);
      expect(service.error()).toBe('No se encuentra la carpeta Curriculum.');
      expect(service.vistaPrevia()).toBeNull();
    });

    it('si vuelve a fallar no conserva la vista previa anterior: estaría desfasada', () => {
      cargarVista();

      service.previsualizar();
      expect(service.vistaPrevia()).toBeNull();
      previsualizacion().error(new ProgressEvent('error'));

      expect(service.vistaPrevia()).toBeNull();
      expect(service.error()).toContain('No se puede conectar con RadarEmpleo');
    });

    it('cancela la petición anterior', () => {
      service.previsualizar();
      const anterior = previsualizacion();

      service.previsualizar();

      expect(anterior.cancelled).toBe(true);
      previsualizacion().flush(crearVista());
    });
  });

  describe('aplicar', () => {
    const VISTA = crearVista([
      crearFila({ clave: 'nueva', resultado: 'Nueva' }),
      crearFila({ clave: 'avanza', resultado: 'AvanzaEstado', estadoActual: 'Preparada' }),
      crearFila({ clave: 'existe', resultado: 'YaExiste' }),
      crearFila({
        clave: 'nueva',
        resultado: 'Conflicto',
        motivo: 'Aparece dos veces en el registro',
      }),
      crearFila({ clave: 'revisar', resultado: 'Conflicto' }),
    ]);

    it('manda origen, ejecucionId, hashRegistro y claves, sin lote', () => {
      cargarVista({ ...VISTA, hashRegistro: 'b'.repeat(64) });

      service.aplicar().subscribe();

      const peticion = http.expectOne(URL_APLICAR);
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toStrictEqual({
        origen: 'Tabla',
        ejecucionId: null,
        hashRegistro: 'b'.repeat(64),
        claves: ['nueva', 'avanza'],
      });
      peticion.flush(crearResultado());
      previsualizacion().flush(crearVista());
    });

    it('al terminar bien vuelve a pedir la vista previa y entrega el resultado', () => {
      cargarVista(VISTA);
      const resultado = crearResultado({ creadas: 1, avanzadas: 1 });

      let recibido: unknown;
      service.aplicar().subscribe((r) => (recibido = r));
      http.expectOne(URL_APLICAR).flush(resultado);

      expect(recibido).toEqual(resultado);
      expect(service.vistaPrevia()).toBeNull();
      expect(service.cargando()).toBe(true);

      const alDia = crearVista([crearFila({ resultado: 'YaExiste' })]);
      previsualizacion().flush(alDia);

      expect(service.vistaPrevia()).toEqual(alDia);
    });

    it('marca «aplicando» mientras guarda', () => {
      cargarVista(VISTA);

      service.aplicar().subscribe();
      expect(service.aplicando()).toBe(true);

      http.expectOne(URL_APLICAR).flush(crearResultado());
      expect(service.aplicando()).toBe(false);
      previsualizacion().flush(crearVista());
    });

    it('un segundo intento mientras guarda no manda nada', () => {
      cargarVista(VISTA);

      service.aplicar().subscribe();
      service.aplicar().subscribe();

      http.expectOne(URL_APLICAR).flush(crearResultado());
      previsualizacion().flush(crearVista());
    });

    it('nada se manda hasta suscribirse', () => {
      cargarVista(VISTA);

      service.aplicar();

      http.expectNone(URL_APLICAR);
      expect(service.aplicando()).toBe(false);
    });

    it('sin nada que aplicar no manda nada', () => {
      let terminado = false;

      service.aplicar().subscribe({ complete: () => (terminado = true) });
      expect(terminado).toBe(true);

      cargarVista(crearVista([crearFila({ resultado: 'YaExiste' })]));
      service.aplicar().subscribe();

      http.expectNone(URL_APLICAR);
      expect(service.aplicando()).toBe(false);
    });

    it('solo manda las claves elegidas', () => {
      cargarVista(VISTA);

      service.elegir(['avanza'], false);
      service.aplicar().subscribe();
      expect(http.expectOne(URL_APLICAR).request.body.claves).toEqual(['nueva']);
    });

    it('sin ninguna elegida no manda nada', () => {
      cargarVista(VISTA);

      service.elegir(['nueva', 'avanza'], false);
      service.aplicar().subscribe();

      http.expectNone(URL_APLICAR);
      expect(service.aplicando()).toBe(false);
    });

    it('por defecto, las que llevan aviso no van: hay que marcarlas', () => {
      cargarVista(
        crearVista([
          crearFila({ clave: 'nueva' }),
          crearFila({ clave: 'aviso', avisos: ['Revisa la empresa.'] }),
        ]),
      );
      expect([...service.elegidas()]).toEqual(['nueva']);

      service.elegir(['aviso'], true);
      service.aplicar().subscribe();

      expect(http.expectOne(URL_APLICAR).request.body.claves).toEqual(['nueva', 'aviso']);
    });

    it('marcar una clave que no se aplica (un conflicto, una que ya existe) no la manda', () => {
      cargarVista(VISTA);

      service.elegir(['existe', 'revisar'], true);
      service.aplicar().subscribe();

      expect(http.expectOne(URL_APLICAR).request.body.claves).toEqual(['nueva', 'avanza']);
    });

    it('cada vista previa nueva vuelve a la elección por defecto', () => {
      cargarVista(VISTA);
      service.elegir(['nueva', 'avanza'], false);
      expect(service.elegidas().size).toBe(0);

      service.previsualizar();
      expect(service.elegidas().size).toBe(0);
      previsualizacion().flush({ ...VISTA, hashRegistro: 'c'.repeat(64) });

      expect([...service.elegidas()]).toEqual(['nueva', 'avanza']);
    });

    it('mientras guarda, o con claves que no se aplican, elegir no cambia nada', () => {
      cargarVista(VISTA);

      service.elegir(['existe', 'revisar', 'inventada'], true);
      expect([...service.elegidas()]).toEqual(['nueva', 'avanza']);

      service.aplicar().subscribe();
      service.elegir(['nueva'], false);
      expect([...service.elegidas()]).toEqual(['nueva', 'avanza']);
      http.expectOne(URL_APLICAR).flush(crearResultado());
      previsualizacion().flush(crearVista());
    });

    describe('con una elección hecha', () => {
      const CON_AVISO = crearVista([
        crearFila({ clave: 'nueva' }),
        crearFila({ clave: 'aviso', avisos: ['Revisa la empresa.'] }),
      ]);

      it('volver a leer con la misma clave deja otra vez sin marcar la que lleva aviso', () => {
        cargarVista(CON_AVISO);
        service.elegir(['aviso'], true);

        service.volverALeer();
        previsualizacion().flush(CON_AVISO);

        expect(service.elegidas().has('aviso')).toBe(false);
      });

      it('tras un 409, la vista nueva vuelve a la de por defecto y el segundo intento no manda la de aviso', () => {
        cargarVista(CON_AVISO);
        service.elegir(['aviso'], true);

        service.aplicar().subscribe({ error: () => undefined });
        http
          .expectOne(URL_APLICAR)
          .flush({ detail: 'Cambiado.' }, { status: 409, statusText: 'Conflict' });
        previsualizacion().flush({ ...CON_AVISO, hashRegistro: 'c'.repeat(64) });

        expect(service.elegidas().has('aviso')).toBe(false);
        service.aplicar().subscribe();
        expect(http.expectOne(URL_APLICAR).request.body.claves).toEqual(['nueva']);
      });

      it.each([400, 500])('tras un %i se conserva lo marcado', (status) => {
        cargarVista(CON_AVISO);
        service.elegir(['aviso'], true);
        service.elegir(['nueva'], false);

        service.aplicar().subscribe({ error: () => undefined });
        http.expectOne(URL_APLICAR).flush({ detail: 'Fallo.' }, { status, statusText: 'Error' });

        expect([...service.elegidas()]).toEqual(['aviso']);
        service.aplicar().subscribe();
        expect(http.expectOne(URL_APLICAR).request.body.claves).toEqual(['aviso']);
      });
    });

    it('un 400 da el detail y deja la vista previa para reintentar', () => {
      const detail = 'El puesto no puede tener más de 300 caracteres.';
      cargarVista(VISTA);

      let error: ErrorApi | undefined;
      service.aplicar().subscribe({ error: (e: ErrorApi) => (error = e) });
      http
        .expectOne(URL_APLICAR)
        .flush({ title: 'Título', detail }, { status: 400, statusText: 'Error' });

      expect(error?.estado).toBe(400);
      expect(error?.mensaje).toBe(detail);
      expect(service.aplicando()).toBe(false);
      expect(service.vistaPrevia()).toEqual(VISTA);
      http.expectNone(URL_PREVISUALIZAR);
    });

    it('un 409 da el detail y vuelve a pedir la vista previa: la que había ya no vale', () => {
      const detail = 'El registro ha cambiado desde que lo leíste. Vuelvo a leerlo.';
      cargarVista(VISTA);

      let error: ErrorApi | undefined;
      service.aplicar().subscribe({ error: (e: ErrorApi) => (error = e) });
      http
        .expectOne(URL_APLICAR)
        .flush({ title: 'Título', detail }, { status: 409, statusText: 'Conflict' });

      expect(error?.estado).toBe(409);
      expect(error?.mensaje).toBe(detail);
      expect(service.aplicando()).toBe(false);
      expect(service.vistaPrevia()).toBeNull();
      expect(service.cargando()).toBe(true);

      const nueva = crearVista([crearFila({ resultado: 'YaExiste' })]);
      previsualizacion().flush(nueva);
      expect(service.vistaPrevia()).toEqual(nueva);
    });
  });

  describe('con la extracción de Claude', () => {
    const URL_COMBINADA = '/api/importacion/extraccion/7/previsualizar';
    const NO_VALE = 'Esa extracción no ha terminado bien. Vuelve a extraer con Claude.';
    const COMBINADA = crearVista([crearFila({ clave: 'gamma' })], {
      origen: 'RegistroConClaude',
      ejecucionId: 7,
      dudas: ['Sin fecha.'],
    });
    let alNoValer: ReturnType<typeof vi.fn<(mensaje: string, yaAvisado: boolean) => void>>;

    beforeEach(() => (alNoValer = vi.fn()));

    function cargarCombinada(): void {
      service.previsualizarExtraccion(7, alNoValer);
      http.expectOne(URL_COMBINADA).flush(COMBINADA);
    }

    it('pide la vista combinada con un cuerpo JSON vacío y borra antes la anterior', () => {
      cargarVista();

      service.previsualizarExtraccion(7, alNoValer);

      const peticion = http.expectOne(URL_COMBINADA);
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({});
      expect(service.vistaPrevia()).toBeNull();
      expect(service.cargando()).toBe(true);
      peticion.flush(COMBINADA);

      expect(service.vistaPrevia()).toEqual(COMBINADA);
      expect(alNoValer).not.toHaveBeenCalled();
    });

    it.each([403, 404, 409])(
      'con un %i la extracción ya no vale: pide la tabla y pasa el mensaje',
      (status) => {
        service.previsualizarExtraccion(7, alNoValer);
        http.expectOne(URL_COMBINADA).flush({ detail: NO_VALE }, { status, statusText: 'No' });

        expect(alNoValer).toHaveBeenCalledExactlyOnceWith(NO_VALE, false);
        expect(service.error()).toBeNull();
        expect(service.cargando()).toBe(true);
        previsualizacion().flush(crearVista());
        expect(service.vistaPrevia()?.origen).toBe('Tabla');
      },
    );

    it('sin conexión deja el error y no vuelve a la tabla', () => {
      service.previsualizarExtraccion(7, alNoValer);
      http.expectOne(URL_COMBINADA).error(new ProgressEvent('error'));

      expect(service.error()).toContain('No se puede conectar con RadarEmpleo');
      expect(alNoValer).not.toHaveBeenCalled();
      http.expectNone(URL_PREVISUALIZAR);
    });

    it('«volver a leer» repite la última: la combinada o, tras pedir la tabla, la tabla', () => {
      cargarCombinada();

      service.volverALeer();
      http.expectOne(URL_COMBINADA).flush(COMBINADA);
      expect(service.extraccionId()).toBe(7);

      service.previsualizar();
      previsualizacion().flush(crearVista());
      expect(service.extraccionId()).toBeNull();
      service.volverALeer();
      previsualizacion().flush(crearVista());
    });

    it('apuntar una extracción no pide nada: al acabar de aplicar se lee la combinada', () => {
      cargarVista();

      service.aplicar().subscribe();
      service.apuntarExtraccion(7, alNoValer);
      http.expectNone(URL_COMBINADA);
      expect(service.vistaPrevia()).not.toBeNull();

      http.expectOne(URL_APLICAR).flush(crearResultado());
      http.expectOne(URL_COMBINADA).flush(COMBINADA);
      http.expectNone(URL_PREVISUALIZAR);
    });

    it('si el aplicar falla sin ser un 409 y se apuntó una extracción mientras guardaba, pide la combinada', () => {
      cargarVista();

      service.aplicar().subscribe({ error: () => undefined });
      service.apuntarExtraccion(7, alNoValer);
      http.expectOne(URL_APLICAR).flush({ detail: 'Fallo.' }, { status: 500, statusText: 'Error' });

      http.expectOne(URL_COMBINADA).flush(COMBINADA);
      expect(service.vistaPrevia()?.origen).toBe('RegistroConClaude');
    });

    it('con la combinada a la vista, un fallo que no es 409 no vuelve a leer nada', () => {
      cargarCombinada();

      service.aplicar().subscribe({ error: () => undefined });
      http.expectOne(URL_APLICAR).flush({ detail: 'Fallo.' }, { status: 500, statusText: 'Error' });

      http.expectNone(URL_COMBINADA);
      expect(service.vistaPrevia()).toEqual(COMBINADA);
    });

    it('olvidar la extracción hace que volver a leer pida la tabla', () => {
      cargarCombinada();

      service.olvidarExtraccion();
      service.volverALeer();

      expect(service.extraccionId()).toBeNull();
      previsualizacion().flush(crearVista());
    });

    it('comprueba si una extracción vale sin tocar la vista que se enseña', () => {
      cargarVista();
      const resultados: boolean[] = [];

      service.extraccionVale(7).subscribe((vale) => resultados.push(vale));
      http.expectOne(URL_COMBINADA).flush(COMBINADA);
      service.extraccionVale(7).subscribe((vale) => resultados.push(vale));
      http
        .expectOne(URL_COMBINADA)
        .flush({ detail: NO_VALE }, { status: 409, statusText: 'Conflict' });

      expect(resultados).toEqual([true, false]);
      expect(service.vistaPrevia()?.origen).toBe('Tabla');
      expect(service.extraccionId()).toBeNull();
    });

    it('aplicar manda RegistroConClaude con la ejecución y vuelve a leer la combinada', () => {
      cargarCombinada();

      service.aplicar().subscribe();
      const peticion = http.expectOne(URL_APLICAR);
      expect(peticion.request.body).toStrictEqual({
        origen: 'RegistroConClaude',
        ejecucionId: 7,
        hashRegistro: 'a'.repeat(64),
        claves: ['gamma'],
      });
      peticion.flush(crearResultado());

      http.expectOne(URL_COMBINADA).flush(COMBINADA);
    });

    it('con un 409 al aplicar, si la extracción ya no vale vuelve a la tabla sin repetir el aviso', () => {
      cargarCombinada();

      let error: ErrorApi | undefined;
      service.aplicar().subscribe({ error: (e: ErrorApi) => (error = e) });
      http
        .expectOne(URL_APLICAR)
        .flush({ detail: 'Cambiado.' }, { status: 409, statusText: 'Conflict' });
      http
        .expectOne(URL_COMBINADA)
        .flush({ detail: NO_VALE }, { status: 409, statusText: 'Conflict' });
      previsualizacion().flush(crearVista());

      expect(error?.mensaje).toBe('Cambiado.');
      expect(alNoValer).toHaveBeenCalledExactlyOnceWith(NO_VALE, true);
      expect(service.error()).toBeNull();
      expect(service.vistaPrevia()?.origen).toBe('Tabla');
    });
  });
});
