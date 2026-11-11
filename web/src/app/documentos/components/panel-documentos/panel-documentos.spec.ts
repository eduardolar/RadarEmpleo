import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatTabGroup } from '@angular/material/tabs';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';
import { CandidaturaDetalle } from '../../../candidaturas/interfaces/candidatura.interface';
import { crearDetalle } from '../../../candidaturas/utils/candidatura-de-prueba';
import { AvisosService } from '../../../shared/services/avisos.service';
import { Documento } from '../../interfaces/documento.interface';
import { crearDocumento } from '../../utils/documento-de-prueba';
import { PanelDocumentos, textoAnadido } from './panel-documentos';

describe('PanelDocumentos', () => {
  let fixture: ComponentFixture<PanelDocumentos>;
  let http: HttpTestingController;
  let abrirDialogo: ReturnType<typeof vi.fn>;
  let avisos: { exito: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  async function crear(
    documentos: Documento[],
    { escritorio = true, alCerrar = undefined as Documento | undefined } = {},
  ): Promise<void> {
    abrirDialogo = vi.fn(() => ({ afterClosed: () => of(alCerrar) }));
    avisos = { exito: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      imports: [PanelDocumentos],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialog, useValue: { open: abrirDialogo } },
        { provide: AvisosService, useValue: avisos },
        {
          provide: BreakpointObserver,
          useValue: {
            isMatched: () => escritorio,
            observe: () => of({ matches: escritorio, breakpoints: {} }),
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PanelDocumentos);
    fixture.componentRef.setInput('candidatura', ficha(documentos));
    await fixture.whenStable();
  }

  function ficha(documentos: Documento[]): CandidaturaDetalle {
    return crearDetalle({ id: 1, empresa: 'Marco Talento', documentos });
  }

  afterEach(() => http.verify());

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function boton(texto: string): HTMLButtonElement | undefined {
    return [...elemento().querySelectorAll<HTMLButtonElement>('button')].find(
      (b) => b.textContent!.trim() === texto,
    );
  }

  function iframe(): HTMLIFrameElement | null {
    return elemento().querySelector('iframe');
  }

  it('sin documentos, un solo botón «Buscar el CV» que abre las sugerencias', async () => {
    await crear([]);

    expect(iframe()).toBeNull();
    expect(elemento().querySelector('[role="tab"]')).toBeNull();
    boton('Buscar el CV')!.click();

    expect(abrirDialogo).toHaveBeenCalledOnce();
    expect(abrirDialogo.mock.calls[0][1].data).toEqual({
      candidaturaId: 1,
      empresa: 'Marco Talento',
      titulo: 'Buscar el CV',
    });
  });

  it('con documentos, el diálogo se titula como su botón, «Buscar otro documento»', async () => {
    await crear([crearDocumento()]);

    boton('Buscar otro documento')!.click();

    expect(abrirDialogo.mock.calls[0][1].data.titulo).toBe('Buscar otro documento');
  });

  it('en escritorio, el PDF vigente se ve en un iframe con título y enlace a otra pestaña', async () => {
    await crear([crearDocumento({ id: 4 })]);

    const visor = iframe()!;
    expect(visor.getAttribute('src')).toBe('/api/documentos/4/archivo#view=FitH');
    expect(visor.title).toBe('CV para Marco Talento, PDF');
    expect(visor.hasAttribute('sandbox')).toBe(false);
    const enlace = [...elemento().querySelectorAll('a')].find((a) =>
      a.textContent!.includes('Abrir en otra pestaña'),
    )!;
    expect(enlace.getAttribute('href')).toBe('/api/documentos/4/archivo');
    expect(enlace.getAttribute('target')).toBe('_blank');
    expect(elemento().textContent).toContain('CV · NET · MarcoTalento');
    expect(elemento().textContent).toContain('31 ago - 6 sep');
  });

  it('en el móvil, solo el nombre y la carpeta: el iframe ni se crea', async () => {
    await crear([crearDocumento()], { escritorio: false });

    expect(iframe()).toBeNull();
    expect(elemento().textContent).toContain('CV · NET · MarcoTalento');
    expect(elemento().textContent).toContain('31 ago - 6 sep');
  });

  it('solo hay pestañas de los tipos que tienen documento', async () => {
    await crear([
      crearDocumento({ id: 1, tipo: 'CV' }),
      crearDocumento({
        id: 2,
        tipo: 'Carta',
        nombreArchivo: 'Carta_Presentacion_Eduardo_Lara_Navarro_MarcoTalento.pdf',
      }),
    ]);

    const pestanas = [...elemento().querySelectorAll('[role="tab"]')].map((t) =>
      t.textContent!.trim(),
    );
    expect(pestanas).toEqual(['CV', 'Carta']);
  });

  it('con solo una carta, sale solo su pestaña', async () => {
    await crear([crearDocumento({ tipo: 'Carta' })]);

    const pestanas = [...elemento().querySelectorAll('[role="tab"]')].map((t) =>
      t.textContent!.trim(),
    );
    expect(pestanas).toEqual(['Carta']);
    expect(iframe()!.title).toBe('Carta para Marco Talento, PDF');
  });

  it('si el archivo no está, lo dice y no pinta el iframe (que enseñaría un error en JSON)', async () => {
    await crear([crearDocumento({ encontrado: false })]);

    expect(iframe()).toBeNull();
    expect(elemento().textContent).toContain('No encuentro el archivo. Puede que se haya movido.');
  });

  it('un .docx no tiene visor: se ofrece descargarlo', async () => {
    await crear([
      crearDocumento({
        id: 3,
        extension: 'docx',
        nombreArchivo: 'CV_Eduardo_Lara_Navarro_NET_MarcoTalento.docx',
      }),
    ]);

    expect(iframe()).toBeNull();
    expect(elemento().textContent).toContain('Es un documento de Word.');
    const descargar = [...elemento().querySelectorAll('a')].find((a) =>
      a.textContent!.includes('Descargar'),
    )!;
    expect(descargar.getAttribute('href')).toBe('/api/documentos/3/archivo');
    expect(descargar.hasAttribute('download')).toBe(true);
  });

  function otros(): HTMLElement {
    return elemento().querySelector('h3')!.parentElement!;
  }

  const ANTERIOR = { id: 2, vigente: false, nombreArchivo: 'CV_Eduardo_Lara_Navarro_Viejo.pdf' };

  it('los CV anteriores y otros tipos van en «Otros documentos»', async () => {
    await crear([crearDocumento({ id: 1, vigente: true }), crearDocumento(ANTERIOR)]);

    expect(otros().textContent).toContain('Otros documentos');
    expect(otros().textContent).toContain('CV · Viejo');
  });

  it('en escritorio, los de «Otros documentos» se abren en otra pestaña', async () => {
    await crear([crearDocumento({ id: 1 }), crearDocumento(ANTERIOR)]);

    const abrir = otros().querySelector('a')!;
    expect(abrir.textContent!.trim()).toBe('Abrir');
    expect(abrir.getAttribute('href')).toBe('/api/documentos/2/archivo');
    expect(abrir.getAttribute('target')).toBe('_blank');
    expect(abrir.getAttribute('aria-label')).toBe('Abrir en otra pestaña: CV · Viejo');
  });

  it('en el móvil, los PDF de «Otros documentos» no se abren; un Word sí se descarga', async () => {
    await crear(
      [
        crearDocumento({ id: 1 }),
        crearDocumento(ANTERIOR),
        crearDocumento({
          id: 3,
          tipo: 'GuiaEntrevista',
          extension: 'docx',
          nombreArchivo: 'Guia_Entrevista_MarcoTalento.docx',
        }),
      ],
      { escritorio: false },
    );

    const enlaces = [...otros().querySelectorAll('a')];
    expect(enlaces.map((a) => a.textContent!.trim())).toEqual(['Descargar']);
    expect(enlaces[0].getAttribute('href')).toBe('/api/documentos/3/archivo');
  });

  it('si el archivo de «Otros documentos» no está, no se ofrece abrirlo', async () => {
    await crear([crearDocumento({ id: 1 }), crearDocumento({ ...ANTERIOR, encontrado: false })]);

    expect(otros().querySelector('a')).toBeNull();
    expect(otros().textContent).toContain('No encuentro el archivo');
  });

  it('al recargar la ficha con el mismo PDF, el iframe no vuelve a pedirlo', async () => {
    const documento = crearDocumento({ id: 4 });
    await crear([documento]);
    const antes = iframe();

    fixture.componentRef.setInput('candidatura', ficha([{ ...documento }]));
    await fixture.whenStable();

    expect(iframe()).toBe(antes);
    expect(iframe()!.getAttribute('src')).toBe('/api/documentos/4/archivo#view=FitH');
  });

  it('«Quitar de la ficha» borra el vínculo, avisa de que el archivo sigue y deja deshacerlo', async () => {
    const documento = crearDocumento({ id: 4 });
    await crear([documento]);
    const quitar = boton('Quitar de la ficha')!;
    expect(quitar.getAttribute('aria-label')).toContain('el archivo no se borra');

    quitar.click();
    http.expectOne('/api/documentos/4').flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    expect(avisos.exito).toHaveBeenCalledWith(
      'He quitado el CV de la ficha. El archivo sigue en su carpeta.',
      expect.objectContaining({ texto: 'Deshacer' }),
    );

    // «Deshacer» lo vuelve a añadir, con su mismo tipo
    avisos.exito.mock.calls[0][1].alPulsar();
    const peticion = http.expectOne('/api/candidaturas/1/documentos');
    expect(peticion.request.body).toEqual({ rutaRelativa: documento.rutaRelativa, tipo: 'CV' });
    peticion.flush(crearDocumento({ id: 5 }), { status: 201, statusText: 'Created' });

    expect(avisos.exito).toHaveBeenLastCalledWith('El CV vuelve a estar en la ficha.');
  });

  it('si «Deshacer» falla, enseña el error', async () => {
    await crear([crearDocumento({ id: 4 })]);

    boton('Quitar de la ficha')!.click();
    http.expectOne('/api/documentos/4').flush(null, { status: 204, statusText: 'No Content' });
    avisos.exito.mock.calls[0][1].alPulsar();
    http
      .expectOne('/api/candidaturas/1/documentos')
      .flush(
        { detail: 'No encuentro el archivo. Puede que se haya movido.' },
        { status: 409, statusText: 'Conflict' },
      );

    expect(avisos.error).toHaveBeenCalledWith('No encuentro el archivo. Puede que se haya movido.');
  });

  it('al quitar un CV anterior no se ofrece deshacer (volvería como vigente)', async () => {
    await crear([crearDocumento({ id: 1 }), crearDocumento(ANTERIOR)]);

    [...otros().querySelectorAll('button')]
      .find((b) => b.textContent!.trim() === 'Quitar de la ficha')!
      .click();
    http.expectOne('/api/documentos/2').flush(null, { status: 204, statusText: 'No Content' });

    expect(avisos.exito).toHaveBeenCalledWith(
      'He quitado el CV anterior de la ficha. El archivo sigue en su carpeta.',
    );
  });

  it('al quitar uno cuyo archivo ya no está, tampoco (no se podría volver a añadir)', async () => {
    await crear([crearDocumento({ id: 4, encontrado: false })]);

    boton('Quitar de la ficha')!.click();
    http.expectOne('/api/documentos/4').flush(null, { status: 204, statusText: 'No Content' });

    expect(avisos.exito).toHaveBeenCalledWith(
      'He quitado el CV de la ficha. El archivo sigue en su carpeta.',
    );
  });

  it('si quitar falla, enseña el error', async () => {
    await crear([crearDocumento({ id: 4 })]);

    boton('Quitar de la ficha')!.click();
    http
      .expectOne('/api/documentos/4')
      .flush({ detail: 'No se ha encontrado.' }, { status: 404, statusText: 'Not Found' });

    expect(avisos.error).toHaveBeenCalledWith('No se ha encontrado.');
  });

  it('al vincular desde el diálogo, avisa', async () => {
    await crear([crearDocumento()], { alCerrar: crearDocumento({ id: 9, tipo: 'Carta' }) });

    boton('Buscar otro documento')!.click();

    expect(avisos.exito).toHaveBeenCalledWith('La carta ya está en la ficha.');
  });

  function titulo(): HTMLElement {
    return elemento().querySelector<HTMLElement>('#titulo-documentos')!;
  }

  it('al usar uno en la ficha vacía, el foco va al título del panel («Buscar el CV» desaparece)', async () => {
    await crear([], { alCerrar: crearDocumento({ id: 9 }) });

    boton('Buscar el CV')!.click();
    // La ficha se recarga con el CV y el botón ya no está
    fixture.componentRef.setInput('candidatura', ficha([crearDocumento({ id: 9 })]));
    await fixture.whenStable();

    expect(boton('Buscar el CV')).toBeUndefined();
    expect(document.activeElement).toBe(titulo());
  });

  it('con documentos, al usar otro el foco no se mueve (el diálogo lo devuelve a su botón)', async () => {
    await crear([crearDocumento()], { alCerrar: crearDocumento({ id: 9, tipo: 'Carta' }) });
    const buscar = boton('Buscar otro documento')!;
    buscar.focus();

    buscar.click();
    await fixture.whenStable();

    expect(document.activeElement).toBe(buscar);
  });

  it('tras «Deshacer», el foco va al título del panel', async () => {
    await crear([crearDocumento({ id: 4 })]);
    boton('Quitar de la ficha')!.click();
    http.expectOne('/api/documentos/4').flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    boton('Buscar otro documento')!.focus(); // el foco está en otro sitio, como en el aviso

    avisos.exito.mock.calls[0][1].alPulsar();
    http
      .expectOne('/api/candidaturas/1/documentos')
      .flush(crearDocumento({ id: 5 }), { status: 201, statusText: 'Created' });
    await fixture.whenStable();

    expect(document.activeElement).toBe(titulo());
  });

  it('sin la carpeta Curriculum, un solo aviso y no «Puede que se haya movido» en cada documento', async () => {
    await crear([]);
    fixture.componentRef.setInput(
      'candidatura',
      crearDetalle({
        id: 1,
        empresa: 'Marco Talento',
        hayCarpetaCurriculum: false,
        documentos: [
          crearDocumento({ id: 1, encontrado: false }),
          crearDocumento({ id: 2, tipo: 'Carta', encontrado: false }),
          crearDocumento({ ...ANTERIOR, id: 3, encontrado: false }),
        ],
      }),
    );
    await fixture.whenStable();

    const texto = elemento().textContent!;
    expect(texto.match(/No encuentro tu carpeta Curriculum/g)).toHaveLength(1);
    expect(texto).not.toContain('Puede que se haya movido');
    expect(texto).not.toContain('No encuentro el archivo');
    expect(iframe()).toBeNull();
  });

  it('con la carpeta, el aviso de la carpeta no sale', async () => {
    await crear([crearDocumento({ encontrado: false })]);

    expect(elemento().textContent).not.toContain('No encuentro tu carpeta Curriculum');
    expect(elemento().textContent).toContain('No encuentro el archivo. Puede que se haya movido.');
  });

  it('las pestañas conservan su contenido: al volver al CV no se vuelve a descargar el PDF', async () => {
    await crear([crearDocumento({ id: 1, tipo: 'CV' }), crearDocumento({ id: 2, tipo: 'Carta' })]);
    const visorCv = iframe()!;
    const pestanas = [...elemento().querySelectorAll<HTMLElement>('[role="tab"]')];

    pestanas[1].click();
    await fixture.whenStable();
    pestanas[0].click();
    await fixture.whenStable();

    // jsdom no termina el cambio de pestaña, así que se mira la opción en el propio MatTabGroup
    const grupo = fixture.debugElement.query(By.directive(MatTabGroup))
      .componentInstance as MatTabGroup;
    expect(grupo.preserveContent).toBe(true);
    expect(visorCv.isConnected).toBe(true);
    expect(visorCv.getAttribute('src')).toBe('/api/documentos/1/archivo#view=FitH');
  });

  it('al cambiar el CV vigente, la pestaña es la misma y el visor enseña el nuevo, sin aviso NG0956', async () => {
    const aviso = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    await crear([crearDocumento({ id: 1 })]);
    const pestana = elemento().querySelector('[role="tab"]');

    fixture.componentRef.setInput(
      'candidatura',
      ficha([crearDocumento({ id: 2 }), crearDocumento({ ...ANTERIOR, id: 1 })]),
    );
    await fixture.whenStable();

    expect(elemento().querySelector('[role="tab"]')).toBe(pestana);
    expect(iframe()!.getAttribute('src')).toBe('/api/documentos/2/archivo#view=FitH');
    expect(aviso.mock.calls.flat().join(' ')).not.toContain('NG0956');
    aviso.mockRestore();
  });

  describe('textoAnadido', () => {
    it('el primero de su tipo', () => {
      expect(textoAnadido(crearDocumento(), [])).toBe('El CV ya está en la ficha.');
    });

    it('si sustituye al vigente, dice a dónde va el anterior', () => {
      const antes = [crearDocumento({ id: 1 })];

      expect(textoAnadido(crearDocumento({ id: 2 }), antes)).toBe(
        'El CV ya está en la ficha. El anterior pasa a «Otros documentos».',
      );
      expect(
        textoAnadido(crearDocumento({ id: 3, tipo: 'Carta' }), [
          ...antes,
          crearDocumento({ id: 4, tipo: 'Carta' }),
        ]),
      ).toBe('La carta ya está en la ficha. La anterior pasa a «Otros documentos».');
    });

    it('los tipos sin pestaña dicen dónde se ven', () => {
      expect(textoAnadido(crearDocumento({ tipo: 'GuiaEntrevista' }), [])).toBe(
        'La guía de entrevista ya está en la ficha, en «Otros documentos».',
      );
    });
  });
});
