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
