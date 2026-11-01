import { diasEnEstado, textoDiasEnEstado } from './dias-en-estado';

describe('diasEnEstado', () => {
  const ahora = new Date('2026-10-30T10:00:00Z');

  it('cuenta los días naturales de Madrid desde que entró en el estado', () => {
    expect(diasEnEstado('2026-10-30T07:00:00Z', ahora)).toBe(0);
    expect(diasEnEstado('2026-10-29T22:30:00Z', ahora)).toBe(1);
    expect(diasEnEstado('2026-10-23T10:00:00Z', ahora)).toBe(7);
  });

  it('no da días negativos si la fecha del servidor va por delante del reloj local', () => {
    expect(diasEnEstado('2026-10-31T10:00:00Z', ahora)).toBe(0);
  });
});

describe('textoDiasEnEstado', () => {
  it('dice «Hoy», «1 día» o «N días»', () => {
    expect(textoDiasEnEstado(0)).toBe('Hoy');
    expect(textoDiasEnEstado(1)).toBe('1 día');
    expect(textoDiasEnEstado(12)).toBe('12 días');
  });
});
