import {
  diaCorto,
  diaLargo,
  diaMadrid,
  diasNaturalesEntre,
  formatearFecha,
  horaMadrid,
  instanteMadrid,
  msHastaMedianocheMadrid,
  sumarDias,
} from './fechas-madrid';

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

describe('horaMadrid', () => {
  it('da la hora de Madrid como HH:mm, en invierno y en verano', () => {
    expect(horaMadrid('2026-11-11T14:00:00Z')).toBe('15:00');
    expect(horaMadrid('2026-07-15T14:00:00Z')).toBe('16:00');
  });

  it('con ceros delante y la medianoche como 00', () => {
    expect(horaMadrid('2026-11-17T08:05:00Z')).toBe('09:05');
    expect(horaMadrid('2026-11-13T23:00:00Z')).toBe('00:00');
  });

  it('el día del cambio de hora usa el horario que ya rige', () => {
    // 25/10/2026: a las 01:00Z en Madrid pasan de las 03:00 a las 02:00
    expect(horaMadrid('2026-10-25T00:30:00Z')).toBe('02:30');
    expect(horaMadrid('2026-10-25T01:30:00Z')).toBe('02:30');
    expect(horaMadrid('2026-10-25T08:00:00Z')).toBe('09:00');
    // 29/03/2026: empieza el horario de verano
    expect(horaMadrid('2026-03-29T07:00:00Z')).toBe('09:00');
  });
});

describe('sumarDias', () => {
  it('suma días de calendario', () => {
    expect(sumarDias('2026-11-13', 1)).toBe('2026-11-14');
    expect(sumarDias('2026-11-13', 7)).toBe('2026-11-20');
    expect(sumarDias('2026-11-13', 0)).toBe('2026-11-13');
  });

  it('pasa de mes y de año', () => {
    expect(sumarDias('2026-11-30', 1)).toBe('2026-12-01');
    expect(sumarDias('2026-12-31', 1)).toBe('2027-01-01');
    expect(sumarDias('2028-02-28', 1)).toBe('2028-02-29');
  });

  it('no pierde ni repite el día al cruzar el cambio de hora', () => {
    expect(sumarDias('2026-10-24', 1)).toBe('2026-10-25');
    expect(sumarDias('2026-10-25', 1)).toBe('2026-10-26');
    expect(sumarDias('2026-03-28', 2)).toBe('2026-03-30');
  });

  it('resta con un número negativo', () => {
    expect(sumarDias('2026-11-01', -1)).toBe('2026-10-31');
  });
});

describe('diaCorto', () => {
  it('da el día de la semana, el número y el mes de un día suelto', () => {
    expect(diaCorto('2026-11-14')).toBe('sáb 14 nov');
    expect(diaCorto('2026-11-03')).toBe('mar 3 nov');
  });

  it('de un instante, el día de Madrid', () => {
    expect(diaCorto('2026-11-13T22:59:00Z')).toBe('vie 13 nov');
    expect(diaCorto('2026-11-13T23:00:00Z')).toBe('sáb 14 nov');
  });

  it('devuelve una cadena vacía si no es una fecha', () => {
    expect(diaCorto('no es una fecha')).toBe('');
  });
});

describe('diaLargo', () => {
  it('da el día de la semana, el número y el mes con todas las letras', () => {
    expect(diaLargo('2026-11-18')).toBe('miércoles 18 de noviembre');
    expect(diaLargo('2026-12-01')).toBe('martes 1 de diciembre');
  });

  it('de un instante, el día de Madrid', () => {
    expect(diaLargo('2026-11-13T22:59:00Z')).toBe('viernes 13 de noviembre');
    expect(diaLargo('2026-11-13T23:00:00Z')).toBe('sábado 14 de noviembre');
  });

  it('devuelve una cadena vacía si no es una fecha', () => {
    expect(diaLargo('no es una fecha')).toBe('');
  });
});

describe('instanteMadrid', () => {
  it('pasa un día y una hora de Madrid a su instante, en invierno y en verano', () => {
    expect(instanteMadrid('2026-11-17', '15:00')?.toISOString()).toBe('2026-11-17T14:00:00.000Z');
    expect(instanteMadrid('2027-07-01', '15:00')?.toISOString()).toBe('2027-07-01T13:00:00.000Z');
  });

  it('la noche que el reloj se adelanta, de 02:00 a 02:59 no existe', () => {
    expect(instanteMadrid('2027-03-28', '02:30')).toBeNull();
    expect(instanteMadrid('2027-03-28', '02:00')).toBeNull();
    expect(instanteMadrid('2027-03-28', '03:00')?.toISOString()).toBe('2027-03-28T01:00:00.000Z');
    expect(instanteMadrid('2027-03-27', '02:30')?.toISOString()).toBe('2027-03-27T01:30:00.000Z');
  });

  it('la hora que se repite en octubre es la de invierno, como en la API', () => {
    expect(instanteMadrid('2027-10-31', '02:30')?.toISOString()).toBe('2027-10-31T01:30:00.000Z');
  });
});

describe('msHastaMedianocheMadrid', () => {
  it('cuenta hasta las 00:00 de Madrid, no de UTC', () => {
    // 22:50Z del 13/11 son las 23:50 en Madrid: faltan 10 minutos
    expect(msHastaMedianocheMadrid(new Date('2026-11-13T22:50:00Z'))).toBe(10 * 60 * 1000);
    // En verano la medianoche de Madrid son las 22:00Z
    expect(msHastaMedianocheMadrid(new Date('2027-07-01T21:00:00Z'))).toBe(60 * 60 * 1000);
  });

  it('el día que se adelanta el reloj tiene 23 horas', () => {
    expect(msHastaMedianocheMadrid(new Date('2027-03-27T23:00:00Z'))).toBe(23 * 60 * 60 * 1000);
  });
});
