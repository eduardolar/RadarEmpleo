import { anchoBarra, etiquetaSemana, textoDias } from './barras';

describe('anchoBarra', () => {
  it('es proporcional a la mayor', () => {
    expect(anchoBarra(12, 12)).toBe(100);
    expect(anchoBarra(3, 12)).toBe(25);
    expect(anchoBarra(1, 3)).toBe(33);
  });

  it('con 0 no hay barra, y si todas son 0 tampoco', () => {
    expect(anchoBarra(0, 12)).toBe(0);
    expect(anchoBarra(0, 0)).toBe(0);
  });

  it('algo que no es 0 se ve aunque sea muy poco', () => {
    expect(anchoBarra(1, 200)).toBe(2);
  });
});

describe('etiquetaSemana', () => {
  it('dice el lunes sin el día de la semana', () => {
    expect(etiquetaSemana('2026-11-02')).toBe('Semana del 2 nov');
    expect(etiquetaSemana('2026-12-28')).toBe('Semana del 28 dic');
  });
});

describe('textoDias', () => {
  it('habla como una persona', () => {
    expect(textoDias(0)).toBe('el mismo día');
    expect(textoDias(1)).toBe('1 día');
    expect(textoDias(9)).toBe('9 días');
    expect(textoDias(6.5)).toBe('6,5 días');
  });
});
