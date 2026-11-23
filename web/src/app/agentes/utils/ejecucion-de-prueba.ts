import { EjecucionAgente, EstadoAgentes, GastoHoy } from '../interfaces/ejecucion.interface';

// Solo para los tests: datos inventados que cada prueba completa con lo que le importa

/**
 * Un análisis del encaje en cola. Si no se dicen, `puedeCancelar` y `puedeReintentar` salen del
 * estado, como en la API (en curso con la cancelación pedida hay que pasar `puedeCancelar: false`)
 */
export function crearEjecucion(cambios: Partial<EjecucionAgente> = {}): EjecucionAgente {
  const estado = cambios.estado ?? 'EnCola';
  return {
    id: 1,
    tarea: 'AnalizarEncaje',
    estado,
    espera: null,
    quienBloquea: null,
    fechaAlta: '2026-11-23T10:00:00Z',
    inicio: null,
    fin: null,
    presupuestoUsd: 0.5,
    tiempoMaximoSegundos: 300,
    costeUsd: null,
    costeEstimado: false,
    resumen: null,
    error: null,
    registroCambiado: false,
    perfilCambiado: false,
    candidaturaId: 12,
    candidatura: {
      id: 12,
      empresa: 'Acme',
      puesto: 'Desarrollador Angular',
      estado: 'Enviada',
      enlace: null,
    },
    rastreoId: null,
    estadoOferta: null,
    puedeCancelar: estado === 'EnCola' || estado === 'EnCurso',
    puedeReintentar: estado === 'Error' || estado === 'Cancelada',
    ...cambios,
  };
}

/** Nada usado hoy, con un tope de 15 */
export function crearGasto(cambios: Partial<GastoHoy> = {}): GastoHoy {
  return {
    hoy: '2026-11-23',
    gastadoUsd: 0,
    reservadoUsd: 0,
    topeUsd: 15,
    disponibleUsd: 15,
    ejecuciones: 0,
    ...cambios,
  };
}

/** Disponibles, con la cola en esta copia de la API y nada en marcha */
export function crearEstadoAgentes(cambios: Partial<EstadoAgentes> = {}): EstadoAgentes {
  return {
    disponibles: true,
    motivoNoDisponibles: null,
    versionClaude: '2.1.185',
    versionProbada: '2.1.185',
    versionDistinta: false,
    gasto: crearGasto(),
    enCurso: null,
    enCola: 0,
    llevaLaCola: true,
    motivoCola: null,
    ...cambios,
  };
}
