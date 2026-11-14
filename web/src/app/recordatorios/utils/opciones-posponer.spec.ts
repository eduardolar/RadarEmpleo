import { opcionesPosponer } from './opciones-posponer';

describe('opcionesPosponer', () => {
  it('da mañana, dentro de 3 días y dentro de una semana, contados desde hoy', () => {
    // Martes 10/11/2026
    expect(opcionesPosponer('2026-11-10')).toEqual([
      { texto: 'Mañana', dia: '2026-11-11', fecha: 'mié 11 nov' },
      { texto: 'Dentro de 3 días', dia: '2026-11-13', fecha: 'vie 13 nov' },
      { texto: 'Dentro de una semana', dia: '2026-11-17', fecha: 'mar 17 nov' },
    ]);
  });

  it('pasa de mes y de año', () => {
    // Miércoles 30/12/2026
    expect(opcionesPosponer('2026-12-30').map((o) => o.dia)).toEqual([
      '2026-12-31',
      '2027-01-02',
      '2027-01-06',
    ]);
  });

  describe('si mañana es fin de semana', () => {
    it('un viernes ofrece «El lunes» con su fecha, y no repite ese día en «Dentro de 3 días»', () => {
      expect(opcionesPosponer('2026-11-13')).toEqual([
        { texto: 'El lunes', dia: '2026-11-16', fecha: 'lun 16 nov' },
        { texto: 'Dentro de una semana', dia: '2026-11-20', fecha: 'vie 20 nov' },
      ]);
    });

    it('un sábado, también «El lunes»: el domingo tampoco contesta nadie', () => {
      expect(opcionesPosponer('2026-11-14')).toEqual([
        { texto: 'El lunes', dia: '2026-11-16', fecha: 'lun 16 nov' },
        { texto: 'Dentro de 3 días', dia: '2026-11-17', fecha: 'mar 17 nov' },
        { texto: 'Dentro de una semana', dia: '2026-11-21', fecha: 'sáb 21 nov' },
      ]);
    });

    it('un domingo vuelve «Mañana», que ya es lunes', () => {
      expect(opcionesPosponer('2026-11-15')[0]).toEqual({
        texto: 'Mañana',
        dia: '2026-11-16',
        fecha: 'lun 16 nov',
      });
    });
  });

  describe('lo que todavía no toca', () => {
    it('se cuenta desde su día, para no adelantarlo', () => {
      // Hoy es viernes 13 y el recordatorio, del martes 17
      expect(opcionesPosponer('2026-11-13', '2026-11-17')).toEqual([
        { texto: 'Un día después', dia: '2026-11-18', fecha: 'mié 18 nov' },
        { texto: '3 días después', dia: '2026-11-20', fecha: 'vie 20 nov' },
        { texto: 'Una semana después', dia: '2026-11-24', fecha: 'mar 24 nov' },
      ]);
    });

    it('si el día siguiente al suyo es fin de semana, también ofrece «El lunes»', () => {
      // El recordatorio es del viernes 20
      expect(opcionesPosponer('2026-11-13', '2026-11-20')).toEqual([
        { texto: 'El lunes', dia: '2026-11-23', fecha: 'lun 23 nov' },
        { texto: 'Una semana después', dia: '2026-11-27', fecha: 'vie 27 nov' },
      ]);
    });

    it('lo vencido y lo de hoy se cuentan desde hoy', () => {
      const desdeHoy = opcionesPosponer('2026-11-10');

      expect(opcionesPosponer('2026-11-10', '2026-11-03')).toEqual(desdeHoy);
      expect(opcionesPosponer('2026-11-10', '2026-11-10')).toEqual(desdeHoy);
    });
  });
});
