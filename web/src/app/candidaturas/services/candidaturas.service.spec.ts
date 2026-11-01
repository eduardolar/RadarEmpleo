import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { crearDetalle, crearResumen } from '../utils/candidatura-de-prueba';
import { CandidaturasService } from './candidaturas.service';

describe('CandidaturasService', () => {
  let service: CandidaturasService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CandidaturasService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function esperarLista(): ReturnType<HttpTestingController['expectOne']> {
    return http.expectOne((r) => r.method === 'GET' && r.url === '/api/candidaturas');
  }

  it('lista las candidaturas con los filtros y marca «cargando» mientras espera', () => {
    service.cargar({ texto: '  angular ', estado: 'Enviada' });

    const peticion = esperarLista();
    expect(peticion.request.params.get('texto')).toBe('angular');
    expect(peticion.request.params.get('estado')).toBe('Enviada');
    expect(service.cargando()).toBe(true);

    peticion.flush([crearResumen()]);

    expect(service.cargando()).toBe(false);
    expect(service.candidaturas()).toHaveLength(1);
    expect(service.error()).toBeNull();
  });

  it('no manda filtros vacíos', () => {
    service.cargar({ texto: '   ' });

    expect(esperarLista().request.params.keys()).toEqual([]);
  });

  it('guarda el mensaje de error si la lista no se puede cargar', () => {
    service.cargar();
    esperarLista().error(new ProgressEvent('error'));

    expect(service.cargando()).toBe(false);
    expect(service.error()).toContain('No se puede conectar con RadarEmpleo');
  });

  it('carga la ficha y avisa con 404 si no existe', () => {
    service.cargarDetalle(1);
    http.expectOne('/api/candidaturas/1').flush(crearDetalle());
    expect(service.detalle()?.id).toBe(1);

    service.cargarDetalle(2);
    expect(service.detalle()).toBeNull();
    http
      .expectOne('/api/candidaturas/2')
      .flush(
        { title: 'No se ha encontrado.', status: 404 },
        { status: 404, statusText: 'Not Found' },
      );

    expect(service.errorDetalle()?.estado).toBe(404);
    expect(service.cargandoDetalle()).toBe(false);
  });

  it('crea con POST y refresca la lista si ya se estaba viendo', () => {
    service.cargar();
    esperarLista().flush([]);

    let creada: number | undefined;
    service.crear({ empresa: 'Acme', puesto: 'Dev' }).subscribe((d) => (creada = d.id));

    const peticion = http.expectOne('/api/candidaturas');
    expect(peticion.request.method).toBe('POST');
    expect(peticion.request.body).toEqual({ empresa: 'Acme', puesto: 'Dev' });
    peticion.flush(crearDetalle({ id: 5 }), { status: 201, statusText: 'Created' });

    expect(creada).toBe(5);
    esperarLista().flush([crearResumen({ id: 5 })]);
    expect(service.candidaturas().map((c) => c.id)).toEqual([5]);
  });

  it('devuelve los errores por campo de un 400', () => {
    let error: ErrorApi | undefined;
    service.crear({ empresa: '', puesto: '' }).subscribe({ error: (e: ErrorApi) => (error = e) });

    http.expectOne('/api/candidaturas').flush(
      {
        title: 'Hay datos que no son válidos.',
        status: 400,
        detail: 'La empresa es obligatoria. El puesto es obligatorio.',
        errors: { empresa: ['La empresa es obligatoria.'], puesto: ['El puesto es obligatorio.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(error?.estado).toBe(400);
    expect(error?.errores['empresa']).toEqual(['La empresa es obligatoria.']);
    expect(error?.errores['puesto']).toEqual(['El puesto es obligatorio.']);
  });

  it('mueve la tarjeta al momento al cambiar de estado y guarda la ficha que devuelve la API', () => {
    service.cargar();
    esperarLista().flush([crearResumen({ id: 1, estado: 'Detectada' })]);
    service.cargarDetalle(1);
    http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));

    service.cambiarEstado(1, { estado: 'Enviada' }).subscribe();
    expect(service.candidaturas()[0].estado).toBe('Enviada');

    const peticion = http.expectOne('/api/candidaturas/1/estado');
    expect(peticion.request.method).toBe('POST');
    expect(peticion.request.body).toEqual({ estado: 'Enviada' });
    peticion.flush(crearDetalle({ id: 1, estado: 'Enviada', estadosPosibles: ['Entrevista'] }));

    expect(service.detalle()?.estado).toBe('Enviada');
    expect(service.detalle()?.estadosPosibles).toEqual(['Entrevista']);
    esperarLista().flush([crearResumen({ id: 1, estado: 'Enviada' })]);
  });

  it('devuelve la tarjeta a su columna, da el mensaje de la API y refresca si responde 409', () => {
    service.cargar();
    esperarLista().flush([crearResumen({ id: 1, estado: 'Enviada' })]);

    let error: ErrorApi | undefined;
    service
      .cambiarEstado(1, { estado: 'Detectada' })
      .subscribe({ error: (e: ErrorApi) => (error = e) });
    expect(service.candidaturas()[0].estado).toBe('Detectada');

    http.expectOne('/api/candidaturas/1/estado').flush(
      {
        title: 'No se puede hacer esa operación',
        status: 409,
        detail: 'No se puede volver de «Enviada» a «Detectada»: los estados solo avanzan.',
      },
      { status: 409, statusText: 'Conflict' },
    );

    expect(service.candidaturas()[0].estado).toBe('Enviada');
    expect(error?.estado).toBe(409);
    expect(error?.mensaje).toBe(
      'No se puede volver de «Enviada» a «Detectada»: los estados solo avanzan.',
    );
    // Lo que se veía estaba desfasado: se vuelve a pedir la lista
    esperarLista().flush([crearResumen({ id: 1, estado: 'Enviada' })]);
  });

  describe('rechazos de un cambio de estado', () => {
    function responderEstado(status: number, cuerpo: object = {}): void {
      http.expectOne('/api/candidaturas/1/estado').flush(cuerpo, { status, statusText: 'Error' });
    }

    it('con 500 devuelve la tarjeta a su columna y no pide nada más', () => {
      service.cargar();
      esperarLista().flush([crearResumen({ id: 1, estado: 'Preparada' })]);
      service.cargarDetalle(1);
      http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));

      let error: ErrorApi | undefined;
      service
        .cambiarEstado(1, { estado: 'Enviada' })
        .subscribe({ error: (e: ErrorApi) => (error = e) });
      responderEstado(500);

      expect(service.candidaturas()[0].estado).toBe('Preparada');
      expect(error?.estado).toBe(500);
      expect(error?.mensaje).toContain('error inesperado');
      http.expectNone('/api/candidaturas/1');
      http.expectNone((r) => r.method === 'GET' && r.url === '/api/candidaturas');
    });

    it('con 404 refresca la lista y la ficha: se ha borrado desde otro sitio', () => {
      service.cargar();
      esperarLista().flush([crearResumen({ id: 1 }), crearResumen({ id: 2 })]);
      service.cargarDetalle(1);
      http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));

      service.cambiarEstado(1, { estado: 'Enviada' }).subscribe({ error: () => undefined });
      responderEstado(404, { title: 'No se ha encontrado.' });

      esperarLista().flush([crearResumen({ id: 2 })]);
      expect(service.candidaturas().map((c) => c.id)).toEqual([2]);

      http
        .expectOne('/api/candidaturas/1')
        .flush({ title: 'No se ha encontrado.' }, { status: 404, statusText: 'Not Found' });
      expect(service.detalle()).toBeNull();
      expect(service.errorDetalle()?.estado).toBe(404);
    });

    it('con 404 de otra candidatura no toca la ficha abierta', () => {
      service.cargar();
      esperarLista().flush([crearResumen({ id: 1 })]);
      service.cargarDetalle(7);
      http.expectOne('/api/candidaturas/7').flush(crearDetalle({ id: 7 }));

      service.cambiarEstado(1, { estado: 'Enviada' }).subscribe({ error: () => undefined });
      responderEstado(404);

      esperarLista().flush([]);
      http.expectNone('/api/candidaturas/7');
      expect(service.detalle()?.id).toBe(7);
    });
  });

  describe('la ficha', () => {
    it('un 404 borra los datos que hubiera: la candidatura ya no existe', () => {
      service.cargarDetalle(1);
      http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));

      service.cargarDetalle(1);
      http
        .expectOne('/api/candidaturas/1')
        .flush({ title: 'No se ha encontrado.' }, { status: 404, statusText: 'Not Found' });

      expect(service.detalle()).toBeNull();
      expect(service.errorDetalle()?.estado).toBe(404);
    });

    it('otro error conserva los datos: la candidatura sigue existiendo', () => {
      service.cargarDetalle(1);
      http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));

      service.cargarDetalle(1);
      http.expectOne('/api/candidaturas/1').error(new ProgressEvent('error'));

      expect(service.detalle()?.id).toBe(1);
      expect(service.errorDetalle()?.estado).toBe(0);
    });

    it('un cambio correcto quita el error de una carga anterior', () => {
      service.cargarDetalle(1);
      http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));
      service.cargarDetalle(1);
      http.expectOne('/api/candidaturas/1').error(new ProgressEvent('error'));

      service.cambiarEstado(1, { estado: 'Enviada' }).subscribe();
      http
        .expectOne('/api/candidaturas/1/estado')
        .flush(crearDetalle({ id: 1, estado: 'Enviada' }));

      expect(service.errorDetalle()).toBeNull();
    });

    describe('una respuesta antigua no pisa lo recién cambiado', () => {
      const NOTA = {
        id: 9,
        candidaturaId: 1,
        texto: 'Nota nueva',
        fecha: '2026-10-30T09:00:00Z',
        origen: 'Manual' as const,
      };

      /** Ficha abierta y una recarga suya que aún no ha respondido */
      function fichaConRecargaEnCamino(): ReturnType<HttpTestingController['expectOne']> {
        service.cargarDetalle(1);
        http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));
        service.cargarDetalle(1);
        expect(service.cargandoDetalle()).toBe(true);
        return http.expectOne('/api/candidaturas/1');
      }

      it('al cambiar de estado se cancela la recarga en camino', () => {
        const antigua = fichaConRecargaEnCamino();

        service.cambiarEstado(1, { estado: 'Enviada' }).subscribe();
        http
          .expectOne('/api/candidaturas/1/estado')
          .flush(crearDetalle({ id: 1, estado: 'Enviada' }));

        expect(antigua.cancelled).toBe(true);
        expect(service.cargandoDetalle()).toBe(false);
        expect(service.detalle()?.estado).toBe('Enviada');
      });

      it('al editar se cancela la recarga en camino', () => {
        const antigua = fichaConRecargaEnCamino();

        service.editar(1, { empresa: 'Acme', puesto: 'Nuevo' }).subscribe();
        http
          .expectOne((r) => r.method === 'PUT' && r.url === '/api/candidaturas/1')
          .flush(crearDetalle({ id: 1, puesto: 'Nuevo' }));

        expect(antigua.cancelled).toBe(true);
        expect(service.cargandoDetalle()).toBe(false);
      });

      it('al añadir una nota se cancela la recarga en camino', () => {
        const antigua = fichaConRecargaEnCamino();

        service.anadirNota(1, 'Nota nueva').subscribe();
        http
          .expectOne('/api/candidaturas/1/notas')
          .flush(NOTA, { status: 201, statusText: 'Created' });

        expect(antigua.cancelled).toBe(true);
        expect(service.cargandoDetalle()).toBe(false);
        expect(service.detalle()?.notas).toEqual([NOTA]);
      });

      it('un cambio de otra candidatura no cancela la carga de la ficha abierta', () => {
        const enCamino = fichaConRecargaEnCamino();

        service.anadirNota(2, 'Nota nueva').subscribe();
        http
          .expectOne('/api/candidaturas/2/notas')
          .flush({ ...NOTA, candidaturaId: 2 }, { status: 201, statusText: 'Created' });

        expect(enCamino.cancelled).toBe(false);
        expect(service.cargandoDetalle()).toBe(true);
        enCamino.flush(crearDetalle({ id: 1 }));
      });
    });
  });

  describe('dos cambios de estado a la vez', () => {
    const LISTA = [
      crearResumen({ id: 1, estado: 'Preparada' }),
      crearResumen({ id: 2, estado: 'Preparada' }),
    ];
    const hayLista = (r: { method: string; url: string }) =>
      r.method === 'GET' && r.url === '/api/candidaturas';

    function responderEstado(id: number, estado: 'Enviada' | 'Oferta'): void {
      http.expectOne(`/api/candidaturas/${id}/estado`).flush(crearDetalle({ id, estado }));
    }

    beforeEach(() => {
      service.cargar();
      esperarLista().flush(LISTA);
    });

    it('la lista se pide una vez y solo cuando han acabado los dos', () => {
      service.cambiarEstado(1, { estado: 'Enviada' }).subscribe();
      service.cambiarEstado(2, { estado: 'Oferta' }).subscribe();

      responderEstado(1, 'Enviada');
      http.expectNone(hayLista);
      // La otra tarjeta sigue donde se movió, sin volver atrás
      expect(service.candidaturas().map((c) => c.estado)).toEqual(['Enviada', 'Oferta']);

      responderEstado(2, 'Oferta');
      esperarLista().flush([
        { ...LISTA[0], estado: 'Enviada' },
        { ...LISTA[1], estado: 'Oferta' },
      ]);
      expect(service.candidaturas().map((c) => c.estado)).toEqual(['Enviada', 'Oferta']);
    });

    it('si uno falla, solo vuelve atrás ese y la lista se pide al acabar los dos', () => {
      service.cambiarEstado(1, { estado: 'Enviada' }).subscribe({ error: () => undefined });
      service.cambiarEstado(2, { estado: 'Oferta' }).subscribe();

      http.expectOne('/api/candidaturas/1/estado').flush({}, { status: 409, statusText: 'Error' });
      http.expectNone(hayLista);
      expect(service.candidaturas().map((c) => c.estado)).toEqual(['Preparada', 'Oferta']);

      responderEstado(2, 'Oferta');
      esperarLista().flush(LISTA);
    });

    it('una lista pedida antes del cambio se cancela y se vuelve a pedir después', () => {
      service.cambiarEstado(1, { estado: 'Enviada' }).subscribe();
      responderEstado(1, 'Enviada');
      const enCamino = esperarLista();

      service.cambiarEstado(2, { estado: 'Oferta' }).subscribe();

      expect(enCamino.cancelled).toBe(true);
      responderEstado(2, 'Oferta');
      esperarLista().flush([]);
    });

    it('una carga con «cargando» cancelada por un cambio no se queda cargando', () => {
      service.cargar();
      const enCamino = esperarLista();
      expect(service.cargando()).toBe(true);

      service.cambiarEstado(1, { estado: 'Enviada' }).subscribe();
      responderEstado(1, 'Enviada');

      expect(enCamino.cancelled).toBe(true);
      esperarLista().flush(LISTA);
      expect(service.cargando()).toBe(false);
    });

    it('la tarjeta solo se mueve al suscribirse y el contador se cierra aunque se abandone', () => {
      const cambio = service.cambiarEstado(1, { estado: 'Enviada' });
      expect(service.candidaturas()[0].estado).toBe('Preparada');
      http.expectNone('/api/candidaturas/1/estado');

      const suscripcion = cambio.subscribe();
      expect(service.candidaturas()[0].estado).toBe('Enviada');
      const abandonada = http.expectOne('/api/candidaturas/1/estado');
      suscripcion.unsubscribe();
      expect(abandonada.cancelled).toBe(true);

      service.editar(2, { empresa: 'Acme', puesto: 'Dev' }).subscribe();
      http.expectOne((r) => r.method === 'PUT').flush(crearDetalle({ id: 2 }));
      esperarLista().flush(LISTA);
    });
  });

  it('borra la candidatura y la quita de la lista', () => {
    service.cargar();
    esperarLista().flush([crearResumen({ id: 1 }), crearResumen({ id: 2 })]);

    service.borrar(1).subscribe();
    const peticion = http.expectOne('/api/candidaturas/1');
    expect(peticion.request.method).toBe('DELETE');
    peticion.flush(null, { status: 204, statusText: 'No Content' });

    expect(service.candidaturas().map((c) => c.id)).toEqual([2]);
  });

  it('añade y borra notas en la ficha abierta', () => {
    service.cargarDetalle(1);
    http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));

    service.anadirNota(1, 'Llamar el lunes').subscribe();
    const alta = http.expectOne('/api/candidaturas/1/notas');
    expect(alta.request.body).toEqual({ texto: 'Llamar el lunes' });
    const nota = {
      id: 9,
      candidaturaId: 1,
      texto: 'Llamar el lunes',
      fecha: '2026-10-30T09:00:00Z',
      origen: 'Manual' as const,
    };
    alta.flush(nota, { status: 201, statusText: 'Created' });
    expect(service.detalle()?.notas).toEqual([nota]);

    service.borrarNota(nota).subscribe();
    const borrado = http.expectOne('/api/notas/9');
    expect(borrado.request.method).toBe('DELETE');
    borrado.flush(null, { status: 204, statusText: 'No Content' });
    expect(service.detalle()?.notas).toEqual([]);
  });
});
