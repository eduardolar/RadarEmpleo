import { EstadoCandidatura } from '../interfaces/candidatura.interface';
import { accionesEstado, siguientePaso } from './acciones-estado';
import { estadosPosibles } from './transiciones-permitidas';

describe('siguientePaso', () => {
  it('da un solo paso después de cada estado del camino normal', () => {
    expect(siguientePaso('Detectada')).toBe('Preparada');
    expect(siguientePaso('Preparada')).toBe('Enviada');
    expect(siguientePaso('Enviada')).toBe('Entrevista');
    expect(siguientePaso('Entrevista')).toBe('Oferta');
  });

  it('no da ninguno en Oferta ni en los estados finales', () => {
    expect(siguientePaso('Oferta')).toBeUndefined();
    expect(siguientePaso('Descartada')).toBeUndefined();
    expect(siguientePaso('Cerrada')).toBeUndefined();
  });
});

describe('accionesEstado', () => {
  function acciones(estado: EstadoCandidatura) {
    return accionesEstado(estado, estadosPosibles(estado));
  }

  function textos(estado: EstadoCandidatura): string[] {
    return acciones(estado).map((a) => a.texto);
  }

  it('pone primero el siguiente paso, luego los saltos y al final Descartar y Cerrar', () => {
    expect(textos('Detectada')).toEqual([
      'Marcar preparada',
      'Marcar enviada',
      'Pasar a Entrevista',
      'Pasar a Oferta',
      'Descartar',
      'Cerrar candidatura',
    ]);
    expect(textos('Enviada')).toEqual([
      'Pasar a Entrevista',
      'Pasar a Oferta',
      'Descartar',
      'Cerrar candidatura',
    ]);
  });

  it.each<[EstadoCandidatura, EstadoCandidatura[]]>([
    ['Detectada', ['Preparada']],
    ['Preparada', ['Enviada']],
    ['Enviada', ['Entrevista']],
    ['Entrevista', ['Oferta']],
    ['Oferta', []],
    ['Descartada', []],
    ['Cerrada', []],
  ])('en %s destaca como mucho un botón', (estado, esperados) => {
    expect(
      acciones(estado)
        .filter((a) => a.destacada)
        .map((a) => a.destino),
    ).toEqual(esperados);
  });

  it.each<[EstadoCandidatura, string[], string[]]>([
    [
      'Detectada',
      ['Marcar preparada', 'Marcar enviada', 'Descartar'],
      ['Pasar a Entrevista', 'Pasar a Oferta', 'Cerrar candidatura'],
    ],
    [
      'Preparada',
      ['Marcar enviada', 'Descartar'],
      ['Pasar a Entrevista', 'Pasar a Oferta', 'Cerrar candidatura'],
    ],
    ['Enviada', ['Pasar a Entrevista', 'Descartar'], ['Pasar a Oferta', 'Cerrar candidatura']],
    ['Entrevista', ['Pasar a Oferta', 'Descartar'], ['Cerrar candidatura']],
    ['Oferta', ['Descartar'], ['Cerrar candidatura']],
    ['Descartada', ['Reabrir'], ['Cerrar candidatura']],
    ['Cerrada', ['Reabrir', 'Descartar'], []],
  ])('en %s deja a la vista lo habitual y el resto en «Más opciones»', (estado, vista, menu) => {
    const todas = acciones(estado);

    expect(todas.filter((a) => !a.enMasOpciones).map((a) => a.texto)).toEqual(vista);
    expect(todas.filter((a) => a.enMasOpciones).map((a) => a.texto)).toEqual(menu);
  });

  it('en un estado final ofrece «Reabrir» antes que el otro estado final', () => {
    expect(textos('Descartada')).toEqual(['Reabrir', 'Cerrar candidatura']);
    expect(textos('Cerrada')).toEqual(['Reabrir', 'Descartar']);
  });
});
