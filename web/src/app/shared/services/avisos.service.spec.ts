import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom, Subject } from 'rxjs';
import { AvisoDeshacer } from '../components/aviso-deshacer/aviso-deshacer';
import { AvisosService, DURACION_DESHACER_MS } from './avisos.service';

describe('AvisosService', () => {
  describe('lo que pide al snackbar', () => {
    let service: AvisosService;
    let open: ReturnType<typeof vi.fn>;
    let openFromComponent: ReturnType<typeof vi.fn>;
    /** Un Subject por aviso abierto, para pulsar la acción de cada uno por separado */
    let acciones: Subject<void>[];

    beforeEach(() => {
      acciones = [];
      open = vi.fn(() => {
        const accion = new Subject<void>();
        acciones.push(accion);
        return { onAction: () => accion.asObservable() };
      });
      openFromComponent = vi.fn();
      TestBed.configureTestingModule({
        providers: [{ provide: MatSnackBar, useValue: { open, openFromComponent } }],
      });
      service = TestBed.inject(AvisosService);
    });

    describe('exito', () => {
      it('sin acción: solo el mensaje, sin botón, 3 segundos', () => {
        service.exito('Candidatura guardada.');

        expect(open).toHaveBeenCalledExactlyOnceWith('Candidatura guardada.', undefined, {
          duration: 3000,
        });
      });

      it('no se anuncia como urgente ni lleva el estilo de error', () => {
        service.exito('Candidatura guardada.');

        const config = open.mock.calls[0][2];
        expect(config.politeness).toBeUndefined();
        expect(config.panelClass).toBeUndefined();
      });

      it('con acción: lleva su botón y dura 6 segundos', () => {
        service.exito('Candidatura guardada.', { texto: 'Ver ficha', alPulsar: vi.fn() });

        expect(open).toHaveBeenCalledExactlyOnceWith('Candidatura guardada.', 'Ver ficha', {
          duration: 6000,
        });
      });

      it('la acción solo se ejecuta al pulsar el botón, una vez por pulsación', () => {
        const alPulsar = vi.fn();
        service.exito('Candidatura guardada.', { texto: 'Ver ficha', alPulsar });
        expect(alPulsar).not.toHaveBeenCalled();

        acciones[0].next();

        expect(alPulsar).toHaveBeenCalledTimes(1);
        expect(alPulsar).toHaveBeenCalledWith();
      });

      it('si el aviso se cierra sin pulsar, la acción no se ejecuta', () => {
        const alPulsar = vi.fn();
        service.exito('Candidatura guardada.', { texto: 'Ver ficha', alPulsar });

        acciones[0].complete();

        expect(alPulsar).not.toHaveBeenCalled();
      });

      it('con dos avisos seguidos, cada botón ejecuta su propia acción', () => {
        const primera = vi.fn();
        const segunda = vi.fn();
        service.exito('Primero.', { texto: 'Uno', alPulsar: primera });
        service.exito('Segundo.', { texto: 'Dos', alPulsar: segunda });

        acciones[1].next();

        expect(segunda).toHaveBeenCalledTimes(1);
        expect(primera).not.toHaveBeenCalled();
      });

      it('con mensaje vacío lo pasa tal cual (no lo rellena ni lo descarta)', () => {
        service.exito('');

        expect(open).toHaveBeenCalledExactlyOnceWith('', undefined, { duration: 3000 });
      });

      it('una acción con texto vacío cuenta como acción: 6 segundos', () => {
        service.exito('Guardado.', { texto: '', alPulsar: vi.fn() });

        expect(open).toHaveBeenCalledExactlyOnceWith('Guardado.', '', { duration: 6000 });
      });
    });

    describe('error', () => {
      it('lleva «Cerrar», dura 8 segundos, estilo de error y se anuncia en seguida', () => {
        service.error('No se ha podido guardar.');

        expect(open).toHaveBeenCalledExactlyOnceWith('No se ha podido guardar.', 'Cerrar', {
          duration: 8000,
          panelClass: 'aviso-error',
          politeness: 'assertive',
        });
      });

      it('pulsar «Cerrar» no necesita nada más del servicio (no se suscribe a la acción)', () => {
        service.error('No se ha podido guardar.');

        expect(acciones[0].observed).toBe(false);
      });

      it('con mensaje vacío lo pasa tal cual', () => {
        service.error('');

        expect(open.mock.calls[0][0]).toBe('');
      });
    });

    describe('deshacible', () => {
      it('abre el aviso con «Deshacer» con el mensaje y la función, 8 segundos', () => {
        const deshacer = vi.fn(() => true);

        service.deshacible('«Backend .NET» pasa a Enviada.', deshacer);

        expect(DURACION_DESHACER_MS).toBe(8000);
        expect(openFromComponent).toHaveBeenCalledExactlyOnceWith(AvisoDeshacer, {
          data: { mensaje: '«Backend .NET» pasa a Enviada.', deshacer, otra: undefined },
          duration: DURACION_DESHACER_MS,
        });
        expect(open).not.toHaveBeenCalled();
      });

      it('pasa la otra acción al aviso', () => {
        const deshacer = vi.fn(() => true);
        const otra = { texto: 'Añadir nota', alPulsar: vi.fn() };

        service.deshacible('Movida.', deshacer, otra);

        expect(openFromComponent.mock.calls[0][1].data).toEqual({
          mensaje: 'Movida.',
          deshacer,
          otra,
        });
      });

      it('no deshace ni ejecuta la otra acción por abrirse', () => {
        const deshacer = vi.fn(() => true);
        const otra = { texto: 'Añadir nota', alPulsar: vi.fn() };

        service.deshacible('Movida.', deshacer, otra);

        expect(deshacer).not.toHaveBeenCalled();
        expect(otra.alPulsar).not.toHaveBeenCalled();
      });

      it('no se anuncia como urgente ni lleva el estilo de error', () => {
        service.deshacible('Movida.', () => true);

        const config = openFromComponent.mock.calls[0][1];
        expect(config.politeness).toBeUndefined();
        expect(config.panelClass).toBeUndefined();
      });
    });
  });

  describe('con el snackbar de verdad', () => {
    let service: AvisosService;
    let snackBar: MatSnackBar;

    beforeEach(() => {
      TestBed.configureTestingModule({
        providers: [{ provide: MatDialog, useValue: { openDialogs: [] } }],
      });
      service = TestBed.inject(AvisosService);
      snackBar = TestBed.inject(MatSnackBar);
    });

    afterEach(() => {
      snackBar.dismiss();
      vi.useRealTimers();
    });

    async function abierto(): Promise<HTMLElement> {
      TestBed.tick();
      await firstValueFrom(snackBar._openedSnackBarRef!.afterOpened());
      return document.querySelector<HTMLElement>('.mat-mdc-snack-bar-container')!;
    }

    function botones(aviso: HTMLElement): HTMLButtonElement[] {
      return [...aviso.querySelectorAll('button')];
    }

    it('éxito: enseña el mensaje, sin botón, y se anuncia con educación', async () => {
      service.exito('Candidatura guardada.');
      const aviso = await abierto();

      expect(aviso.textContent).toContain('Candidatura guardada.');
      expect(botones(aviso)).toHaveLength(0);
      expect(aviso.querySelector('[aria-live]')?.getAttribute('aria-live')).toBe('polite');
    });

    it('éxito con acción: el botón ejecuta la acción y cierra el aviso', async () => {
      const alPulsar = vi.fn();
      service.exito('Candidatura guardada.', { texto: 'Ver ficha', alPulsar });
      const aviso = await abierto();
      const cerrado = firstValueFrom(snackBar._openedSnackBarRef!.afterDismissed());

      const [boton] = botones(aviso);
      expect(boton.textContent?.trim()).toBe('Ver ficha');
      boton.click();

      expect(alPulsar).toHaveBeenCalledTimes(1);
      expect(await cerrado).toEqual({ dismissedByAction: true });
    });

    it('error: lleva «Cerrar», el estilo de error y se anuncia en seguida', async () => {
      service.error('No se ha podido guardar.');
      const aviso = await abierto();

      expect(aviso.classList).toContain('aviso-error');
      expect(botones(aviso).map((b) => b.textContent?.trim())).toEqual(['Cerrar']);
      expect(aviso.querySelector('[aria-live]')?.getAttribute('aria-live')).toBe('assertive');
    });

    it('un aviso nuevo sustituye al que estaba a la vista, y su acción ya no se ejecuta', async () => {
      const primera = vi.fn();
      service.exito('Primero.', { texto: 'Uno', alPulsar: primera });
      await abierto();
      const referenciaPrimera = snackBar._openedSnackBarRef!;
      const primeraCerrada = firstValueFrom(referenciaPrimera.afterDismissed());

      service.error('Segundo.');
      const aviso = await abierto();

      expect(await primeraCerrada).toEqual({ dismissedByAction: false });
      expect(snackBar._openedSnackBarRef).not.toBe(referenciaPrimera);
      expect(aviso.textContent).toContain('Segundo.');
      expect(aviso.textContent).not.toContain('Primero.');
      referenciaPrimera.dismissWithAction();
      expect(primera).not.toHaveBeenCalled();
    });

    it.each([
      { tipo: 'éxito', abrir: () => service.exito('Guardado.'), duracion: 3000 },
      {
        tipo: 'éxito con acción',
        abrir: () => service.exito('Guardado.', { texto: 'Ver', alPulsar: vi.fn() }),
        duracion: 6000,
      },
      { tipo: 'error', abrir: () => service.error('Ha fallado.'), duracion: 8000 },
      {
        tipo: 'deshacible',
        abrir: () => service.deshacible('Movida.', () => true),
        duracion: DURACION_DESHACER_MS,
      },
    ])('$tipo empieza a cerrarse solo a los $duracion ms', async ({ abrir, duracion }) => {
      vi.useFakeTimers();
      abrir();
      TestBed.tick();
      const referencia = snackBar._openedSnackBarRef!;
      // La cuenta empieza al terminar la animación de entrada, y la de salida añade su tiempo:
      // por eso se espera a que esté abierto y se espía el inicio del cierre
      const cerrar = vi.spyOn(referencia, 'dismiss');
      const cerrado = vi.fn();
      referencia.afterDismissed().subscribe(cerrado);
      let abiertoEn: number | undefined;
      referencia.afterOpened().subscribe(() => (abiertoEn = Date.now()));
      // La entrada termina como mucho a los 200 ms (el plan B de Material si no llega animationend)
      await vi.advanceTimersByTimeAsync(200);
      expect(abiertoEn).toBeDefined();
      const restante = abiertoEn! + duracion - Date.now();

      await vi.advanceTimersByTimeAsync(restante - 1);
      expect(cerrar).not.toHaveBeenCalled();

      await vi.advanceTimersByTimeAsync(1);
      expect(cerrar).toHaveBeenCalledTimes(1);

      await vi.runAllTimersAsync();
      expect(cerrado).toHaveBeenCalledTimes(1);
    });
  });
});
