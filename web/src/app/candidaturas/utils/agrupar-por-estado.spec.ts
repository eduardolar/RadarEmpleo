import { agruparPorEstado } from './agrupar-por-estado';
import { crearResumen } from './candidatura-de-prueba';

describe('agruparPorEstado', () => {
  it('devuelve las siete columnas en el orden del tablero aunque no haya candidaturas', () => {
    const columnas = agruparPorEstado([]);

    expect(columnas.map((c) => c.estado)).toEqual([
      'Detectada',
      'Preparada',
      'Enviada',
      'Entrevista',
      'Oferta',
      'Descartada',
      'Cerrada',
    ]);
    expect(columnas.every((c) => c.candidaturas.length === 0)).toBe(true);
  });

  it('mete cada candidatura en su columna respetando el orden de llegada', () => {
    const lista = [
      crearResumen({ id: 1, estado: 'Enviada' }),
      crearResumen({ id: 2, estado: 'Detectada' }),
      crearResumen({ id: 3, estado: 'Enviada' }),
      crearResumen({ id: 4, estado: 'Cerrada' }),
    ];

    const columnas = agruparPorEstado(lista);
    const ids = Object.fromEntries(
      columnas.map((c) => [c.estado, c.candidaturas.map((cand) => cand.id)]),
    );

    expect(ids).toEqual({
      Detectada: [2],
      Preparada: [],
      Enviada: [1, 3],
      Entrevista: [],
      Oferta: [],
      Descartada: [],
      Cerrada: [4],
    });
  });
});
