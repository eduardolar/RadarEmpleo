import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog } from '@angular/material/dialog';
import { of, Subject } from 'rxjs';
import { confirmar, DialogoConfirmacion, intentosDeCerrar } from './dialogo-confirmacion';

describe('confirmar', () => {
  const DATOS = { titulo: 'Borrar nota', mensaje: 'No se puede deshacer.', confirmar: 'Borrar' };

  function abrir(respuesta: unknown): { open: ReturnType<typeof vi.fn>; emitido: unknown } {
    const open = vi.fn(() => ({ afterClosed: () => of(respuesta) }));
    let emitido: unknown;
    confirmar({ open } as unknown as MatDialog, DATOS).subscribe((r) => (emitido = r));
    return { open, emitido };
  }

  it('solo emite true si se pulsa el botón de confirmar', () => {
    expect(abrir(true).emitido).toBe(true);
    expect(abrir(false).emitido).toBe(false);
    expect(abrir(undefined).emitido).toBe(false);
  });

  it('el alertdialog apunta con aria-describedby al texto que se pinta', async () => {
    const config = abrir(true).open.mock.lastCall![1] as {
      role: string;
      ariaDescribedBy: string;
    };
    TestBed.configureTestingModule({
      imports: [DialogoConfirmacion],
      providers: [{ provide: MAT_DIALOG_DATA, useValue: DATOS }],
    });
    const fixture = TestBed.createComponent(DialogoConfirmacion);
    await fixture.whenStable();

    const descripcion = (fixture.nativeElement as HTMLElement).querySelector(
      `#${config.ariaDescribedBy}`,
    );

    expect(config.role).toBe('alertdialog');
    expect(descripcion?.textContent).toContain('No se puede deshacer.');
  });
});

describe('intentosDeCerrar', () => {
  let teclas: Subject<KeyboardEvent>;
  let clicFuera: Subject<MouseEvent>;
  let intentos: Event[];

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    teclas = new Subject();
    clicFuera = new Subject();
    intentos = [];
    intentosDeCerrar({
      keydownEvents: () => teclas,
      backdropClick: () => clicFuera,
    }).subscribe((evento) => intentos.push(evento));
  });

  afterEach(() => vi.useRealTimers());

  it('Esc cuenta desde el primer momento y no deja que el navegador lo use', () => {
    const esc = new KeyboardEvent('keydown', { key: 'Escape', cancelable: true });

    teclas.next(esc);

    expect(intentos).toEqual([esc]);
    expect(esc.defaultPrevented).toBe(true);
  });

  it('otras teclas y Esc con modificadores no cuentan', () => {
    teclas.next(new KeyboardEvent('keydown', { key: 'a' }));
    teclas.next(new KeyboardEvent('keydown', { key: 'Escape', shiftKey: true }));

    expect(intentos).toEqual([]);
  });

  it('el clic fuera no cuenta durante el margen del doble clic y luego sí', () => {
    vi.setSystemTime(Date.now() + 499);
    clicFuera.next(new MouseEvent('click'));
    expect(intentos).toHaveLength(0);

    vi.setSystemTime(Date.now() + 1);
    clicFuera.next(new MouseEvent('click'));
    expect(intentos).toHaveLength(1);
  });
});
