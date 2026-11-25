import { crearEjecucion } from './ejecucion-de-prueba';
import { agruparPorDia, erroresResueltos, pasaElFiltro, recuentoDeHoy } from './tareas-por-dia';

describe('tareas por día', () => {
  const HOY = '2026-11-23';

  describe('erroresResueltos', () => {
    it('un error con la misma tarea y candidatura hecha después está resuelto', () => {
      const lista = [
        crearEjecucion({ id: 3, estado: 'Ok' }),
        crearEjecucion({ id: 2, estado: 'Error' }),
        crearEjecucion({ id: 1, estado: 'Error' }),
      ];

      expect([...erroresResueltos(lista)].sort()).toEqual([1, 2]);
    });

    it('no lo resuelve una hecha antes, ni la de otra candidatura u otra tarea, ni una cancelada', () => {
      const lista = [
        crearEjecucion({ id: 6, estado: 'Cancelada' }),
        crearEjecucion({ id: 5, estado: 'Ok', tarea: 'ExtraerRegistro', candidaturaId: null }),
        crearEjecucion({ id: 4, estado: 'Ok', candidaturaId: 20 }),
        crearEjecucion({ id: 3, estado: 'Error' }),
        crearEjecucion({ id: 2, estado: 'Ok' }),
      ];

      expect(erroresResueltos(lista).size).toBe(0);
    });
  });

  it('pasaElFiltro: «No han salido» son los errores y «En marcha», en cola y en curso', () => {
    const estados = ['EnCola', 'EnCurso', 'Ok', 'Error', 'Cancelada'] as const;

    expect(estados.filter((estado) => pasaElFiltro({ estado }, 'todas'))).toEqual(estados);
    expect(estados.filter((estado) => pasaElFiltro({ estado }, 'no-han-salido'))).toEqual([
      'Error',
    ]);
    expect(estados.filter((estado) => pasaElFiltro({ estado }, 'en-marcha'))).toEqual([
      'EnCola',
      'EnCurso',
    ]);
  });

  describe('agruparPorDia', () => {
    it('«Hoy», «Ayer» y la fecha, por el día de Madrid y sin cambiar el orden', () => {
      const grupos = agruparPorDia(
        [
          crearEjecucion({ id: 5, fechaAlta: '2026-11-23T10:00:00Z' }),
          // Las 23:30 UTC del 22 ya son el 23 en Madrid
          crearEjecucion({ id: 4, fechaAlta: '2026-11-22T23:30:00Z' }),
          crearEjecucion({ id: 3, fechaAlta: '2026-11-22T10:00:00Z' }),
          crearEjecucion({ id: 2, fechaAlta: '2026-11-20T10:00:00Z' }),
        ],
        HOY,
      );

      expect(grupos.map((g) => [g.titulo, g.tareas.map((e) => e.id)])).toEqual([
        ['Hoy', [5, 4]],
        ['Ayer', [3]],
        ['20 nov 2026', [2]],
      ]);
    });

    const titulos = (fechas: string[], hoy: string) =>
      agruparPorDia(
        fechas.map((fechaAlta, i) => crearEjecucion({ id: fechas.length - i, fechaAlta })),
        hoy,
      ).map((g) => [g.dia, g.titulo, g.tareas.length]);

    it('en horario de verano la medianoche de Madrid son las 22:00 UTC', () => {
      // Las 22:30 UTC del 15 de julio ya son las 00:30 del 16 en Madrid
      expect(titulos(['2026-07-15T22:30:00Z', '2026-07-15T21:59:59Z'], '2026-07-16')).toEqual([
        ['2026-07-16', 'Hoy', 1],
        ['2026-07-15', 'Ayer', 1],
      ]);
    });

    it('«Ayer» también cuando ayer era otro mes u otro año', () => {
      expect(titulos(['2026-12-01T09:00:00Z', '2026-11-30T09:00:00Z'], '2026-12-01')).toEqual([
        ['2026-12-01', 'Hoy', 1],
        ['2026-11-30', 'Ayer', 1],
      ]);
      // Las 23:30 UTC del 31 de diciembre ya son el 1 de enero en Madrid
      expect(
        titulos(
          ['2026-12-31T23:30:00Z', '2026-12-31T22:59:59Z', '2026-12-30T12:00:00Z'],
          '2027-01-01',
        ),
      ).toEqual([
        ['2027-01-01', 'Hoy', 1],
        ['2026-12-31', 'Ayer', 1],
        ['2026-12-30', '30 dic 2026', 1],
      ]);
      expect(titulos(['2028-02-29T12:00:00Z'], '2028-03-01')).toEqual([['2028-02-29', 'Ayer', 1]]);
    });

    it('el día que se atrasa el reloj (25 horas) sigue siendo un solo grupo', () => {
      // El 25/10/2026 va de las 22:00 UTC del 24 a las 23:00 UTC del 25
      const fechas = [
        '2026-10-25T23:00:00Z',
        '2026-10-25T22:59:59Z',
        '2026-10-25T01:30:00Z',
        '2026-10-25T00:30:00Z',
        '2026-10-24T22:00:00Z',
        '2026-10-24T21:59:59Z',
      ];

      expect(titulos(fechas, '2026-10-25')).toEqual([
        ['2026-10-26', '26 oct 2026', 1],
        ['2026-10-25', 'Hoy', 4],
        ['2026-10-24', 'Ayer', 1],
      ]);
      expect(titulos(fechas, '2026-10-26')).toEqual([
        ['2026-10-26', 'Hoy', 1],
        ['2026-10-25', 'Ayer', 4],
        ['2026-10-24', '24 oct 2026', 1],
      ]);
      const lista = fechas.map((fechaAlta) => crearEjecucion({ fechaAlta }));
      expect(recuentoDeHoy(lista, '2026-10-25')).toBe('Hoy: 4 tareas');
    });

    it('el día que se adelanta el reloj (23 horas) sigue siendo un solo grupo', () => {
      // El 29/03/2026 va de las 23:00 UTC del 28 a las 22:00 UTC del 29
      const fechas = [
        '2026-03-29T22:00:00Z',
        '2026-03-29T21:59:59Z',
        '2026-03-29T01:00:00Z',
        '2026-03-28T23:00:00Z',
        '2026-03-28T22:59:59Z',
      ];

      expect(titulos(fechas, '2026-03-29')).toEqual([
        ['2026-03-30', '30 mar 2026', 1],
        ['2026-03-29', 'Hoy', 3],
        ['2026-03-28', 'Ayer', 1],
      ]);
      expect(titulos(fechas, '2026-03-30')).toEqual([
        ['2026-03-30', 'Hoy', 1],
        ['2026-03-29', 'Ayer', 3],
        ['2026-03-28', '28 mar 2026', 1],
      ]);
    });
  });

  describe('recuentoDeHoy', () => {
    it('cuenta solo lo de hoy, con el singular y el plural', () => {
      const ayer = crearEjecucion({ estado: 'Error', fechaAlta: '2026-11-22T10:00:00Z' });

      expect(recuentoDeHoy([ayer], HOY)).toBe('Hoy: ninguna tarea');
      expect(recuentoDeHoy([crearEjecucion({ estado: 'Ok' }), ayer], HOY)).toBe('Hoy: 1 tarea');
      expect(recuentoDeHoy([crearEjecucion({ estado: 'Error' }), ayer], HOY)).toBe(
        'Hoy: 1 tarea · 1 no ha salido',
      );
      expect(
        recuentoDeHoy(
          [
            crearEjecucion({ estado: 'Error' }),
            crearEjecucion({ estado: 'Error' }),
            crearEjecucion({ estado: 'EnCurso' }),
          ],
          HOY,
        ),
      ).toBe('Hoy: 3 tareas · 2 no han salido');
    });
  });
});
