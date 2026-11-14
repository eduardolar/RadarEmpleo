import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CandidaturasService } from '../../candidaturas/services/candidaturas.service';
import { crearDetalle } from '../../candidaturas/utils/candidatura-de-prueba';
import { ErrorApi } from '../../core/interfaces/error-api.interface';
import { GuardarRecordatorio } from '../interfaces/recordatorio.interface';
import {
  crearPanel,
  crearRecordatorio,
  crearRecordatorioAgenda,
} from '../utils/recordatorio-de-prueba';
import { RecordatoriosService } from './recordatorios.service';

describe('RecordatoriosService', () => {
  let service: RecordatoriosService;
  let candidaturas: CandidaturasService;
  let http: HttpTestingController;

  const DATOS: GuardarRecordatorio = {
    tipo: 'PublicacionLinkedIn',
    titulo: 'Publicar en LinkedIn',
    dia: '2026-11-17',
    hora: null,
    notas: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RecordatoriosService);
    candidaturas = TestBed.inject(CandidaturasService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function cargarPanel(panel = crearPanel()): void {
    service.cargarPanel();
    http.expectOne('/api/panel/hoy').flush(panel);
  }

  function abrirFicha(id = 1, recordatorios = [crearRecordatorio({ id: 5, candidaturaId: id })]) {
    candidaturas.cargarDetalle(id);
    http.expectOne(`/api/candidaturas/${id}`).flush(crearDetalle({ id, recordatorios }));
  }

  describe('cargarPanel', () => {
    it('la primera vez marca «cargando» y guarda el panel', () => {
      service.cargarPanel();

      expect(service.cargando()).toBe(true);
      const panel = crearPanel({ paraHoy: [crearRecordatorioAgenda()] });
      http.expectOne('/api/panel/hoy').flush(panel);

      expect(service.cargando()).toBe(false);
      expect(service.panel()).toEqual(panel);
      expect(service.error()).toBeNull();
    });

    it('si falla la primera vez, deja el mensaje', () => {
      service.cargarPanel();
      http.expectOne('/api/panel/hoy').flush(null, { status: 503, statusText: 'x' });

      expect(service.panel()).toBeNull();
      expect(service.cargando()).toBe(false);
      expect(service.error()).toContain('No se puede conectar');
    });

    it('tras un error, reintentar lo limpia y vuelve a marcar «cargando»', () => {
      service.cargarPanel();
      http.expectOne('/api/panel/hoy').flush(null, { status: 503, statusText: 'x' });

      service.cargarPanel();

      expect(service.error()).toBeNull();
      expect(service.cargando()).toBe(true);
      http.expectOne('/api/panel/hoy').flush(crearPanel());
      expect(service.panel()).not.toBeNull();
    });

    it('con un panel ya a la vista, lo refresca sin «cargando» y, si falla, se queda el que había', () => {
      const panel = crearPanel({ paraHoy: [crearRecordatorioAgenda()] });
      cargarPanel(panel);

      service.cargarPanel();

      expect(service.cargando()).toBe(false);
      http.expectOne('/api/panel/hoy').flush(null, { status: 500, statusText: 'x' });
      expect(service.panel()).toEqual(panel);
      expect(service.error()).toBeNull();
    });

    it('una segunda petición cancela la primera', () => {
      service.cargarPanel();
      const primera = http.expectOne('/api/panel/hoy');

      service.cargarPanel();

      expect(primera.cancelled).toBe(true);
      http.expectOne('/api/panel/hoy').flush(crearPanel());
    });
  });

  describe('crear', () => {
    it('suelto: POST a /api/recordatorios con el día, la hora y las notas', () => {
      let creado: unknown;
      service.crear({ ...DATOS, hora: '15:00', notas: 'Post de Angular' }).subscribe((r) => {
        creado = r;
      });

      const peticion = http.expectOne('/api/recordatorios');
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({
        tipo: 'PublicacionLinkedIn',
        titulo: 'Publicar en LinkedIn',
        dia: '2026-11-17',
        hora: '15:00',
        notas: 'Post de Angular',
      });
      const respuesta = crearRecordatorio({ id: 3 });
      peticion.flush(respuesta, { status: 201, statusText: 'Created' });

      expect(creado).toEqual(respuesta);
    });

    it('sin hora manda null', () => {
      service.crear(DATOS).subscribe();

      const peticion = http.expectOne('/api/recordatorios');
      expect(peticion.request.body.hora).toBeNull();
      peticion.flush(crearRecordatorio());
    });

    it('de una candidatura: POST a su ruta y entra en la ficha abierta por su fecha', () => {
      abrirFicha(1, [
        crearRecordatorio({ id: 5, candidaturaId: 1, fechaHora: '2026-11-20T08:00:00Z' }),
      ]);

      service.crear(DATOS, 1).subscribe();

      const peticion = http.expectOne('/api/candidaturas/1/recordatorios');
      expect(peticion.request.method).toBe('POST');
      peticion.flush(
        crearRecordatorio({ id: 6, candidaturaId: 1, fechaHora: '2026-11-17T08:00:00Z' }),
      );

      expect(candidaturas.detalle()!.recordatorios.map((r) => r.id)).toEqual([6, 5]);
    });

    it('no toca la ficha de otra candidatura ni pide el panel si nunca se ha pedido', () => {
      abrirFicha(1);

      service.crear(DATOS, 2).subscribe();
      http
        .expectOne('/api/candidaturas/2/recordatorios')
        .flush(crearRecordatorio({ id: 6, candidaturaId: 2 }));

      expect(candidaturas.detalle()!.recordatorios.map((r) => r.id)).toEqual([5]);
      // afterEach comprueba que no ha salido ningún GET /api/panel/hoy
    });

    it('con el panel ya pedido, lo refresca sin «cargando»', () => {
      cargarPanel();

      service.crear(DATOS).subscribe();
      http.expectOne('/api/recordatorios').flush(crearRecordatorio({ id: 3 }));

      expect(service.cargando()).toBe(false);
      const panel = crearPanel({ proximos: [crearRecordatorioAgenda({ id: 3 })] });
      http.expectOne('/api/panel/hoy').flush(panel);
      expect(service.panel()).toEqual(panel);
    });

    it('un 400 llega como ErrorApi con los errores por campo', () => {
      let error: ErrorApi | undefined;
      service.crear(DATOS).subscribe({ error: (e: ErrorApi) => (error = e) });

      http
        .expectOne('/api/recordatorios')
        .flush(
          { detail: 'La hora tiene que ser como 09:30.', errors: { hora: ['La hora…'] } },
          { status: 400, statusText: 'Bad Request' },
        );

      expect(error?.estado).toBe(400);
      expect(error?.errores).toEqual({ hora: ['La hora…'] });
    });
  });

  describe('editar, marcar como hecho y posponer', () => {
    const RECORDATORIO = crearRecordatorio({ id: 5, candidaturaId: 1 });

    it('editar: PUT con los datos', () => {
      service.editar(RECORDATORIO, DATOS).subscribe();

      const peticion = http.expectOne('/api/recordatorios/5');
      expect(peticion.request.method).toBe('PUT');
      expect(peticion.request.body).toEqual(DATOS);
      peticion.flush(RECORDATORIO);
    });

    it('marcarHecho: POST a /hecho con true o false', () => {
      service.marcarHecho(RECORDATORIO, true).subscribe();
      const hecho = http.expectOne('/api/recordatorios/5/hecho');
      expect(hecho.request.method).toBe('POST');
      expect(hecho.request.body).toEqual({ hecho: true });
      hecho.flush({ ...RECORDATORIO, hecho: true });

      service.marcarHecho(RECORDATORIO, false).subscribe();
      const pendiente = http.expectOne('/api/recordatorios/5/hecho');
      expect(pendiente.request.body).toEqual({ hecho: false });
      pendiente.flush(RECORDATORIO);
    });

    it('posponer: POST a /posponer con el día', () => {
      service.posponer(RECORDATORIO, '2026-11-14').subscribe();

      const peticion = http.expectOne('/api/recordatorios/5/posponer');
      expect(peticion.request.method).toBe('POST');
      expect(peticion.request.body).toEqual({ dia: '2026-11-14' });
      peticion.flush(RECORDATORIO);
    });

    it('pone al día la ficha abierta con lo que devuelve la API', () => {
      abrirFicha(1);

      service.marcarHecho(RECORDATORIO, true).subscribe();
      http
        .expectOne('/api/recordatorios/5/hecho')
        .flush({ ...RECORDATORIO, hecho: true, fechaHecho: '2026-11-13T09:00:00Z' });

      const enFicha = candidaturas.detalle()!.recordatorios;
      expect(enFicha).toHaveLength(1);
      expect(enFicha[0].hecho).toBe(true);
    });

    it('no toca la ficha abierta si es de otra candidatura', () => {
      abrirFicha(2, [crearRecordatorio({ id: 8, candidaturaId: 2 })]);

      service.marcarHecho(RECORDATORIO, true).subscribe();
      http.expectOne('/api/recordatorios/5/hecho').flush({ ...RECORDATORIO, hecho: true });

      expect(candidaturas.detalle()!.recordatorios.map((r) => r.id)).toEqual([8]);
    });

    it('refresca el panel sin «cargando»', () => {
      cargarPanel(crearPanel({ paraHoy: [crearRecordatorioAgenda({ id: 5 })] }));

      service.marcarHecho(RECORDATORIO, true).subscribe();
      http.expectOne('/api/recordatorios/5/hecho').flush({ ...RECORDATORIO, hecho: true });

      expect(service.cargando()).toBe(false);
      http.expectOne('/api/panel/hoy').flush(crearPanel());
      expect(service.panel()!.paraHoy).toEqual([]);
    });

    it('no manda la segunda operación sobre el mismo recordatorio mientras va la primera', () => {
      const terminadas: string[] = [];
      service.marcarHecho(RECORDATORIO, true).subscribe();
      expect(service.ocupado(5)).toBe(true);

      service.marcarHecho(RECORDATORIO, true).subscribe({
        next: () => terminadas.push('next'),
        complete: () => terminadas.push('complete'),
      });
      service.posponer(RECORDATORIO, '2026-11-14').subscribe();

      // Una sola petición: las otras dos han acabado sin emitir
      expect(terminadas).toEqual(['complete']);
      http.expectOne('/api/recordatorios/5/hecho').flush({ ...RECORDATORIO, hecho: true });
      expect(service.ocupado(5)).toBe(false);
    });

    it('sí deja operar a la vez sobre recordatorios distintos', () => {
      service.marcarHecho(RECORDATORIO, true).subscribe();
      service.marcarHecho(crearRecordatorio({ id: 6 }), true).subscribe();

      http.expectOne('/api/recordatorios/5/hecho').flush(RECORDATORIO);
      http.expectOne('/api/recordatorios/6/hecho').flush(crearRecordatorio({ id: 6 }));
    });

    it('tras un error deja volver a intentarlo', () => {
      service.marcarHecho(RECORDATORIO, true).subscribe({ error: () => undefined });
      http.expectOne('/api/recordatorios/5/hecho').flush(null, { status: 500, statusText: 'x' });

      expect(service.ocupado(5)).toBe(false);
      service.marcarHecho(RECORDATORIO, true).subscribe();
      http.expectOne('/api/recordatorios/5/hecho').flush(RECORDATORIO);
    });

    it.each([409, 404])('con un %i vuelve a pedir el panel y la ficha abierta', (estado) => {
      cargarPanel();
      abrirFicha(1);
      let error: ErrorApi | undefined;

      service.posponer(RECORDATORIO, '2026-11-14').subscribe({
        error: (e: ErrorApi) => (error = e),
      });
      http
        .expectOne('/api/recordatorios/5/posponer')
        .flush({ detail: 'Ya está hecho.' }, { status: estado, statusText: 'x' });

      expect(error?.estado).toBe(estado);
      http.expectOne('/api/panel/hoy').flush(crearPanel());
      http.expectOne('/api/candidaturas/1').flush(crearDetalle({ id: 1 }));
    });

    it('con un 500 no vuelve a pedir nada', () => {
      cargarPanel();

      service.posponer(RECORDATORIO, '2026-11-14').subscribe({ error: () => undefined });
      http.expectOne('/api/recordatorios/5/posponer').flush(null, { status: 500, statusText: 'x' });
      // afterEach comprueba que no ha salido ningún GET
    });
  });

  describe('borrar', () => {
    const RECORDATORIO = crearRecordatorio({ id: 5, candidaturaId: 1 });

    it('DELETE, lo quita de la ficha abierta y refresca el panel', () => {
      cargarPanel();
      abrirFicha(1);
      let borrado = false;

      service.borrar(RECORDATORIO).subscribe(() => (borrado = true));
      const peticion = http.expectOne('/api/recordatorios/5');
      expect(peticion.request.method).toBe('DELETE');
      peticion.flush(null, { status: 204, statusText: 'No Content' });

      expect(borrado).toBe(true);
      expect(candidaturas.detalle()!.recordatorios).toEqual([]);
      http.expectOne('/api/panel/hoy').flush(crearPanel());
    });

    it('un doble clic solo manda una petición', () => {
      service.borrar(RECORDATORIO).subscribe();
      service.borrar(RECORDATORIO).subscribe();

      http.expectOne('/api/recordatorios/5').flush(null, { status: 204, statusText: 'x' });
    });

    it('con un 404 (ya lo han borrado) vuelve a pedir el panel', () => {
      cargarPanel();

      service.borrar(crearRecordatorio({ id: 5 })).subscribe({ error: () => undefined });
      http.expectOne('/api/recordatorios/5').flush(null, { status: 404, statusText: 'x' });

      http.expectOne('/api/panel/hoy').flush(crearPanel());
    });
  });
});
