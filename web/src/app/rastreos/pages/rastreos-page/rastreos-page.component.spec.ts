import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { CandidaturaResumen } from '../../../candidaturas/interfaces/candidatura.interface';
import { crearResumen } from '../../../candidaturas/utils/candidatura-de-prueba';
import { AccionAviso, AvisosService } from '../../../shared/services/avisos.service';
import { RastreoDetalle, ResultadoEscaneo } from '../../interfaces/rastreo.interface';
import {
  crearDetalleRastreo,
  crearOferta,
  crearRastreo,
  crearResultadoEscaneo,
} from '../../utils/rastreo-de-prueba';
import RastreosPageComponent from './rastreos-page.component';

describe('RastreosPageComponent', () => {
  let fixture: ComponentFixture<RastreosPageComponent>;
  let http: HttpTestingController;
  let avisos: { exito: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  const DETALLE = crearDetalleRastreo({
    id: 1,
    total: 3,
    pendientes: 1,
    informe: '- **Alto · Acme — Backend** https://ejemplo.com/ofertas/1',
    ofertas: [
      crearOferta({ id: 10, linea: 1, empresa: 'Acme', puesto: 'Backend' }),
      crearOferta({ id: 11, linea: 2, estado: 'Convertida', candidaturaId: 5, puesto: 'Front' }),
      crearOferta({ id: 12, linea: 3, estado: 'Ignorada', puesto: 'Camarero' }),
    ],
  });

  async function abrir(
    resultado: ResultadoEscaneo = crearResultadoEscaneo(),
    detalle: RastreoDetalle | null = DETALLE,
    candidaturas: CandidaturaResumen[] = [],
  ): Promise<HTMLElement> {
    avisos = { exito: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      imports: [RastreosPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AvisosService, useValue: avisos },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RastreosPageComponent);
    await fixture.whenStable();

    http.expectOne('/api/candidaturas').flush(candidaturas);
    http.expectOne('/api/rastreos/escanear').flush(resultado);
    http.expectOne('/api/rastreos').flush(detalle ? [crearRastreo({ id: detalle.id })] : []);
    if (detalle) {
      http.expectOne(`/api/rastreos/${detalle.id}`).flush(detalle);
    }
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => http.verify());

  function boton(elemento: HTMLElement, texto: string): HTMLButtonElement {
    return [...elemento.querySelectorAll('button')].find((b) => b.textContent?.includes(texto))!;
  }

  it('al entrar busca informes nuevos y abre el más reciente con su informe y sus ofertas', async () => {
    const elemento = await abrir(crearResultadoEscaneo({ ofertasNuevas: 3, lineasSinLeer: 2 }));

    expect(elemento.querySelector('[role="status"]')?.textContent).toContain(
      'He encontrado 3 ofertas nuevas.',
    );
    expect(elemento.textContent).toContain('2 líneas de los informes');
    expect(elemento.querySelector('app-informe-rastreo li')?.textContent).toContain(
      'Acme — Backend',
    );
    expect(elemento.querySelectorAll('app-tarjeta-oferta')).toHaveLength(3);
    expect(elemento.textContent).toContain('Ya en el tablero (1)');
  });

  it('las ignoradas van plegadas al final', async () => {
    const elemento = await abrir();

    const plegadas = elemento.querySelector('section[aria-labelledby="titulo-ofertas"] details')!;
    expect(plegadas.hasAttribute('open')).toBe(false);
    expect(plegadas.querySelector('summary')?.textContent).toContain('No me interesan (1)');
    expect(plegadas.textContent).toContain('Camarero');
  });

  it('«Pasar al tablero» avisa con «Abrir ficha», que lleva a la candidatura', async () => {
    const elemento = await abrir();
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    boton(elemento, 'Pasar al tablero').click();
    http.expectOne('/api/rastreos/ofertas/10/detectar').flush({
      oferta: crearOferta({ id: 10, linea: 1, estado: 'Convertida', candidaturaId: 40 }),
      candidaturaId: 40,
      yaExistia: false,
    });

    expect(avisos.exito).toHaveBeenCalledWith('Añadida al tablero.', expect.anything());
    const accion = avisos.exito.mock.calls[0][1] as AccionAviso;
    expect(accion.texto).toBe('Abrir ficha');
    accion.alPulsar();
    expect(navegar).toHaveBeenCalledWith(['/candidaturas', 40]);
  });

  it('si ya estaba en el tablero, lo dice', async () => {
    const elemento = await abrir();

    boton(elemento, 'Pasar al tablero').click();
    http.expectOne('/api/rastreos/ofertas/10/detectar').flush({
      oferta: crearOferta({ id: 10, estado: 'Convertida', candidaturaId: 5 }),
      candidaturaId: 5,
      yaExistia: true,
    });

    expect(avisos.exito).toHaveBeenCalledWith('Ya estaba en el tablero.', expect.anything());
  });

  it('un error al pasarla al tablero se enseña como aviso', async () => {
    const elemento = await abrir();

    boton(elemento, 'Pasar al tablero').click();
    http
      .expectOne('/api/rastreos/ofertas/10/detectar')
      .flush({ detail: 'No se ha podido.' }, { status: 409, statusText: 'Conflict' });

    expect(avisos.error).toHaveBeenCalledWith('No se ha podido.');
  });

  it('«No me interesa» la manda a las plegadas', async () => {
    const elemento = await abrir();

    boton(elemento, 'No me interesa').click();
    await fixture.whenStable();

    expect(
      elemento.querySelector('section[aria-labelledby="titulo-ofertas"] details summary')
        ?.textContent,
    ).toContain('No me interesan (2)');
    http
      .expectOne('/api/rastreos/ofertas/10/ignorar')
      .flush(crearOferta({ id: 10, linea: 1, estado: 'Ignorada' }));
  });

  it('«Volver a buscar» escanea otra vez', async () => {
    const elemento = await abrir();

    boton(elemento, 'Volver a buscar').click();

    http.expectOne('/api/candidaturas').flush([]);
    http.expectOne('/api/rastreos/escanear').flush(crearResultadoEscaneo());
    http.expectOne('/api/rastreos').flush([crearRastreo({ id: 1 })]);
    http.expectOne('/api/rastreos/1').flush(DETALLE);
  });

  it('sin rastreos lo explica', async () => {
    const elemento = await abrir(crearResultadoEscaneo(), null);

    expect(elemento.textContent).toContain('Todavía no hay rastreos.');
    expect(elemento.querySelector('mat-select')).toBeNull();
  });

  it('si no encuentra la carpeta, lo dice y enseña lo leído otras veces', async () => {
    avisos = { exito: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      imports: [RastreosPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AvisosService, useValue: avisos },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RastreosPageComponent);
    await fixture.whenStable();

    http.expectOne('/api/candidaturas').flush([]);
    http
      .expectOne('/api/rastreos/escanear')
      .flush(
        { detail: 'No encuentro tu carpeta Curriculum.' },
        { status: 409, statusText: 'Conflict' },
      );
    http.expectOne('/api/rastreos').flush([crearRastreo({ id: 1 })]);
    http.expectOne('/api/rastreos/1').flush(DETALLE);
    await fixture.whenStable();

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.querySelector('[role="alert"]')?.textContent).toContain(
      'No encuentro tu carpeta Curriculum.',
    );
    expect(elemento.querySelectorAll('app-tarjeta-oferta')).toHaveLength(3);
  });

  it('la pasada al tablero se queda en su sitio con «En el tablero · Abrir ficha»', async () => {
    const elemento = await abrir();

    boton(elemento, 'Pasar al tablero').click();
    http.expectOne('/api/rastreos/ofertas/10/detectar').flush({
      oferta: crearOferta({
        id: 10,
        linea: 1,
        empresa: 'Acme',
        puesto: 'Backend',
        estado: 'Convertida',
        candidaturaId: 40,
      }),
      candidaturaId: 40,
      yaExistia: false,
    });
    await fixture.whenStable();

    const primera = elemento.querySelector('app-tarjeta-oferta')!;
    expect(primera.textContent).toContain('Backend');
    expect(primera.querySelector('a[href="/candidaturas/40"]')?.textContent).toContain(
      'Abrir ficha',
    );
    // Sigue arriba: el grupo de abajo no crece
    expect(elemento.textContent).toContain('Ya en el tablero (1)');
    expect(elemento.textContent).toContain('Ya has mirado todas las ofertas de este rastreo.');
  });

  it('avisa si en el tablero hay una candidatura de la misma empresa (las del registro no tienen enlace)', async () => {
    const elemento = await abrir(crearResultadoEscaneo(), DETALLE, [
      crearResumen({
        id: 77,
        empresa: 'ACME, S.L.',
        puesto: 'Backend .NET',
        estado: 'Enviada',
        enlace: null,
      }),
      crearResumen({ id: 78, empresa: 'Globex', puesto: 'Front' }),
    ]);

    const tarjeta = elemento.querySelector('app-tarjeta-oferta')!;
    expect(tarjeta.textContent).toContain('Puede que ya la tengas.');
    expect(tarjeta.textContent).toContain('una candidatura de Acme');
    expect(tarjeta.querySelector('a[href="/candidaturas/77"]')?.textContent).toContain(
      'Backend .NET · Enviada',
    );
    expect(tarjeta.querySelector('a[href="/candidaturas/78"]')).toBeNull();
  });

  it('«No me interesan las que quedan» aparta todas las pendientes de una vez', async () => {
    const detalle = crearDetalleRastreo({
      id: 1,
      pendientes: 3,
      ofertas: [
        crearOferta({ id: 20, linea: 1, puesto: 'Uno' }),
        crearOferta({ id: 21, linea: 2, puesto: 'Dos' }),
        crearOferta({ id: 22, linea: 3, puesto: 'Tres' }),
      ],
    });
    const elemento = await abrir(crearResultadoEscaneo(), detalle);

    boton(elemento, 'No me interesan las que quedan (3)').click();
    for (const id of [20, 21, 22]) {
      http
        .expectOne(`/api/rastreos/ofertas/${id}/ignorar`)
        .flush(crearOferta({ id, linea: id - 19, estado: 'Ignorada' }));
    }
    await fixture.whenStable();

    expect(avisos.exito).toHaveBeenCalledWith('3 ofertas movidas a «No me interesan».');
    expect(elemento.textContent).toContain('No me interesan (3)');
    expect(boton(elemento, 'No me interesan las que quedan')).toBeUndefined();
  });

  it('con una sola pendiente no ofrece apartar «las que quedan»', async () => {
    const elemento = await abrir();

    expect(boton(elemento, 'No me interesan las que quedan')).toBeUndefined();
  });

  it('todo mirado: lleva al rastreo más reciente que aún tiene ofertas nuevas', async () => {
    avisos = { exito: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      imports: [RastreosPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AvisosService, useValue: avisos },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RastreosPageComponent);
    await fixture.whenStable();

    http.expectOne('/api/candidaturas').flush([]);
    http.expectOne('/api/rastreos/escanear').flush(crearResultadoEscaneo());
    http
      .expectOne('/api/rastreos')
      .flush([
        crearRastreo({ id: 2, fecha: '2026-11-06', pendientes: 0 }),
        crearRastreo({ id: 1, fecha: '2026-10-30', pendientes: 3 }),
      ]);
    http.expectOne('/api/rastreos/2').flush(
      crearDetalleRastreo({
        id: 2,
        pendientes: 0,
        ofertas: [crearOferta({ id: 30, rastreoId: 2, estado: 'Ignorada' })],
      }),
    );
    await fixture.whenStable();
    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.textContent).toContain('En el rastreo del 30/10 (viernes) quedan');
    expect(elemento.textContent).toContain('3 nuevas.');
    boton(elemento, 'Verlas').click();
    http.expectOne('/api/rastreos/1').flush(DETALLE);
  });
});
