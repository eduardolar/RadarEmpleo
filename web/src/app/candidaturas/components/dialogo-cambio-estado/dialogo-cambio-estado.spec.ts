import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { of, Subject } from 'rxjs';
import { DatosCambioEstado, DialogoCambioEstado } from './dialogo-cambio-estado';

describe('DialogoCambioEstado', () => {
  let fixture: ComponentFixture<DialogoCambioEstado>;
  let cerrar: ReturnType<typeof vi.fn>;
  let abrirConfirmacion: ReturnType<typeof vi.fn>;
  let teclas: Subject<KeyboardEvent>;
  let clicFuera: Subject<MouseEvent>;

  async function crear(datos: DatosCambioEstado): Promise<void> {
    cerrar = vi.fn();
    abrirConfirmacion = vi.fn();
    teclas = new Subject();
    clicFuera = new Subject();
    TestBed.configureTestingModule({
      imports: [DialogoCambioEstado],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: datos },
        {
          provide: MatDialogRef,
          useValue: { close: cerrar, keydownEvents: () => teclas, backdropClick: () => clicFuera },
        },
      ],
    });
    // overrideProvider: MatDialogModule, importado por el componente, trae su propio MatDialog
    TestBed.overrideProvider(MatDialog, { useValue: { open: abrirConfirmacion } });
    fixture = TestBed.createComponent(DialogoCambioEstado);
    await fixture.whenStable();
  }

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function enviar(): Promise<void> {
    elemento().querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await fixture.whenStable();
  }

  const DESCARTAR: DatosCambioEstado = {
    empresa: 'Acme',
    puesto: 'Desarrollador Angular',
    actual: 'Enviada',
    destino: 'Descartada',
  };

  afterEach(() => vi.useRealTimers());

  it('al descartar no se cierra sin motivo y explica que hace falta', async () => {
    await crear(DESCARTAR);

    await enviar();

    expect(cerrar).not.toHaveBeenCalled();
    expect(elemento().textContent).toContain(
      'Para descartar una candidatura hay que indicar el motivo.',
    );
  });

  it('al descartar enseña todos los motivos en español para elegir de un toque', async () => {
    await crear(DESCARTAR);

    const motivos = elemento().querySelectorAll('mat-radio-button');

    expect(motivos).toHaveLength(10);
    expect(motivos[1].textContent).toContain('Inglés');
    expect(motivos[9].textContent).toContain('Otro');
  });

  it('al elegir el motivo con un toque ya se puede descartar', async () => {
    await crear(DESCARTAR);

    elemento().querySelectorAll<HTMLInputElement>('mat-radio-button input')[1].click();
    await enviar();

    expect(cerrar).toHaveBeenCalledWith({
      estado: 'Descartada',
      comentario: null,
      motivoDescarte: 'Ingles',
      motivoDescarteTexto: null,
    });
  });

  it('si falta el motivo, el error se anuncia y el foco va a los motivos', async () => {
    await crear(DESCARTAR);

    await enviar();

    expect(elemento().querySelector('[role="alert"]')?.textContent).toContain('hay que indicar');
    expect(document.activeElement?.closest('mat-radio-group')).not.toBeNull();
  });

  it('al descartar con motivo devuelve la petición con motivoDescarteTexto y el comentario', async () => {
    await crear(DESCARTAR);
    const formulario = fixture.componentInstance['formulario'];
    formulario.setValue({
      motivoDescarte: 'Ingles',
      motivoDescarteTexto: '  Piden C1  ',
      comentario: '',
    });

    await enviar();

    expect(cerrar).toHaveBeenCalledWith({
      estado: 'Descartada',
      comentario: null,
      motivoDescarte: 'Ingles',
      motivoDescarteTexto: 'Piden C1',
    });
  });

  it('al cerrar explica cómo volver, no pide motivo y deja comentar', async () => {
    await crear({ ...DESCARTAR, destino: 'Cerrada' });

    expect(elemento().querySelector('h2')?.textContent).toContain('Cerrar candidatura');
    expect(elemento().textContent).toContain('«Reabrir» la devuelve a Detectada');
    expect(elemento().querySelector('mat-radio-button')).toBeNull();
    fixture.componentInstance['formulario'].controls.comentario.setValue('  La han retirado ');

    await enviar();

    expect(cerrar).toHaveBeenCalledWith({ estado: 'Cerrada', comentario: 'La han retirado' });
  });

  it('para otros estados no pide motivo y el comentario es opcional', async () => {
    await crear({ ...DESCARTAR, actual: 'Preparada', destino: 'Enviada' });

    expect(elemento().querySelector('mat-radio-button')).toBeNull();
    expect(elemento().querySelector('h2')?.textContent).toContain('Marcar enviada');

    await enviar();

    expect(cerrar).toHaveBeenCalledWith({ estado: 'Enviada', comentario: null });
  });

  describe('salir sin confirmar', () => {
    function pulsarEsc(): void {
      teclas.next(new KeyboardEvent('keydown', { key: 'Escape' }));
    }

    function escribirComentario(): void {
      const comentario = elemento().querySelector<HTMLTextAreaElement>(
        '[formcontrolname="comentario"]',
      )!;
      comentario.value = 'Piden C1';
      comentario.dispatchEvent(new Event('input'));
    }

    /** La respuesta de la confirmación «¿Salir sin guardar?» */
    function responderConfirmacion(respuesta: boolean): void {
      abrirConfirmacion.mockReturnValue({ afterClosed: () => of(respuesta) });
    }

    function botonCancelar(): HTMLButtonElement {
      return elemento().querySelector<HTMLButtonElement>('mat-dialog-actions button')!;
    }

    it('Esc sin nada escrito cierra sin preguntar', async () => {
      await crear(DESCARTAR);

      pulsarEsc();

      expect(abrirConfirmacion).not.toHaveBeenCalled();
      expect(cerrar).toHaveBeenCalledWith();
    });

    it('Esc con algo escrito pregunta y no cierra si se decide seguir', async () => {
      await crear(DESCARTAR);
      escribirComentario();
      responderConfirmacion(false);

      pulsarEsc();

      expect(abrirConfirmacion).toHaveBeenCalledTimes(1);
      expect(cerrar).not.toHaveBeenCalled();
    });

    it('elegir un motivo también cuenta como algo que se puede perder', async () => {
      await crear(DESCARTAR);
      elemento().querySelectorAll<HTMLInputElement>('mat-radio-button input')[1].click();
      responderConfirmacion(false);

      pulsarEsc();

      expect(abrirConfirmacion).toHaveBeenCalledTimes(1);
      expect(cerrar).not.toHaveBeenCalled();
    });

    it('al confirmar la salida, cierra sin mandar nada', async () => {
      await crear(DESCARTAR);
      escribirComentario();
      responderConfirmacion(true);

      pulsarEsc();

      expect(cerrar).toHaveBeenCalledWith();
    });

    it('Cancelar pregunta igual que Esc', async () => {
      await crear(DESCARTAR);
      escribirComentario();
      responderConfirmacion(false);

      botonCancelar().click();

      expect(abrirConfirmacion).toHaveBeenCalledTimes(1);
      expect(cerrar).not.toHaveBeenCalled();
    });

    it('el segundo clic del doble clic que lo abrió no lo cierra', async () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      await crear(DESCARTAR);

      vi.setSystemTime(Date.now() + 300);
      clicFuera.next(new MouseEvent('click'));

      expect(cerrar).not.toHaveBeenCalled();
    });

    it('pasado ese margen, el clic fuera cierra como siempre', async () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      await crear(DESCARTAR);

      vi.setSystemTime(Date.now() + 500);
      clicFuera.next(new MouseEvent('click'));

      expect(cerrar).toHaveBeenCalledWith();
    });
  });
});
