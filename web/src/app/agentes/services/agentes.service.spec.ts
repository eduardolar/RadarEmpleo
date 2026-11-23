import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { SistemaService } from '../../core/services/sistema.service';
import { crearSistemaInfo } from '../../core/utils/sistema-de-prueba';
import { EjecucionAgente, EstadoEjecucion } from '../interfaces/ejecucion.interface';
import { crearEjecucion, crearEstadoAgentes } from '../utils/ejecucion-de-prueba';
import { AgentesService, INTERVALO_SEGUIMIENTO_MS } from './agentes.service';

const URL_ESTADO = '/api/agentes/estado';
const URL_EJECUCIONES = '/api/agentes/ejecuciones';

describe('AgentesService', () => {
  let service: AgentesService;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AgentesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  function conInfo(agentesDisponibles: boolean): void {
    TestBed.inject(SistemaService).cargarInfo();
    http.expectOne('/api/sistema/info').flush(crearSistemaInfo({ agentesDisponibles }));
  }

  /** Lanza un análisis y responde con la ejecución dada (en cola, por defecto) */
  function lanzar(ejecucion = crearEjecucion()): void {
    service.lanzarAnalisis(ejecucion.candidaturaId ?? 12).subscribe();
    http
      .expectOne(`/api/agentes/analizar/${ejecucion.candidaturaId ?? 12}`)
      .flush(ejecucion, { status: 202, statusText: 'Accepted' });
  }

  function seguimiento(id = 1): ReturnType<HttpTestingController['expectOne']> {
    return http.expectOne(`${URL_EJECUCIONES}/${id}`);
  }

  function ninguno(id = 1): void {
    expect(http.match(`${URL_EJECUCIONES}/${id}`)).toEqual([]);
  }

  function ponerVisibilidad(estado: DocumentVisibilityState): void {
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue(estado);
    document.dispatchEvent(new Event('visibilitychange'));
  }

  describe('sin agentes disponibles', () => {
    it('no pide el estado si /api/sistema/info todavía no ha contestado', () => {
      service.cargarEstado();

      http.expectNone(URL_ESTADO);
      expect(service.disponibles()).toBe(false);
      expect(service.puedeLanzar()).toBe(false);
    });

    it('no pide el estado ni la última ejecución, ni lanza nada, con agentesDisponibles a false', () => {
      conInfo(false);
      let ultima: EjecucionAgente | null | undefined;
      let emitido = false;

      service.cargarEstado();
      service.ultimaDe('AnalizarEncaje', 12).subscribe((e) => (ultima = e));
      service.lanzarAnalisis(12).subscribe(() => (emitido = true));
      service.lanzarExtraccion().subscribe(() => (emitido = true));

      http.expectNone(URL_ESTADO);
      http.expectNone(() => true);
      expect(ultima).toBeNull();
      expect(emitido).toBe(false);
      expect(service.puedeLanzar()).toBe(false);
    });
  });

  describe('cargarEstado', () => {
    beforeEach(() => conInfo(true));

    it('guarda el estado y, con la cola en esta copia, deja lanzar', () => {
      const estado = crearEstadoAgentes();
      expect(service.puedeLanzar()).toBe(false);

      service.cargarEstado();
      const peticion = http.expectOne(URL_ESTADO);
      expect(peticion.request.method).toBe('GET');
      peticion.flush(estado);

      expect(service.estado()).toEqual(estado);
      expect(service.llevaLaCola()).toBe(true);
      expect(service.motivoCola()).toBeNull();
      expect(service.puedeLanzar()).toBe(true);
    });

    it('si otra copia lleva la cola, no deja lanzar y dice por qué', () => {
      const motivo = 'Otra copia de RadarEmpleo lleva los agentes de esta base de datos.';

      service.cargarEstado();
      http
        .expectOne(URL_ESTADO)
        .flush(crearEstadoAgentes({ llevaLaCola: false, motivoCola: motivo }));

      expect(service.disponibles()).toBe(true);
      expect(service.llevaLaCola()).toBe(false);
      expect(service.motivoCola()).toBe(motivo);
      expect(service.puedeLanzar()).toBe(false);
    });

    it('un 403 deja disponibles a false y ya no se pide nada más', () => {
      service.cargarEstado();
      http.expectOne(URL_ESTADO).flush(null, { status: 403, statusText: 'Forbidden' });

      expect(service.disponibles()).toBe(false);
      expect(service.estado()).toBeNull();
      expect(service.errorEstado()).toBeNull();

      service.lanzarExtraccion().subscribe();
      http.expectNone('/api/agentes/extraer-registro');
    });

    it('tras un 403 se puede volver a pedir y, si contesta bien, vuelve a estar disponible', () => {
      service.cargarEstado();
      http.expectOne(URL_ESTADO).flush(null, { status: 403, statusText: 'Forbidden' });
      expect(service.disponibles()).toBe(false);

      service.cargarEstado();
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());

      expect(service.disponibles()).toBe(true);
      expect(service.puedeLanzar()).toBe(true);
    });

    it('un error que no es 403 conserva el estado anterior', () => {
      const estado = crearEstadoAgentes();
      service.cargarEstado();
      http.expectOne(URL_ESTADO).flush(estado);

      service.cargarEstado();
      http.expectOne(URL_ESTADO).flush(null, { status: 500, statusText: 'x' });

      expect(service.estado()).toEqual(estado);
      expect(service.errorEstado()).not.toBeNull();
    });

    it('otro error deja el mensaje y sigue disponible', () => {
      service.cargarEstado();
      http.expectOne(URL_ESTADO).flush(null, { status: 503, statusText: 'x' });

      expect(service.errorEstado()).toContain('No se puede conectar');
      expect(service.disponibles()).toBe(true);
    });

    it('una segunda petición cancela la primera', () => {
      service.cargarEstado();
      const primera = http.expectOne(URL_ESTADO);

      service.cargarEstado();

      expect(primera.cancelled).toBe(true);
      http.expectOne(URL_ESTADO).flush(crearEstadoAgentes());
    });
  });

  describe('lanzar, cancelar y reintentar', () => {
    beforeEach(() => conInfo(true));

    it('analizar: POST con {} y la ejecución queda en el mapa', () => {
      let lanzada: EjecucionAgente | undefined;
      const ejecucion = crearEjecucion({ id: 3 });

      service.lanzarAnalisis(12).subscribe((e) => (lanzada = e));

      const peticion = http.expectOne('/api/agentes/analizar/12');
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({});
      peticion.flush(ejecucion, { status: 202, statusText: 'Accepted' });

      expect(lanzada).toEqual(ejecucion);
      expect(service.ejecuciones().get(3)).toEqual(ejecucion);
    });

    it('extraer el registro: POST con {}', () => {
      service.lanzarExtraccion().subscribe();

      const peticion = http.expectOne('/api/agentes/extraer-registro');
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({});
      peticion.flush(crearEjecucion({ tarea: 'ExtraerRegistro', candidaturaId: null }));
    });

    it('no manda un segundo lanzamiento igual mientras va el primero', () => {
      service.lanzarAnalisis(12).subscribe();
      service.lanzarAnalisis(12).subscribe();
      service.lanzarAnalisis(13).subscribe();

      http.expectOne('/api/agentes/analizar/12').flush(crearEjecucion({ id: 1 }));
      http
        .expectOne('/api/agentes/analizar/13')
        .flush(crearEjecucion({ id: 2, candidaturaId: 13 }));
    });

    it('cancelar: POST con {} a su ruta', () => {
      const ejecucion = crearEjecucion({ id: 5 });

      service.cancelar(ejecucion).subscribe();

      const peticion = http.expectOne(`${URL_EJECUCIONES}/5/cancelar`);
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({});
      expect(service.ocupada(5)).toBe(true);
      peticion.flush(crearEjecucion({ id: 5, estado: 'Cancelada' }));

      expect(service.ocupada(5)).toBe(false);
      expect(service.ejecuciones().get(5)?.estado).toBe('Cancelada');
    });

    it('no deja un segundo cancelar (ni reintentar) mientras va el primero', () => {
      const ejecucion = crearEjecucion({ id: 5, estado: 'EnCurso' });
      let segundo = false;

      service.cancelar(ejecucion).subscribe();
      service.cancelar(ejecucion).subscribe(() => (segundo = true));
      service.reintentar(ejecucion).subscribe(() => (segundo = true));

      http
        .expectOne(`${URL_EJECUCIONES}/5/cancelar`)
        .flush(crearEjecucion({ id: 5, estado: 'EnCurso', puedeCancelar: false }));
      http.expectNone(`${URL_EJECUCIONES}/5/reintentar`);
      expect(segundo).toBe(false);

      // Tras la respuesta ya se puede volver a operar
      service.cancelar(ejecucion).subscribe({ error: () => undefined });
      http
        .expectOne(`${URL_EJECUCIONES}/5/cancelar`)
        .flush({ detail: 'Ya ha terminado.' }, { status: 409, statusText: 'Conflict' });
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento(5).flush(crearEjecucion({ id: 5, estado: 'Cancelada' }));
    });

    it('reintentar: POST con {} y sigue la ejecución nueva', () => {
      let nueva: EjecucionAgente | undefined;

      service.reintentar(crearEjecucion({ id: 5, estado: 'Error' })).subscribe((e) => (nueva = e));

      const peticion = http.expectOne(`${URL_EJECUCIONES}/5/reintentar`);
      expect(peticion.request.body).toEqual({});
      peticion.flush(crearEjecucion({ id: 6 }), { status: 202, statusText: 'Accepted' });

      expect(nueva?.id).toBe(6);
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento(6).flush(crearEjecucion({ id: 6, estado: 'Ok' }));
    });

    it('un error llega como ErrorApi; un 403 además deja disponibles a false', () => {
      let error: ErrorApi | undefined;

      service.lanzarAnalisis(12).subscribe({ error: (e: ErrorApi) => (error = e) });
      http
        .expectOne('/api/agentes/analizar/12')
        .flush({ detail: 'Solo desde el PC.' }, { status: 403, statusText: 'Forbidden' });

      expect(error).toEqual({ estado: 403, mensaje: 'Solo desde el PC.', errores: {} });
      expect(service.disponibles()).toBe(false);
    });

    it('un 409 no cambia la disponibilidad', () => {
      let error: ErrorApi | undefined;

      service.lanzarExtraccion().subscribe({ error: (e: ErrorApi) => (error = e) });
      http
        .expectOne('/api/agentes/extraer-registro')
        .flush(
          { detail: 'Has llegado al tope de uso estimado de hoy.' },
          { status: 409, statusText: 'Conflict' },
        );

      expect(error?.mensaje).toBe('Has llegado al tope de uso estimado de hoy.');
      expect(service.disponibles()).toBe(true);
    });
  });

  describe('seguimiento', () => {
    beforeEach(() => conInfo(true));

    it('pide la ejecución cada 5 s hasta que termina y entonces para', () => {
      const terminadas: EjecucionAgente[] = [];
      service.terminada$.subscribe((e) => terminadas.push(e));
      lanzar();

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS - 1);
      ninguno();
      vi.advanceTimersByTime(1);
      const primera = seguimiento();
      expect(primera.request.method).toBe('GET');
      primera.flush(crearEjecucion({ estado: 'EnCurso', inicio: '2026-11-23T10:00:05Z' }));
      expect(service.ejecuciones().get(1)?.estado).toBe('EnCurso');

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      const ok = crearEjecucion({ estado: 'Ok', costeUsd: 0.18 });
      seguimiento().flush(ok);

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 4);
      ninguno();
      expect(terminadas).toEqual([ok]);
      expect(service.ejecuciones().get(1)).toEqual(ok);
    });

    it.each<EstadoEjecucion>(['Ok', 'Error', 'Cancelada'])('para en %s', (estado) => {
      lanzar();

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento().flush(crearEjecucion({ estado }));

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 3);
      ninguno();
    });

    it('terminada$ emite una sola vez aunque se vuelva a ver terminada', () => {
      const terminadas: EjecucionAgente[] = [];
      service.terminada$.subscribe((e) => terminadas.push(e));
      lanzar();
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento().flush(crearEjecucion({ estado: 'Ok' }));

      service.ultimaDe('AnalizarEncaje', 12).subscribe();
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush([crearEjecucion({ estado: 'Ok' })]);

      expect(terminadas.length).toBe(1);
    });

    it('lanzar una que ya ha terminado (oferta cerrada) no la sigue ni avisa', () => {
      const terminadas: EjecucionAgente[] = [];
      service.terminada$.subscribe((e) => terminadas.push(e));

      lanzar(crearEjecucion({ estado: 'Ok', costeUsd: 0 }));

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 2);
      ninguno();
      expect(terminadas).toEqual([]);
    });

    it('un solo temporizador para todas las activas', () => {
      lanzar(crearEjecucion({ id: 1, candidaturaId: 12 }));
      vi.advanceTimersByTime(2000);
      lanzar(crearEjecucion({ id: 2, candidaturaId: 13 }));

      // A los 5 s de la primera salen las dos juntas
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS - 2000);
      seguimiento(1).flush(crearEjecucion({ id: 1, estado: 'EnCurso' }));
      seguimiento(2).flush(crearEjecucion({ id: 2, candidaturaId: 13 }));

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento(1).flush(crearEjecucion({ id: 1, estado: 'Ok' }));
      seguimiento(2).flush(crearEjecucion({ id: 2, candidaturaId: 13, estado: 'EnCurso' }));

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      ninguno(1);
      seguimiento(2).flush(crearEjecucion({ id: 2, candidaturaId: 13, estado: 'Ok' }));
    });

    it('no solapa peticiones: si la API tarda, espera a la respuesta y otros 5 s', () => {
      lanzar();
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      const lenta = seguimiento();

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 3);
      ninguno();
      lenta.flush(crearEjecucion({ estado: 'EnCurso' }));

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento().flush(crearEjecucion({ estado: 'Ok' }));
    });

    it('se pausa con la pestaña oculta y, al volver, pregunta al momento', () => {
      lanzar();

      ponerVisibilidad('hidden');
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 4);
      ninguno();

      ponerVisibilidad('visible');
      seguimiento().flush(crearEjecucion({ estado: 'EnCurso' }));

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento().flush(crearEjecucion({ estado: 'Ok' }));
    });

    it('lanzada con la pestaña oculta, no pregunta hasta que vuelve', () => {
      ponerVisibilidad('hidden');
      lanzar();

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 2);
      ninguno();

      ponerVisibilidad('visible');
      seguimiento().flush(crearEjecucion({ estado: 'Ok' }));
    });

    it('volver a la pestaña sin nada que seguir no pide nada', () => {
      ponerVisibilidad('hidden');
      ponerVisibilidad('visible');

      http.expectNone(() => true);
    });

    it('si falla la conexión, lo vuelve a intentar en 5 s', () => {
      lanzar();
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento().error(new ProgressEvent('error'));

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento().flush(crearEjecucion({ estado: 'Ok' }));
    });

    it('un 404 (ya no existe) la quita de las ejecuciones, avisa por perdida$ y deja de seguirla', () => {
      const perdidas: number[] = [];
      const terminadas: EjecucionAgente[] = [];
      service.perdida$.subscribe((id) => perdidas.push(id));
      service.terminada$.subscribe((e) => terminadas.push(e));
      lanzar();
      expect(service.ejecuciones().get(1)?.estado).toBe('EnCola');

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento().flush(null, { status: 404, statusText: 'Not Found' });

      expect(service.ejecuciones().has(1)).toBe(false);
      expect(perdidas).toEqual([1]);
      expect(terminadas).toEqual([]);
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 2);
      ninguno();
    });

    it('un 403 deja de seguir todo y deja disponibles a false', () => {
      lanzar();
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento().flush(null, { status: 403, statusText: 'Forbidden' });

      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 2);
      ninguno();
      expect(service.disponibles()).toBe(false);
    });

    it('tras un 403, otra respuesta que seguía en vuelo no vuelve a armar el seguimiento', () => {
      lanzar(crearEjecucion({ id: 1, candidaturaId: 12 }));
      lanzar(crearEjecucion({ id: 2, candidaturaId: 13 }));
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      const otra = seguimiento(2);

      seguimiento(1).flush(null, { status: 403, statusText: 'Forbidden' });
      otra.flush(crearEjecucion({ id: 2, candidaturaId: 13, estado: 'EnCurso' }));

      expect(vi.getTimerCount()).toBe(0);
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS * 2);
      ninguno(1);
      ninguno(2);
      expect(service.ejecuciones().get(2)?.estado).toBe('EnCurso');
    });

    it('al destruirse no queda ningún temporizador', () => {
      lanzar();

      TestBed.resetTestingModule();

      expect(vi.getTimerCount()).toBe(0);
    });
  });

  describe('ultimaDe', () => {
    beforeEach(() => conInfo(true));

    it('pide la última de esa tarea y candidatura con limite=1 y sigue la que no ha terminado', () => {
      let ultima: EjecucionAgente | null | undefined;

      service.ultimaDe('AnalizarEncaje', 12).subscribe((e) => (ultima = e));

      const peticion = http.expectOne((r) => r.url === URL_EJECUCIONES);
      expect(peticion.request.method).toBe('GET');
      expect(peticion.request.params.get('tarea')).toBe('AnalizarEncaje');
      expect(peticion.request.params.get('candidaturaId')).toBe('12');
      expect(peticion.request.params.get('limite')).toBe('1');
      peticion.flush([crearEjecucion({ id: 4, estado: 'EnCurso' })]);

      expect(ultima?.id).toBe(4);
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      seguimiento(4).flush(crearEjecucion({ id: 4, estado: 'Ok' }));
    });

    it('sin candidatura no manda el parámetro; sin ninguna, null', () => {
      let ultima: EjecucionAgente | null | undefined;

      service.ultimaDe('ExtraerRegistro').subscribe((e) => (ultima = e));

      const peticion = http.expectOne((r) => r.url === URL_EJECUCIONES);
      expect(peticion.request.params.has('candidaturaId')).toBe(false);
      peticion.flush([]);

      expect(ultima).toBeNull();
    });
  });

  describe('cargarHistorial', () => {
    it('sin agentes disponibles no pide nada', () => {
      conInfo(false);
      service.cargarHistorial();

      expect(http.match((r) => r.url === URL_EJECUCIONES)).toEqual([]);
      expect(service.historial()).toBeNull();
    });

    it('pide las últimas 50 sin filtros y no sigue ninguna', () => {
      conInfo(true);
      service.cargarHistorial();
      expect(service.cargandoHistorial()).toBe(true);

      const peticion = http.expectOne((r) => r.url === URL_EJECUCIONES);
      expect(peticion.request.params.get('limite')).toBe('50');
      expect(peticion.request.params.has('tarea')).toBe(false);
      peticion.flush([crearEjecucion({ id: 2, estado: 'EnCurso' }), crearEjecucion({ id: 1 })]);

      expect(service.historial()?.map((e) => e.id)).toEqual([2, 1]);
      expect(service.cargandoHistorial()).toBe(false);
      // El seguimiento lo arma quien las pinta (la tarjeta), no la lista
      vi.advanceTimersByTime(INTERVALO_SEGUIMIENTO_MS);
      ninguno(2);
    });

    it('al refrescar conserva la lista anterior; si falla, deja el mensaje y la lista', () => {
      conInfo(true);
      service.cargarHistorial();
      http.expectOne((r) => r.url === URL_EJECUCIONES).flush([crearEjecucion({ id: 1 })]);

      service.cargarHistorial();
      expect(service.historial()?.map((e) => e.id)).toEqual([1]);
      http
        .expectOne((r) => r.url === URL_EJECUCIONES)
        .flush({ title: 'Fallo' }, { status: 500, statusText: 'Error' });

      expect(service.historial()?.map((e) => e.id)).toEqual([1]);
      expect(service.errorHistorial()).not.toBeNull();
      expect(service.cargandoHistorial()).toBe(false);
    });

    it('un 403 deja disponibles a false y no deja mensaje', () => {
      conInfo(true);
      service.cargarHistorial();
      http
        .expectOne((r) => r.url === URL_EJECUCIONES)
        .flush(null, { status: 403, statusText: 'Forbidden' });

      expect(service.disponibles()).toBe(false);
      expect(service.errorHistorial()).toBeNull();
    });
  });
});
