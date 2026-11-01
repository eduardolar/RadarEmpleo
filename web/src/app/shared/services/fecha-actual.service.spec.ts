import { TestBed } from '@angular/core/testing';
import { FechaActualService } from './fecha-actual.service';

describe('FechaActualService', () => {
  const ANTES = new Date('2026-10-30T22:50:00Z');
  const DESPUES = new Date('2026-10-30T23:10:00Z');
  let visibilidad: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
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
});
