import { diasRapidos } from './dias-rapidos';

describe('diasRapidos', () => {
  it('da hoy, mañana y dentro de una semana, contados desde el día que se le pasa', () => {
    expect(diasRapidos('2026-11-13')).toEqual([
      { texto: 'Hoy', dia: '2026-11-13' },
      { texto: 'Mañana', dia: '2026-11-14' },
      { texto: 'En una semana', dia: '2026-11-20' },
    ]);
  });

  it('cambia de mes y de año cuando toca', () => {
    expect(diasRapidos('2026-12-31').map((d) => d.dia)).toEqual([
      '2026-12-31',
      '2027-01-01',
      '2027-01-07',
    ]);
  });
});
