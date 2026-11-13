import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { AvisosService, DURACION_DESHACER_MS } from '../../services/avisos.service';

describe('AvisoDeshacer', () => {
  let deshacer: ReturnType<typeof vi.fn<() => boolean>>;
  let nota: ReturnType<typeof vi.fn<() => void>>;
  let dialogosAbiertos: unknown[];

  beforeEach(() => {
    deshacer = vi.fn<() => boolean>(() => true);
    nota = vi.fn<() => void>();
    dialogosAbiertos = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: MatDialog,
          useValue: {
            get openDialogs() {
              return dialogosAbiertos;
            },
          },
        },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(MatSnackBar).dismiss();
    document.querySelectorAll('[data-prueba]').forEach((elemento) => elemento.remove());
  });

  async function abrir(conNota = false): Promise<HTMLElement> {
    TestBed.inject(AvisosService).deshacible(
      '«Backend .NET» pasa a Enviada.',
      deshacer,
      conNota ? { texto: 'Añadir nota', alPulsar: nota } : undefined,
    );
    TestBed.tick();
    await firstValueFrom(TestBed.inject(MatSnackBar)._openedSnackBarRef!.afterOpened());
    return document.querySelector<HTMLElement>('app-aviso-deshacer')!;
  }

  function botones(aviso: HTMLElement): HTMLButtonElement[] {
    return [...aviso.querySelectorAll('button')];
  }

  function pulsarCtrlZ(destino: EventTarget = document.body): KeyboardEvent {
    const evento = new KeyboardEvent('keydown', {
      key: 'z',
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    });
    destino.dispatchEvent(evento);
    return evento;
  }

  /** Un elemento de prueba en la página (se quita al acabar) */
  function crear(html: string): HTMLElement {
    const contenedor = document.createElement('div');
    contenedor.dataset['prueba'] = '';
    contenedor.innerHTML = html;
    document.body.append(contenedor);
    return contenedor.querySelector<HTMLElement>('[data-destino]')!;
  }

  it('sale con el mensaje y «Deshacer», que se puede alcanzar con el teclado', async () => {
    const aviso = await abrir();

    expect(aviso.textContent).toContain('«Backend .NET» pasa a Enviada.');
    const [boton] = botones(aviso);
    expect(boton.textContent?.trim()).toBe('Deshacer');
    expect(boton.tabIndex).toBe(0);
    expect(boton.getAttribute('aria-keyshortcuts')).toBe('Control+Z');
  });

  it('dura unos 8 segundos y se anuncia al lector de pantalla', async () => {
    await abrir();

    const referencia = TestBed.inject(MatSnackBar)._openedSnackBarRef!;
    expect(referencia.containerInstance.snackBarConfig.duration).toBe(DURACION_DESHACER_MS);
    expect(DURACION_DESHACER_MS).toBe(8000);
    expect(document.querySelector('.mat-mdc-snack-bar-container [aria-live]')).not.toBeNull();
  });

  it('al pulsar «Deshacer» deshace y cierra el aviso', async () => {
    const aviso = await abrir();
    const cerrado = firstValueFrom(
      TestBed.inject(MatSnackBar)._openedSnackBarRef!.afterDismissed(),
    );

    botones(aviso)[0].click();

    expect(deshacer).toHaveBeenCalledTimes(1);
    expect(await cerrado).toEqual({ dismissedByAction: true });
  });

  it('la otra acción va después y no deshace', async () => {
    const aviso = await abrir(true);

    expect(botones(aviso).map((b) => b.textContent?.trim())).toEqual(['Deshacer', 'Añadir nota']);
    botones(aviso)[1].click();

    expect(nota).toHaveBeenCalledTimes(1);
    expect(deshacer).not.toHaveBeenCalled();
  });

  it('Ctrl+Z deshace mientras se ve', async () => {
    await abrir();

    const evento = pulsarCtrlZ();

    expect(deshacer).toHaveBeenCalledTimes(1);
    expect(evento.defaultPrevented).toBe(true);
  });

  describe('una sola petición', () => {
    it('Ctrl+Z dos veces seguidas deshace una vez', async () => {
      await abrir();

      pulsarCtrlZ();
      pulsarCtrlZ();

      expect(deshacer).toHaveBeenCalledTimes(1);
    });

    it('el botón y luego Ctrl+Z, también una vez', async () => {
      const aviso = await abrir();

      botones(aviso)[0].click();
      pulsarCtrlZ();
      botones(aviso)[0].click();

      expect(deshacer).toHaveBeenCalledTimes(1);
    });

    it('tras «Añadir nota», Ctrl+Z ya no deshace', async () => {
      const aviso = await abrir(true);

      botones(aviso)[1].click();
      pulsarCtrlZ();

      expect(deshacer).not.toHaveBeenCalled();
    });
  });

  describe('si ahora no se puede deshacer', () => {
    it('pide esperar y deja el aviso abierto para volver a intentarlo', async () => {
      deshacer.mockReturnValueOnce(false);
      const aviso = await abrir();
      const cerrado = vi.fn();
      TestBed.inject(MatSnackBar)._openedSnackBarRef!.afterDismissed().subscribe(cerrado);

      botones(aviso)[0].click();
      TestBed.tick();

      expect(aviso.textContent).toContain('Espera a que termine el cambio.');
      expect(cerrado).not.toHaveBeenCalled();

      botones(aviso)[0].click();
      expect(deshacer).toHaveBeenCalledTimes(2);
    });
  });

  describe('dónde no actúa Ctrl+Z', () => {
    it('con el foco en un campo de texto es del campo, no del aviso', async () => {
      await abrir();

      const evento = pulsarCtrlZ(crear('<input data-destino />'));

      expect(deshacer).not.toHaveBeenCalled();
      expect(evento.defaultPrevented).toBe(false);
    });

    it('ni en un mat-select ni en un combobox', async () => {
      await abrir();

      pulsarCtrlZ(crear('<mat-select data-destino tabindex="0"></mat-select>'));
      pulsarCtrlZ(crear('<div role="combobox" tabindex="0" data-destino></div>'));

      expect(deshacer).not.toHaveBeenCalled();
    });

    it('ni con un diálogo abierto', async () => {
      await abrir();
      dialogosAbiertos.push({});

      pulsarCtrlZ();

      expect(deshacer).not.toHaveBeenCalled();
    });

    it('ni desde otro panel flotante (un menú abierto)', async () => {
      await abrir();

      pulsarCtrlZ(
        crear('<div class="cdk-overlay-pane"><button data-destino>Mover a…</button></div>'),
      );

      expect(deshacer).not.toHaveBeenCalled();
    });

    it('con el foco en el propio aviso, sí', async () => {
      const aviso = await abrir();

      pulsarCtrlZ(botones(aviso)[0]);

      expect(deshacer).toHaveBeenCalledTimes(1);
    });

    it('Ctrl+Mayús+Z (rehacer) no deshace', async () => {
      await abrir();

      document.body.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Z', ctrlKey: true, shiftKey: true, bubbles: true }),
      );

      expect(deshacer).not.toHaveBeenCalled();
    });

    it('cerrado el aviso, ya no hace nada', async () => {
      await abrir();
      const referencia = TestBed.inject(MatSnackBar)._openedSnackBarRef!;
      referencia.dismiss();
      await firstValueFrom(referencia.afterDismissed());

      pulsarCtrlZ();

      expect(deshacer).not.toHaveBeenCalled();
    });
  });
});
