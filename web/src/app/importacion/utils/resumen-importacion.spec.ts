import { ResumenImportacion } from '../interfaces/importacion.interface';
import { crearFila, crearResultado } from './importacion-de-prueba';
import {
  filasAplicables,
  filasConResultado,
  lineasResumen,
  textoBotonAplicar,
  textoEnviosEnTexto,
  textoEstadoFila,
  textoNadaQueAnadir,
  textoResultado,
  tituloNadaQueAplicar,
} from './resumen-importacion';

function resumen(cambios: Partial<ResumenImportacion> = {}): ResumenImportacion {
  return {
    nuevas: 0,
    avanzan: 0,
    yaExisten: 0,
    paraRevisar: 0,
    sinLeer: 0,
    enviosEnTexto: 0,
    ...cambios,
  };
}

describe('filasAplicables', () => {
  it('solo deja las nuevas y las que cambian de estado', () => {
    const filas = [
      crearFila({ clave: 'a', resultado: 'Nueva' }),
      crearFila({ clave: 'b', resultado: 'AvanzaEstado' }),
      crearFila({ clave: 'c', resultado: 'YaExiste' }),
      crearFila({ clave: 'd', resultado: 'Conflicto' }),
    ];

    expect(filasAplicables(filas).map((f) => f.clave)).toEqual(['a', 'b']);
  });

  it('una fila repetida en el registro no se pide dos veces: la segunda es conflicto', () => {
    const filas = [
      crearFila({ clave: 'a', resultado: 'Nueva' }),
      crearFila({ clave: 'a', resultado: 'Conflicto', motivo: 'Aparece dos veces en el registro' }),
    ];

    expect(filasAplicables(filas).map((f) => f.clave)).toEqual(['a']);
  });
});

describe('filasConResultado', () => {
  it('filtra por resultado', () => {
    const filas = [
      crearFila({ clave: 'a', resultado: 'YaExiste' }),
      crearFila({ clave: 'b', resultado: 'Conflicto' }),
    ];

    expect(filasConResultado(filas, 'Conflicto').map((f) => f.clave)).toEqual(['b']);
  });
});

describe('lineasResumen', () => {
  it('sin nada no da ninguna línea', () => {
    expect(lineasResumen(resumen())).toEqual([]);
  });

  it('con 1 va en singular y se salta las que están a cero', () => {
    expect(lineasResumen(resumen({ nuevas: 1, avanzan: 1, yaExisten: 1 }))).toEqual([
      '1 nueva',
      '1 cambia de estado',
      '1 ya la tienes',
    ]);
  });

  it('con varias va en plural', () => {
    expect(
      lineasResumen(resumen({ nuevas: 3, avanzan: 2, yaExisten: 4, paraRevisar: 5, sinLeer: 6 })),
    ).toEqual([
      '3 nuevas',
      '2 cambian de estado',
      '4 ya las tienes',
      '5 para revisar',
      '6 no se pueden leer',
    ]);
  });

  it('los envíos en texto no cuentan: no están entre las filas', () => {
    expect(lineasResumen(resumen({ enviosEnTexto: 40 }))).toEqual([]);
  });
});

describe('textoBotonAplicar', () => {
  it('distingue singular y plural', () => {
    expect(textoBotonAplicar(resumen({ nuevas: 1 }))).toBe('Añadir 1 candidatura');
    expect(textoBotonAplicar(resumen({ nuevas: 8 }))).toBe('Añadir 8 candidaturas');
  });

  it('si solo cambian de estado no dice «añadir»', () => {
    expect(textoBotonAplicar(resumen({ avanzan: 1 }))).toBe('Actualizar 1 candidatura');
    expect(textoBotonAplicar(resumen({ avanzan: 2 }))).toBe('Actualizar 2 candidaturas');
  });

  it('si hay de las dos cosas dice las dos', () => {
    expect(textoBotonAplicar(resumen({ nuevas: 1, avanzan: 1 }))).toBe(
      'Añadir 1 candidatura y actualizar 1',
    );
    expect(textoBotonAplicar(resumen({ nuevas: 3, avanzan: 2 }))).toBe(
      'Añadir 3 candidaturas y actualizar 2',
    );
  });
});

describe('textoResultado', () => {
  it('cuenta las añadidas en singular y plural', () => {
    expect(textoResultado(crearResultado({ creadas: 1 }))).toBe('Se ha añadido 1 candidatura.');
    expect(textoResultado(crearResultado({ creadas: 8 }))).toBe('Se han añadido 8 candidaturas.');
  });

  it('cuenta las actualizadas', () => {
    expect(textoResultado(crearResultado({ creadas: 0, avanzadas: 1 }))).toBe(
      'Se ha actualizado 1 candidatura.',
    );
    expect(textoResultado(crearResultado({ creadas: 0, avanzadas: 2 }))).toBe(
      'Se han actualizado 2 candidaturas.',
    );
  });

  it('junta las dos cosas', () => {
    expect(textoResultado(crearResultado({ creadas: 1, avanzadas: 1 }))).toBe(
      'Se ha añadido 1 candidatura y se ha actualizado 1.',
    );
    expect(textoResultado(crearResultado({ creadas: 3, avanzadas: 2 }))).toBe(
      'Se han añadido 3 candidaturas y se han actualizado 2.',
    );
  });

  it('con 0 y 0 (otra aplicación se ha adelantado) no habla de «0 candidaturas»', () => {
    expect(textoResultado(crearResultado({ creadas: 0, sinCambios: 8 }))).toBe(
      'No había nada nuevo que añadir.',
    );
  });

  it('dice cuántos conflictos quedan', () => {
    expect(textoResultado(crearResultado({ creadas: 2, paraRevisar: 1 }))).toBe(
      'Se han añadido 2 candidaturas. Queda 1 para revisar.',
    );
    expect(textoResultado(crearResultado({ creadas: 2, paraRevisar: 3 }))).toBe(
      'Se han añadido 2 candidaturas. Quedan 3 para revisar.',
    );
  });

  it('no enseña sinCambios: mezcla las que ya existían con las que no se pidieron', () => {
    expect(textoResultado(crearResultado({ creadas: 2, sinCambios: 40 }))).not.toContain('40');
  });
});

describe('textoNadaQueAnadir', () => {
  it('sin nada que mirar solo dice que no hay nuevas', () => {
    expect(textoNadaQueAnadir(resumen({ yaExisten: 4 }))).toBe(
      'No hay candidaturas nuevas que añadir.',
    );
  });

  it('con conflictos o líneas sin leer manda a mirarlas (el título ya dice que no hay nada por añadir)', () => {
    for (const cambios of [{ paraRevisar: 1 }, { sinLeer: 2 }]) {
      expect(textoNadaQueAnadir(resumen(cambios))).toBe('Abajo tienes las que conviene revisar.');
    }
  });
});

describe('tituloNadaQueAplicar', () => {
  it('«Todo al día» solo si no queda nada que mirar, se haya aplicado o no', () => {
    expect(tituloNadaQueAplicar(resumen({ yaExisten: 4 }), false)).toBe('Todo al día');
    expect(tituloNadaQueAplicar(resumen({ yaExisten: 4 }), true)).toBe('Todo al día');
  });

  it('con filas para revisar o sin leer no está al día', () => {
    for (const cambios of [{ paraRevisar: 1 }, { sinLeer: 2 }]) {
      expect(tituloNadaQueAplicar(resumen(cambios), false)).toBe('No queda nada por añadir');
      expect(tituloNadaQueAplicar(resumen(cambios), true)).toBe('Importación hecha');
    }
  });
});

describe('textoEnviosEnTexto', () => {
  it('sin envíos no hay aviso', () => {
    expect(textoEnviosEnTexto(0)).toBeNull();
  });

  it('distingue singular y plural', () => {
    expect(textoEnviosEnTexto(1)).toBe(
      'Hay 1 envío escrito como texto; ese se podrá traer más adelante.',
    );
    expect(textoEnviosEnTexto(41)).toBe(
      'Hay unos 41 envíos escritos como texto; esos se podrán traer más adelante.',
    );
  });
});

describe('textoEstadoFila', () => {
  it('una nueva dice cómo llega', () => {
    expect(textoEstadoFila(crearFila({ estadoRegistro: 'Enviada' }))).toBe('Enviada');
  });

  it('una que cambia dice de dónde a dónde', () => {
    const fila = crearFila({
      resultado: 'AvanzaEstado',
      estadoRegistro: 'Entrevista',
      estadoActual: 'Enviada',
    });

    expect(textoEstadoFila(fila)).toBe('De Enviada a Entrevista');
  });

  it('una que ya existe dice cómo está en la app, aunque vaya por delante del registro', () => {
    const fila = crearFila({
      resultado: 'YaExiste',
      estadoRegistro: 'Enviada',
      estadoActual: 'Entrevista',
    });

    expect(textoEstadoFila(fila)).toBe('Entrevista');
  });

  it('un conflicto dice cómo llega: el motivo explica el resto', () => {
    const fila = crearFila({
      resultado: 'Conflicto',
      estadoRegistro: 'Enviada',
      estadoActual: 'Cerrada',
    });

    expect(textoEstadoFila(fila)).toBe('Enviada');
  });
});
