import { EjecucionAgente } from '../interfaces/ejecucion.interface';
import { crearEjecucion, crearGasto } from './ejecucion-de-prueba';
import {
  avisoCambio,
  avisoCambioCorto,
  avisosCambio,
  conPunto,
  duracion,
  ETIQUETA_ESTADO,
  NOMBRE_TAREA,
  relojEnCurso,
  textoEstado,
  usoDelDia,
  usoEstimado,
} from './textos-ejecucion';

const AHORA = new Date('2026-11-23T10:01:20Z');
const TOPE = 15;

function texto(cambios: Partial<EjecucionAgente>, tope: number | null = TOPE): string {
  return textoEstado(crearEjecucion(cambios), AHORA, tope);
}

describe('duracion', () => {
  it.each([
    [0, '0 s'],
    [59, '59 s'],
    [59.9, '59 s'],
    [60, '1 min'],
    [80, '1 min 20 s'],
    [300, '5 min'],
    [3599, '59 min 59 s'],
    [3600, '1 h'],
    [3720, '1 h 2 min'],
    [3725, '1 h 2 min'],
    [-5, '0 s'],
  ])('%s s es «%s»', (segundos, esperado) => {
    expect(duracion(segundos)).toBe(esperado);
  });

  it('sin un número (una fecha inválida) da null, nunca «NaN s»', () => {
    expect(duracion(NaN)).toBeNull();
    expect(duracion(Infinity)).toBeNull();
  });
});

describe('usoEstimado', () => {
  it('con el coste, el porcentaje redondeado del tope de hoy', () => {
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 0.18 }), TOPE)).toBe(
      'uso estimado: 1 % del tope de hoy',
    );
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 2.4 }), TOPE)).toBe(
      'uso estimado: 16 % del tope de hoy',
    );
  });

  it('terminada sin el coste: no se sabe, como mucho su presupuesto, redondeado hacia arriba', () => {
    const ejecucion = crearEjecucion({
      estado: 'Ok',
      inicio: '2026-11-23T10:00:00Z',
      costeEstimado: true,
      presupuestoUsd: 0.4,
    });

    expect(usoEstimado(ejecucion, TOPE)).toBe(
      'uso estimado: no se sabe, como mucho el 3 % del tope de hoy',
    );
  });

  it('un presupuesto que da el porcentaje justo no sube por la coma flotante', () => {
    const ejecucion = crearEjecucion({
      estado: 'Error',
      costeEstimado: true,
      presupuestoUsd: 0.45,
    });

    expect(usoEstimado(ejecucion, TOPE)).toBe(
      'uso estimado: no se sabe, como mucho el 3 % del tope de hoy',
    );
  });

  it('en curso, como mucho su presupuesto', () => {
    const ejecucion = crearEjecucion({ estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' });

    expect(usoEstimado(ejecucion, TOPE)).toBe('uso estimado: como mucho el 4 % del tope de hoy');
  });

  it('coste 0 (una oferta cerrada no llama a Claude): no ha gastado nada', () => {
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 0 }), TOPE)).toBe(
      'no ha gastado nada de tu tope de hoy',
    );
  });

  it('por debajo del 1 % dice «menos del 1 %», nunca «0 %»', () => {
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 0.05 }), TOPE)).toBe(
      'uso estimado: menos del 1 % del tope de hoy',
    );
  });

  it('«100 %» solo con el tope entero; justo por debajo se queda en 99', () => {
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 15 }), TOPE)).toBe(
      'uso estimado: 100 % del tope de hoy',
    );
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 14.95 }), TOPE)).toBe(
      'uso estimado: 99 % del tope de hoy',
    );
  });

  it('sin tope (o con 0) no dice nada', () => {
    const ejecucion = crearEjecucion({ estado: 'Ok', costeUsd: 0.18 });

    expect(usoEstimado(ejecucion, null)).toBeNull();
    expect(usoEstimado(ejecucion, 0)).toBeNull();
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 0 }), null)).toBeNull();
  });

  it('lo que no llegó a empezar no ha gastado nada: no dice nada', () => {
    expect(usoEstimado(crearEjecucion({ estado: 'EnCola' }), TOPE)).toBeNull();
    expect(usoEstimado(crearEjecucion({ estado: 'Cancelada' }), TOPE)).toBeNull();
  });
});

describe('usoDelDia', () => {
  it('el porcentaje del tope usado hoy', () => {
    expect(usoDelDia(crearGasto({ gastadoUsd: 0.42 }))).toBe(
      'Uso estimado de tu suscripción hoy: 3 % del tope',
    );
  });

  it('sin nada usado lo dice así; con muy poco, «menos del 1 %»', () => {
    expect(usoDelDia(crearGasto())).toBe('Hoy aún no has usado Claude');
    expect(usoDelDia(crearGasto({ gastadoUsd: 0.01 }))).toBe(
      'Uso estimado de tu suscripción hoy: menos del 1 % del tope',
    );
  });

  it('sin tope no dice nada', () => {
    expect(usoDelDia(crearGasto({ topeUsd: 0 }))).toBeNull();
  });
});

describe('textoEstado', () => {
  it('en cola por turno', () => {
    expect(texto({ estado: 'EnCola', espera: 'Turno' })).toBe(
      'En cola: hay otra tarea de Claude en marcha.',
    );
  });

  it('en cola por el bloqueo dice quién lo tiene', () => {
    expect(
      texto({ estado: 'EnCola', espera: 'BloqueoOcupado', quienBloquea: 'la tarea de los lunes' }),
    ).toBe('Esperando: la tarea de los lunes está usando Claude. Empieza sola cuando termine.');
  });

  it('en cola por el bloqueo sin saber quién', () => {
    expect(texto({ estado: 'EnCola', espera: 'BloqueoOcupado' })).toBe(
      'Esperando: Claude está ocupado con otra cosa. Empieza sola cuando termine.',
    );
  });

  it('recién encolada, sin motivo de espera', () => {
    expect(texto({ estado: 'EnCola' })).toBe('En cola: empieza en unos segundos.');
  });

  it.each([
    ['AnalizarEncaje', 'Analizando el encaje… · 1 min 20 s (máximo 5 min)'],
    ['ExtraerRegistro', 'Leyendo el registro… · 1 min 20 s (máximo 5 min)'],
    ['Rastreo', 'Rastreando ofertas… · 1 min 20 s (máximo 5 min)'],
    ['RellenarEnvio', 'Preparando el envío… · 1 min 20 s (máximo 5 min)'],
  ] as const)('en curso (%s): lo que hace y el tiempo con su máximo', (tarea, esperado) => {
    expect(texto({ estado: 'EnCurso', tarea, inicio: '2026-11-23T10:00:00Z' })).toBe(esperado);
  });

  it('en curso sin hora de inicio, o con una fecha inválida, solo lo que hace', () => {
    expect(texto({ estado: 'EnCurso' })).toBe('Analizando el encaje…');
    expect(texto({ estado: 'EnCurso', inicio: 'no es una fecha' })).toBe('Analizando el encaje…');
  });

  it('en curso con la cancelación pedida', () => {
    expect(texto({ estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z', puedeCancelar: false })).toBe(
      'Cancelando… se parará en unos segundos.',
    );
  });

  it('hecha: el tiempo que ha tardado y el uso estimado, sin USD', () => {
    const resultado = texto({
      estado: 'Ok',
      inicio: '2026-11-23T10:00:00Z',
      fin: '2026-11-23T10:02:10Z',
      costeUsd: 0.18,
    });

    expect(resultado).toBe('Hecha en 2 min 10 s · uso estimado: 1 % del tope de hoy');
  });

  it('hecha sin gastar nada', () => {
    const resultado = texto({
      estado: 'Ok',
      inicio: '2026-11-23T10:00:00Z',
      fin: '2026-11-23T10:00:12Z',
      costeUsd: 0,
    });

    expect(resultado).toBe('Hecha en 12 s · no ha gastado nada de tu tope de hoy');
  });

  it('hecha sin coste conocido: no se sabe, como mucho el presupuesto', () => {
    const resultado = texto({
      estado: 'Ok',
      inicio: '2026-11-23T10:00:00Z',
      fin: '2026-11-23T10:00:59Z',
      costeEstimado: true,
    });

    expect(resultado).toBe(
      'Hecha en 59 s · uso estimado: no se sabe, como mucho el 4 % del tope de hoy',
    );
  });

  it('hecha sin tope: solo el tiempo', () => {
    const resultado = texto(
      { estado: 'Ok', inicio: '2026-11-23T10:00:00Z', fin: '2026-11-23T10:02:10Z', costeUsd: 0.18 },
      null,
    );

    expect(resultado).toBe('Hecha en 2 min 10 s.');
  });

  it('hecha con una fecha inválida no pinta «NaN»', () => {
    const resultado = texto({ estado: 'Ok', inicio: 'x', fin: '2026-11-23T10:02:10Z' }, null);

    expect(resultado).toBe('Hecha.');
  });

  it('error: el mensaje de la API (MensajesEjecucion) detrás de «No ha salido.»', () => {
    expect(texto({ estado: 'Error', error: 'Claude ha devuelto un error.' })).toBe(
      'No ha salido. Claude ha devuelto un error.',
    );
    expect(texto({ estado: 'Error', error: 'Ha tardado más de 5 min y la he parado.' })).toBe(
      'No ha salido. Ha tardado más de 5 min y la he parado.',
    );
    expect(texto({ estado: 'Error', error: 'No encuentro Claude Code en este PC' })).toBe(
      'No ha salido. No encuentro Claude Code en este PC.',
    );
    expect(texto({ estado: 'Error' })).toBe('No ha salido.');
    expect(texto({ estado: 'Error', error: '  ' })).toBe('No ha salido.');
  });

  it('cancelada sin empezar: no ha gastado nada; empezada, con su uso si se sabe', () => {
    expect(texto({ estado: 'Cancelada' })).toBe('Cancelada antes de empezar. No ha gastado nada.');
    expect(texto({ estado: 'Cancelada', inicio: '2026-11-23T10:00:00Z', costeUsd: 0.15 })).toBe(
      'Cancelada antes de terminar · uso estimado: 1 % del tope de hoy',
    );
    expect(
      texto({ estado: 'Cancelada', inicio: '2026-11-23T10:00:00Z', costeEstimado: true }),
    ).toBe(
      'Cancelada antes de terminar · uso estimado: no se sabe, como mucho el 4 % del tope de hoy',
    );
    expect(texto({ estado: 'Cancelada', inicio: '2026-11-23T10:00:00Z' })).toBe(
      'Cancelada antes de terminar.',
    );
  });

  it('en curso sin `ahora`, la frase sin el tiempo', () => {
    expect(
      textoEstado(
        crearEjecucion({ estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' }),
        null,
        TOPE,
      ),
    ).toBe('Analizando el encaje…');
  });

  it('ningún estado habla de dólares ni pinta «NaN»', () => {
    const estados: Partial<EjecucionAgente>[] = [
      { estado: 'EnCola', espera: 'Turno' },
      { estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' },
      { estado: 'Ok', inicio: '2026-11-23T10:00:00Z', fin: '2026-11-23T10:01:00Z', costeUsd: 3 },
      { estado: 'Ok', inicio: '2026-11-23T10:00:00Z', costeEstimado: true },
      { estado: 'Ok', inicio: 'x', fin: 'y', costeUsd: 0 },
      { estado: 'Error', error: 'Ha llegado al tope de uso de esta tarea y la he parado.' },
      { estado: 'Cancelada' },
    ];

    for (const cambios of estados) {
      expect(texto(cambios)).not.toMatch(/USD|\$|dólar|NaN/i);
    }
  });
});

describe('avisosCambio', () => {
  it('sin cambios no hay avisos', () => {
    expect(avisosCambio(crearEjecucion())).toEqual([]);
  });

  it('uno por cada archivo que ha cambiado, sin el nombre del archivo', () => {
    expect(avisosCambio(crearEjecucion({ registroCambiado: true, perfilCambiado: true }))).toEqual([
      'Ojo: tu registro de candidaturas ha cambiado mientras trabajaba Claude. Si no lo has tocado tú, revísalo.',
      'Ojo: tu perfil de búsqueda ha cambiado mientras trabajaba Claude. Si no lo has tocado tú, revísalo.',
    ]);
    expect(avisoCambio('perfil')).not.toContain('.md');
  });
});

describe('etiquetas', () => {
  it('cada estado (en femenino, como la tarea) y cada tarea tienen su texto', () => {
    expect(Object.values(ETIQUETA_ESTADO)).toEqual([
      'En cola',
      'En curso',
      'Hecha',
      'No ha salido',
      'Cancelada',
    ]);
    expect(NOMBRE_TAREA.AnalizarEncaje).toBe('Análisis del encaje');
    expect(NOMBRE_TAREA.ExtraerRegistro).toBe('Lectura del registro');
    expect(NOMBRE_TAREA.RellenarEnvio).toBe('Preparar el envío');
  });
});

describe('conPunto', () => {
  it.each([
    ['Claude ha devuelto un error.', 'Claude ha devuelto un error.'],
    ['Sin conexión', 'Sin conexión.'],
    ['¿Está instalado?', '¿Está instalado?'],
    ['¡Para!', '¡Para!'],
    ['Leyendo…', 'Leyendo…'],
    ['  con espacios  ', 'con espacios.'],
  ])('%j → %j', (entrada, salida) => {
    expect(conPunto(entrada)).toBe(salida);
  });
});

describe('relojEnCurso', () => {
  const enCurso = (cambios: Partial<EjecucionAgente> = {}) =>
    crearEjecucion({ estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z', ...cambios });

  it('el tiempo que lleva y el máximo', () => {
    expect(relojEnCurso(enCurso(), AHORA)).toBe('1 min 20 s (máximo 5 min)');
  });

  it('null fuera de en curso, cancelándose, sin inicio o con una fecha inválida', () => {
    expect(relojEnCurso(crearEjecucion(), AHORA)).toBeNull();
    expect(relojEnCurso(enCurso({ puedeCancelar: false }), AHORA)).toBeNull();
    expect(relojEnCurso(enCurso({ inicio: null }), AHORA)).toBeNull();
    expect(relojEnCurso(enCurso({ inicio: 'x' }), AHORA)).toBeNull();
  });
});

describe('avisoCambioCorto', () => {
  it('en una línea y sin el nombre del archivo', () => {
    expect(avisoCambioCorto('registro')).toBe(
      'Ojo: cambió tu registro de candidaturas mientras trabajaba Claude.',
    );
    expect(avisosCambio(crearEjecucion({ perfilCambiado: true }), true)).toEqual([
      'Ojo: cambió tu perfil de búsqueda mientras trabajaba Claude.',
    ]);
  });
});
