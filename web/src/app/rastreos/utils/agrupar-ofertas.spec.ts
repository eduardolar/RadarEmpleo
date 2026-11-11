import { agruparOfertas, contarPendientes } from './agrupar-ofertas';
import { crearOferta } from './rastreo-de-prueba';

describe('agruparOfertas', () => {
  it('reparte por estado y cada grupo va en el orden del informe', () => {
    const ofertas = [
      crearOferta({ id: 1, linea: 30, estado: 'Pendiente' }),
      crearOferta({ id: 2, linea: 10, estado: 'Ignorada' }),
      crearOferta({ id: 3, linea: 20, estado: 'Convertida', candidaturaId: 7 }),
      crearOferta({ id: 4, linea: 5, estado: 'Pendiente' }),
      crearOferta({ id: 5, linea: 40, estado: 'Ignorada' }),
    ];

    const grupos = agruparOfertas(ofertas);

    expect(grupos.pendientes.map((o) => o.id)).toEqual([4, 1]);
    expect(grupos.enElTablero.map((o) => o.id)).toEqual([3]);
    expect(grupos.ignoradas.map((o) => o.id)).toEqual([2, 5]);
  });

  it('con la misma línea, por id', () => {
    const grupos = agruparOfertas([
      crearOferta({ id: 9, linea: 1 }),
      crearOferta({ id: 2, linea: 1 }),
    ]);

    expect(grupos.pendientes.map((o) => o.id)).toEqual([2, 9]);
  });

  it('no cambia la lista que recibe', () => {
    const ofertas = [crearOferta({ id: 1, linea: 2 }), crearOferta({ id: 2, linea: 1 })];

    agruparOfertas(ofertas);

    expect(ofertas.map((o) => o.id)).toEqual([1, 2]);
  });

  it('las pasadas al tablero en esta visita se quedan en su sitio', () => {
    const ofertas = [
      crearOferta({ id: 1, linea: 1, estado: 'Pendiente' }),
      crearOferta({ id: 2, linea: 2, estado: 'Convertida', candidaturaId: 7 }),
      crearOferta({ id: 3, linea: 3, estado: 'Pendiente' }),
      crearOferta({ id: 4, linea: 4, estado: 'Convertida', candidaturaId: 8 }),
    ];

    const grupos = agruparOfertas(ofertas, new Set([2]));

    expect(grupos.pendientes.map((o) => o.id)).toEqual([1, 2, 3]);
    expect(grupos.enElTablero.map((o) => o.id)).toEqual([4]);
  });

  it('sin ofertas, tres grupos vacíos', () => {
    expect(agruparOfertas([])).toEqual({ pendientes: [], enElTablero: [], ignoradas: [] });
  });
});

describe('contarPendientes', () => {
  it('cuenta solo las pendientes', () => {
    expect(
      contarPendientes([
        crearOferta({ estado: 'Pendiente' }),
        crearOferta({ estado: 'Convertida' }),
        crearOferta({ estado: 'Ignorada' }),
        crearOferta({ estado: 'Pendiente' }),
      ]),
    ).toBe(2);
  });
});
