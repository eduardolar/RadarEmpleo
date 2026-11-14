import { TestBed } from '@angular/core/testing';
import { FechaActualService } from './fecha-actual.service';

describe('FechaActualService', () => {
  const ANTES = new Date('2026-10-30T22:50:00Z');
  const DESPUES = new Date('2026-10-30T23:10:00Z');
  let visibilidad: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'] });
    vi.setSystemTime(ANTES);
    visibilidad = vi.spyOn(document, 'visibilityState', 'get');
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('se actualiza al volver a la pestaña', () => {
    const service = TestBed.inject(FechaActualService);
    expect(service.ahora()).toEqual(ANTES);

    vi.setSystemTime(DESPUES);
    visibilidad.mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));

    expect(service.ahora()).toEqual(DESPUES);
  });

  it('no hace nada al ocultarse la pestaña', () => {
    const service = TestBed.inject(FechaActualService);

    vi.setSystemTime(DESPUES);
    visibilidad.mockReturnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange'));

    expect(service.ahora()).toEqual(ANTES);
  });

  it('se actualiza cuando la ventana recibe el foco', () => {
    const service = TestBed.inject(FechaActualService);

    vi.setSystemTime(DESPUES);
    window.dispatchEvent(new Event('focus'));

    expect(service.ahora()).toEqual(DESPUES);
  });

  describe('a medianoche de Madrid, con la pestaña a la vista', () => {
    // 22:50Z del 30/10 son las 23:50 en Madrid (invierno): faltan 10 minutos para el día 31
    const DIEZ_MINUTOS = 10 * 60 * 1000;

    it('cambia sola justo después de las 00:00, y no antes', () => {
      const service = TestBed.inject(FechaActualService);

      vi.advanceTimersByTime(DIEZ_MINUTOS);
      expect(service.ahora()).toEqual(ANTES);

      vi.advanceTimersByTime(1000);
      expect(service.ahora().toISOString()).toBe('2026-10-30T23:00:01.000Z');
    });

    it('se vuelve a programar para la medianoche siguiente', () => {
      const service = TestBed.inject(FechaActualService);
      vi.advanceTimersByTime(DIEZ_MINUTOS + 1000);

      vi.advanceTimersByTime(24 * 60 * 60 * 1000);

      expect(service.ahora().toISOString()).toBe('2026-10-31T23:00:01.000Z');
      expect(vi.getTimerCount()).toBe(1);
    });

    it('al volver a la pestaña no se acumulan temporizadores', () => {
      TestBed.inject(FechaActualService);
      visibilidad.mockReturnValue('visible');

      document.dispatchEvent(new Event('visibilitychange'));
      window.dispatchEvent(new Event('focus'));

      expect(vi.getTimerCount()).toBe(1);
    });

    it('al destruirse no deja el temporizador ni escucha los eventos', () => {
      const service = TestBed.inject(FechaActualService);
      TestBed.resetTestingModule();

      expect(vi.getTimerCount()).toBe(0);
      vi.setSystemTime(DESPUES);
      window.dispatchEvent(new Event('focus'));
      expect(service.ahora()).toEqual(ANTES);
    });
  });
});
