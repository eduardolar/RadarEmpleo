import { diaMadrid, diasNaturalesEntre, formatearFecha } from './fechas-madrid';

describe('formatearFecha', () => {
  it('enseña el día en hora de Madrid', () => {
    expect(formatearFecha('2026-10-30T09:05:00Z')).toBe('30 oct 2026');
  });

  it('suma una hora en invierno y dos en verano al pasar de UTC a Madrid', () => {
    expect(formatearFecha('2026-10-30T09:05:00Z', 'dia-hora')).toBe('30 oct 2026, 10:05');
    expect(formatearFecha('2026-07-15T09:05:00Z', 'dia-hora')).toBe('15 jul 2026, 11:05');
  });

  it('cambia de día si en Madrid ya es medianoche aunque en UTC no', () => {
    expect(formatearFecha('2026-10-30T23:30:00Z')).toBe('31 oct 2026');
  });

  it('devuelve una cadena vacía si no hay fecha o no es válida', () => {
    expect(formatearFecha(null)).toBe('');
    expect(formatearFecha(undefined)).toBe('');
    expect(formatearFecha('no es una fecha')).toBe('');
  });
});

describe('diaMadrid', () => {
  it('da el día de Madrid como AAAA-MM-DD', () => {
    expect(diaMadrid('2026-10-30T22:59:00Z')).toBe('2026-10-30');
    expect(diaMadrid('2026-10-30T23:00:00Z')).toBe('2026-10-31');
  });

  it('cambia de día en la medianoche de Madrid también en verano', () => {
    expect(diaMadrid('2026-07-15T21:59:59Z')).toBe('2026-07-15');
    expect(diaMadrid('2026-07-15T22:00:00Z')).toBe('2026-07-16');
  });

  it('rellena con ceros el mes y el día', () => {
    expect(diaMadrid('2026-01-05T10:00:00Z')).toBe('2026-01-05');
  });
});

describe('diasNaturalesEntre', () => {
  it('es 0 dentro del mismo día de Madrid', () => {
    expect(diasNaturalesEntre('2026-10-30T07:00:00Z', '2026-10-30T22:00:00Z')).toBe(0);
  });

  it('cuenta días del calendario, no bloques de 24 horas', () => {
    expect(diasNaturalesEntre('2026-10-30T22:00:00Z', '2026-10-31T00:30:00Z')).toBe(1);
    expect(diasNaturalesEntre('2026-10-20T10:00:00Z', '2026-10-30T09:00:00Z')).toBe(10);
  });

  it('no se descuadra con el cambio de hora de octubre', () => {
    // El 25/10/2026 Madrid pasa de UTC+2 a UTC+1: ese día dura 25 horas
    // De las 00:00 a las 23:59 de ese día: casi 25 horas y sigue siendo el mismo día
    expect(diasNaturalesEntre('2026-10-24T22:00:00Z', '2026-10-25T22:59:00Z')).toBe(0);
    // De las 23:59 del 24 a las 00:00 del 26
    expect(diasNaturalesEntre('2026-10-24T21:59:00Z', '2026-10-25T23:00:00Z')).toBe(2);
  });

  it('no se descuadra con el cambio de hora de marzo', () => {
    // El 29/03/2026 Madrid pasa de UTC+1 a UTC+2: ese día dura 23 horas
    // De las 00:00 a las 23:59 de ese día
    expect(diasNaturalesEntre('2026-03-28T23:00:00Z', '2026-03-29T21:59:00Z')).toBe(0);
    // De las 23:59 del 28 a las 00:00 del 30: solo 23 horas, pero son dos días de calendario
    expect(diasNaturalesEntre('2026-03-28T22:59:00Z', '2026-03-29T22:00:00Z')).toBe(2);
  });
});
