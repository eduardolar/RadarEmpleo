import { CambioEstado, Nota } from '../interfaces/candidatura.interface';
import { lineaDeTiempo } from './linea-de-tiempo';

function cambio(id: number, fecha: string): CambioEstado {
  return { id, de: null, a: 'Detectada', fecha, comentario: null, origen: 'Manual' };
}

function nota(id: number, fecha: string): Nota {
  return { id, candidaturaId: 1, texto: `Nota ${id}`, fecha, origen: 'Manual' };
}

describe('lineaDeTiempo', () => {
  it('mezcla historial y notas de lo más reciente a lo más antiguo', () => {
    const elementos = lineaDeTiempo(
      [cambio(1, '2026-10-20T08:00:00Z'), cambio(2, '2026-10-25T08:00:00Z')],
      [nota(7, '2026-10-28T08:00:00Z'), nota(6, '2026-10-22T08:00:00Z')],
    );

    expect(elementos.map((e) => e.clave)).toEqual(['nota-7', 'estado-2', 'nota-6', 'estado-1']);
  });

  it('con la misma fecha pone el cambio de estado antes que la nota', () => {
    const fecha = '2026-10-20T08:00:00Z';

    const elementos = lineaDeTiempo([cambio(1, fecha)], [nota(1, fecha)]);

    expect(elementos.map((e) => e.tipo)).toEqual(['estado', 'nota']);
  });

  it('devuelve una lista vacía si no hay nada', () => {
    expect(lineaDeTiempo([], [])).toEqual([]);
  });
});
