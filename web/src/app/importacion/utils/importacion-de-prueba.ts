import {
  FilaVistaPrevia,
  ResultadoAplicacion,
  VistaPreviaImportacion,
} from '../interfaces/importacion.interface';

// Solo para los tests: datos inventados que cada prueba completa con lo que le importa

export function crearFila(cambios: Partial<FilaVistaPrevia> = {}): FilaVistaPrevia {
  return {
    clave: 'acme|desarrollador angular|2026-08-03',
    resultado: 'Nueva',
    empresa: 'Acme',
    puesto: 'Desarrollador Angular',
    fecha: '2026-08-03',
    estadoRegistro: 'Enviada',
    estadoActual: null,
    candidaturaId: null,
    motivo: null,
    avisos: [],
    delTextoLibre: false,
    ...cambios,
  };
}

/** Vista previa con estas filas y el resumen que les corresponde */
export function crearVista(
  filas: FilaVistaPrevia[] = [],
  cambios: Partial<VistaPreviaImportacion> = {},
): VistaPreviaImportacion {
  const contar = (resultado: FilaVistaPrevia['resultado']) =>
    filas.filter((f) => f.resultado === resultado).length;
  const sinLeer = cambios.sinLeer ?? [];
  return {
    origen: 'Tabla',
    hashRegistro: 'a'.repeat(64),
    ejecucionId: null,
    resumen: {
      nuevas: contar('Nueva'),
      avanzan: contar('AvanzaEstado'),
      yaExisten: contar('YaExiste'),
      paraRevisar: contar('Conflicto'),
      sinLeer: sinLeer.length,
      enviosEnTexto: 0,
      delTextoLibre: 0,
    },
    filas,
    sinLeer,
    fuera: [],
    dudas: [],
    ...cambios,
  };
}

export function crearResultado(cambios: Partial<ResultadoAplicacion> = {}): ResultadoAplicacion {
  return {
    creadas: 1,
    avanzadas: 0,
    sinCambios: 0,
    paraRevisar: 0,
    radarActualizado: true,
    ...cambios,
  };
}
