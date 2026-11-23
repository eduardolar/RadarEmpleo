import { crearEjecucion } from './ejecucion-de-prueba';
import { ultimasDeCadaTarea } from './ultimas-ejecuciones';

describe('ultimasDeCadaTarea', () => {
  it('se queda con la de id mayor de cada tarea y candidatura, en cualquier orden', () => {
    const lista = [
      crearEjecucion({ id: 3, candidaturaId: 12 }),
      crearEjecucion({ id: 7, candidaturaId: 12 }),
      crearEjecucion({ id: 5, candidaturaId: 20 }),
      crearEjecucion({ id: 6, tarea: 'ExtraerRegistro', candidaturaId: null }),
      crearEjecucion({ id: 2, tarea: 'ExtraerRegistro', candidaturaId: null }),
    ];

    expect([...ultimasDeCadaTarea(lista)].sort()).toEqual([5, 6, 7]);
  });

  it('la misma candidatura con otra tarea cuenta aparte', () => {
    const lista = [
      crearEjecucion({ id: 1, candidaturaId: 12 }),
      crearEjecucion({ id: 2, tarea: 'Rastreo', candidaturaId: 12 }),
    ];

    expect([...ultimasDeCadaTarea(lista)].sort()).toEqual([1, 2]);
  });

  it('sin ejecuciones, ninguna', () => {
    expect(ultimasDeCadaTarea([]).size).toBe(0);
  });
});
