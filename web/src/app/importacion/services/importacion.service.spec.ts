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

    it('reenvía el lote tal cual con las claves de las nuevas y las que cambian de estado', () => {
      cargarVista(VISTA);

      service.aplicar().subscribe();

      const peticion = http.expectOne(URL_APLICAR);
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({ lote: VISTA.lote, claves: ['nueva', 'avanza'] });
      // El lote es el mismo objeto: ni se rellenan los null ni se toca el enlace
      expect(peticion.request.body.lote).toBe(VISTA.lote);
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
      const detail =
        'Estas candidaturas ya se han importado desde otra pestaña. Vuelvo a leer el registro.';
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
});
