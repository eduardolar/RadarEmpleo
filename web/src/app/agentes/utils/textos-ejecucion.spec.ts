import { EjecucionAgente } from '../interfaces/ejecucion.interface';
import { crearEjecucion, crearGasto } from './ejecucion-de-prueba';
import {
  avisosCambio,
  duracion,
  ETIQUETA_ESTADO,
  NOMBRE_TAREA,
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
});

describe('usoEstimado', () => {
  it('con el coste, el porcentaje redondeado del tope del día', () => {
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 0.18 }), TOPE)).toBe(
      'uso estimado: 1 % del tope del día',
    );
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 2.4 }), TOPE)).toBe(
      'uso estimado: 16 % del tope del día',
    );
  });

  it('sin el coste, «hasta el» con el presupuesto, redondeado hacia arriba', () => {
    const ejecucion = crearEjecucion({
      estado: 'Ok',
      inicio: '2026-11-23T10:00:00Z',
      costeEstimado: true,
      presupuestoUsd: 0.4,
    });

    expect(usoEstimado(ejecucion, TOPE)).toBe('uso estimado: hasta el 3 % del tope del día');
  });

  it('un presupuesto que da el porcentaje justo no sube por la coma flotante', () => {
    const ejecucion = crearEjecucion({
      estado: 'Error',
      costeEstimado: true,
      presupuestoUsd: 0.45,
    });

    expect(usoEstimado(ejecucion, TOPE)).toBe('uso estimado: hasta el 3 % del tope del día');
  });

  it('en curso, hasta su presupuesto', () => {
    const ejecucion = crearEjecucion({ estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' });

    expect(usoEstimado(ejecucion, TOPE)).toBe('uso estimado: hasta el 4 % del tope del día');
  });

  it('coste 0 (una oferta cerrada no llama a Claude) es el 0 %', () => {
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 0 }), TOPE)).toBe(
      'uso estimado: 0 % del tope del día',
    );
  });

  it('por debajo del 1 % dice «menos del 1 %», nunca «0 %»', () => {
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 0.05 }), TOPE)).toBe(
      'uso estimado: menos del 1 % del tope del día',
    );
  });

  it('«100 %» solo con el tope entero; justo por debajo se queda en 99', () => {
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 15 }), TOPE)).toBe(
      'uso estimado: 100 % del tope del día',
    );
    expect(usoEstimado(crearEjecucion({ estado: 'Ok', costeUsd: 14.95 }), TOPE)).toBe(
      'uso estimado: 99 % del tope del día',
    );
  });

  it('sin tope (o con 0) no dice nada', () => {
    const ejecucion = crearEjecucion({ estado: 'Ok', costeUsd: 0.18 });

    expect(usoEstimado(ejecucion, null)).toBeNull();
    expect(usoEstimado(ejecucion, 0)).toBeNull();
  });

  it('lo que no llegó a empezar no ha gastado nada: no dice nada', () => {
    expect(usoEstimado(crearEjecucion({ estado: 'EnCola' }), TOPE)).toBeNull();
    expect(usoEstimado(crearEjecucion({ estado: 'Cancelada' }), TOPE)).toBeNull();
  });
});

describe('usoDelDia', () => {
  it('el porcentaje del tope diario usado hoy', () => {
    expect(usoDelDia(crearGasto({ gastadoUsd: 0.42 }))).toBe(
      'Uso estimado de tu suscripción hoy: 3 % del tope diario',
    );
  });

  it('sin nada usado, el 0 %; con muy poco, «menos del 1 %»', () => {
    expect(usoDelDia(crearGasto())).toBe('Uso estimado de tu suscripción hoy: 0 % del tope diario');
    expect(usoDelDia(crearGasto({ gastadoUsd: 0.01 }))).toBe(
      'Uso estimado de tu suscripción hoy: menos del 1 % del tope diario',
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
      'Esperando: Claude está ocupado en otra sesión. Empieza sola cuando termine.',
    );
  });

  it('recién encolada, sin motivo de espera', () => {
    expect(texto({ estado: 'EnCola' })).toBe('En cola: empieza en unos segundos.');
  });

  it.each([
    ['AnalizarEncaje', 'Analizando el encaje… · 1 min 20 s de 5 min'],
    ['ExtraerRegistro', 'Leyendo el registro… · 1 min 20 s de 5 min'],
    ['Rastreo', 'Rastreando ofertas… · 1 min 20 s de 5 min'],
  ] as const)('en curso (%s): lo que hace y el tiempo de su máximo', (tarea, esperado) => {
    expect(texto({ estado: 'EnCurso', tarea, inicio: '2026-11-23T10:00:00Z' })).toBe(esperado);
  });

  it('en curso sin hora de inicio, solo lo que hace', () => {
    expect(texto({ estado: 'EnCurso' })).toBe('Analizando el encaje…');
  });

  it('en curso con la cancelación pedida', () => {
    expect(texto({ estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z', puedeCancelar: false })).toBe(
      'Cancelando: la paro en unos segundos.',
    );
  });

  it('hecho: el tiempo que ha tardado y el uso estimado, sin USD', () => {
    const resultado = texto({
      estado: 'Ok',
      inicio: '2026-11-23T10:00:00Z',
      fin: '2026-11-23T10:02:10Z',
      costeUsd: 0.18,
    });

    expect(resultado).toBe('Hecho en 2 min 10 s · uso estimado: 1 % del tope del día');
  });

  it('hecho sin coste conocido: «hasta el» con el presupuesto', () => {
    const resultado = texto({
      estado: 'Ok',
      inicio: '2026-11-23T10:00:00Z',
      fin: '2026-11-23T10:00:59Z',
      costeEstimado: true,
    });

    expect(resultado).toBe('Hecho en 59 s · uso estimado: hasta el 4 % del tope del día');
  });

  it('hecho sin tope: solo el tiempo', () => {
    const resultado = texto(
      { estado: 'Ok', inicio: '2026-11-23T10:00:00Z', fin: '2026-11-23T10:02:10Z', costeUsd: 0.18 },
      null,
    );

    expect(resultado).toBe('Hecho en 2 min 10 s.');
  });

  it('error: el mensaje de la API tal cual', () => {
    expect(texto({ estado: 'Error', error: 'Claude ha devuelto un error.' })).toBe(
      'No ha salido: Claude ha devuelto un error.',
    );
    expect(texto({ estado: 'Error' })).toBe('No ha salido.');
  });

  it('cancelada', () => {
    expect(texto({ estado: 'Cancelada' })).toBe('Cancelada.');
  });

  it('ningún estado habla de dólares', () => {
    const estados: Partial<EjecucionAgente>[] = [
      { estado: 'EnCola', espera: 'Turno' },
      { estado: 'EnCurso', inicio: '2026-11-23T10:00:00Z' },
      { estado: 'Ok', inicio: '2026-11-23T10:00:00Z', fin: '2026-11-23T10:01:00Z', costeUsd: 3 },
      { estado: 'Ok', inicio: '2026-11-23T10:00:00Z', costeEstimado: true },
      { estado: 'Error', error: 'Ha llegado al tope de uso de esta tarea y la he parado.' },
      { estado: 'Cancelada' },
    ];

    for (const cambios of estados) {
      expect(texto(cambios)).not.toMatch(/USD|\$|dólar/i);
    }
  });
});

describe('avisosCambio', () => {
  it('sin cambios no hay avisos', () => {
    expect(avisosCambio(crearEjecucion())).toEqual([]);
  });

  it('uno por cada archivo que ha cambiado', () => {
    expect(avisosCambio(crearEjecucion({ registroCambiado: true, perfilCambiado: true }))).toEqual([
      'Ojo: ofertas-enviadas.md ha cambiado mientras trabajaba Claude. Si no lo has tocado tú, revísalo.',
      'Ojo: perfil-busqueda.md ha cambiado mientras trabajaba Claude. Si no lo has tocado tú, revísalo.',
    ]);
  });
});

describe('etiquetas', () => {
  it('cada estado y cada tarea tienen su texto', () => {
    expect(Object.values(ETIQUETA_ESTADO)).toEqual([
      'En cola',
      'En curso',
      'Hecho',
      'No ha salido',
      'Cancelada',
    ]);
    expect(NOMBRE_TAREA.AnalizarEncaje).toBe('Análisis del encaje');
    expect(NOMBRE_TAREA.ExtraerRegistro).toBe('Lectura del registro');
  });
});
