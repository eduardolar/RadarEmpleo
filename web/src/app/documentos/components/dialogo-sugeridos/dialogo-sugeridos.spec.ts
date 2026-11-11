import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { DocumentoSugerido } from '../../interfaces/documento.interface';
import { DocumentosService } from '../../services/documentos.service';
import { crearDocumento, crearSugerido } from '../../utils/documento-de-prueba';
import { DialogoSugeridos } from './dialogo-sugeridos';

describe('DialogoSugeridos', () => {
  let fixture: ComponentFixture<DialogoSugeridos>;
  let http: HttpTestingController;
  let cerrar: ReturnType<typeof vi.fn>;

  async function crear(sugeridos?: DocumentoSugerido[]): Promise<void> {
    cerrar = vi.fn();
    TestBed.configureTestingModule({
      imports: [DialogoSugeridos],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: { candidaturaId: 1, empresa: 'Marco Talento', titulo: 'Buscar el CV' },
        },
        { provide: MatDialogRef, useValue: { close: cerrar } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DialogoSugeridos);
    await fixture.whenStable();
    if (sugeridos) {
      peticionSugeridos().flush(sugeridos);
      await fixture.whenStable();
    }
  }

  afterEach(() => http.verify());

  function peticionSugeridos(): ReturnType<HttpTestingController['expectOne']> {
    return http.expectOne((r) => r.url === '/api/documentos/sugeridos');
  }

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function botonesUsar(): HTMLButtonElement[] {
    return [...elemento().querySelectorAll<HTMLButtonElement>('button')].filter(
      (b) => b.textContent!.trim() === 'Usar este',
    );
  }

  it('al abrirse busca los de la candidatura y lo dice mientras tanto', async () => {
    await crear();

    expect(peticionSugeridos().request.params.get('candidaturaId')).toBe('1');
    expect(elemento().textContent).toContain('Buscando en la carpeta Curriculum...');
    expect(elemento().textContent).toContain('Buscar el CV para Marco Talento');
  });

  it('cada fila lleva el nombre legible, la semana, la marca «Antiguo» y «Usar este»', async () => {
    await crear([
      crearSugerido(),
      crearSugerido({
        rutaRelativa: 'Archivo/_estilo_antiguo/CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf',
        carpeta: 'Archivo/_estilo_antiguo',
        esAntiguo: true,
      }),
    ]);

    const filas = [...elemento().querySelectorAll('li')];
    expect(filas).toHaveLength(2);
    expect(filas[0].textContent).toContain('CV · NET · MarcoTalento');
    expect(filas[0].textContent).toContain('31 ago - 6 sep');
    expect(filas[0].textContent).not.toContain('Antiguo');
    expect(filas[1].textContent).toContain('Antiguo');
    expect(botonesUsar()).toHaveLength(2);
    expect(botonesUsar()[0].getAttribute('aria-label')).toBe(
      'Usar este: CV · NET · MarcoTalento, 31 ago - 6 sep',
    );
  });

  function destacados(): HTMLButtonElement[] {
    return botonesUsar().filter((b) => b.hasAttribute('mat-flat-button'));
  }

  it('el «Usar este» de la primera fila va destacado y se lleva el foco (Intro lo usa)', async () => {
    await crear([
      crearSugerido(),
      crearSugerido({ rutaRelativa: 'Archivo/CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf' }),
    ]);

    expect(destacados()).toEqual([botonesUsar()[0]]);
    expect(document.activeElement).toBe(botonesUsar()[0]);
  });

  it('si la primera ya está en la ficha, el destacado es el de la siguiente', async () => {
    await crear([
      crearSugerido({ yaVinculado: true }),
      crearSugerido({ rutaRelativa: 'Archivo/CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf' }),
      crearSugerido({ rutaRelativa: 'Archivo/CV_Eduardo_Lara_Navarro_Otro.pdf' }),
    ]);

    expect(botonesUsar()).toHaveLength(2);
    expect(destacados()).toEqual([botonesUsar()[0]]);
    const filas = [...elemento().querySelectorAll('li')];
    expect(filas[1].contains(destacados()[0])).toBe(true);
  });

  it('si todas están ya en la ficha, ninguna va destacada', async () => {
    await crear([
      crearSugerido({ yaVinculado: true }),
      crearSugerido({
        rutaRelativa: 'Archivo/CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf',
        yaVinculado: true,
      }),
    ]);

    expect(elemento().querySelector('[mat-flat-button]')).toBeNull();
  });

  it('el título repite el botón que lo ha abierto', async () => {
    await crear([]);

    expect(elemento().querySelector('h2')!.textContent!.trim()).toBe(
      'Buscar el CV para Marco Talento',
    );
  });

  it('«Usar este» vincula el archivo y cierra con el documento', async () => {
    const sugerido = crearSugerido();
    await crear([sugerido]);
    const documento = crearDocumento();

    botonesUsar()[0].click();
    const peticion = http.expectOne('/api/candidaturas/1/documentos');
    expect(peticion.request.body).toEqual({ rutaRelativa: sugerido.rutaRelativa });
    peticion.flush(documento, { status: 201, statusText: 'Created' });

    expect(cerrar).toHaveBeenCalledWith(documento);
  });

  it('si el archivo ya no está, lo dice, no cierra y vuelve a buscar', async () => {
    await crear([crearSugerido()]);

    botonesUsar()[0].click();
    http
      .expectOne('/api/candidaturas/1/documentos')
      .flush(
        { detail: 'No encuentro el archivo. Puede que se haya movido.' },
        { status: 409, statusText: 'Conflict' },
      );
    peticionSugeridos().flush([]);
    await fixture.whenStable();

    expect(cerrar).not.toHaveBeenCalled();
    expect(elemento().querySelector('[role="alert"]')!.textContent).toContain(
      'No encuentro el archivo. Puede que se haya movido.',
    );
  });

  it('el que ya está en la ficha no se puede volver a usar', async () => {
    await crear([crearSugerido({ yaVinculado: true })]);

    expect(botonesUsar()).toHaveLength(0);
    expect(elemento().textContent).toContain('Ya está en la ficha');
  });

  it('sin resultados, lo dice', async () => {
    await crear([]);

    expect(elemento().textContent).toContain(
      'No he encontrado ningún CV con el nombre de esta empresa.',
    );
  });

  it('si solo hay de su semana, avisa de que ninguno lleva el nombre de la empresa', async () => {
    await crear([crearSugerido({ coincidencia: 'MismaSemana' })]);

    expect(elemento().textContent).toContain('Estos son los de su semana');
    expect(botonesUsar()).toHaveLength(1);
  });

  it('si solo hay de su semana, ningún «Usar este» va destacado ni se lleva el foco', async () => {
    // Con Intro se vincularía el CV de otra empresa
    await crear([
      crearSugerido({ coincidencia: 'MismaSemana' }),
      crearSugerido({
        coincidencia: 'MismaSemana',
        rutaRelativa:
          'Candidaturas/Semana 2026-08-31 (31 ago - 6 sep)/CV_Eduardo_Lara_Navarro_IA_Galandor.pdf',
      }),
    ]);

    expect(botonesUsar()).toHaveLength(2);
    expect(destacados()).toEqual([]);
    expect(botonesUsar()).not.toContain(document.activeElement);
  });

  it('el posterior al envío lleva su marca y no va destacado aunque sea el único', async () => {
    await crear([crearSugerido({ posteriorAlEnvio: true })]);

    const fila = elemento().querySelector('li')!;
    expect(fila.textContent).toContain('Posterior al envío');
    expect(botonesUsar()[0].getAttribute('aria-label')).toContain('posterior al envío');
    expect(destacados()).toEqual([]);
    expect(document.activeElement).not.toBe(botonesUsar()[0]);
  });

  it('con uno bueno y otro posterior al envío, el destacado es el bueno', async () => {
    await crear([
      crearSugerido(),
      crearSugerido({
        rutaRelativa: 'Candidaturas/Semana 2026-10-26 (26 oct - 1 nov)/CV.pdf',
        posteriorAlEnvio: true,
      }),
    ]);

    expect(destacados()).toEqual([botonesUsar()[0]]);
    expect(elemento().querySelectorAll('li')[0].textContent).not.toContain('Posterior al envío');
  });

  it('al cerrarse mientras busca, cancela la petición de sugerencias', async () => {
    await crear();
    const peticion = peticionSugeridos();

    fixture.destroy();

    expect(peticion.cancelled).toBe(true);
    expect(TestBed.inject(DocumentosService).cargandoSugeridos()).toBe(false);
  });

  it('un error al buscar se enseña con «Reintentar»', async () => {
    await crear();
    peticionSugeridos().flush(
      { detail: 'No se encuentra la carpeta Curriculum.' },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();

    expect(elemento().textContent).toContain('No se encuentra la carpeta Curriculum.');
    [...elemento().querySelectorAll('button')]
      .find((b) => b.textContent!.includes('Reintentar'))!
      .click();
    peticionSugeridos().flush([]);
  });
});
