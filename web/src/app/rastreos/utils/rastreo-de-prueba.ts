import {
  OfertaRastreo,
  RastreoDetalle,
  RastreoResumen,
  ResultadoEscaneo,
} from '../interfaces/rastreo.interface';

// Solo para los tests: datos mínimos que cada prueba completa con lo que le importa

export function crearOferta(cambios: Partial<OfertaRastreo> = {}): OfertaRastreo {
  return {
    id: 1,
    rastreoId: 1,
    linea: 10,
    empresa: 'Acme',
    puesto: 'Desarrollador Angular',
    enlace: 'https://ejemplo.com/ofertas/1',
    encaje: 'Alto',
    pega: null,
    estado: 'Pendiente',
    candidaturaId: null,
    fechaEstado: '2026-11-06T09:00:00Z',
    textoOriginal: '- **Alto · Acme — Desarrollador Angular** https://ejemplo.com/ofertas/1',
    ...cambios,
  };
}

export function crearRastreo(cambios: Partial<RastreoResumen> = {}): RastreoResumen {
  return {
    id: 1,
    fecha: '2026-11-06',
    titulo: 'Rastreo 06/11 (viernes)',
    origen: 'Registro',
    total: 1,
    pendientes: 1,
    fechaEscaneo: '2026-11-08T09:00:00Z',
    ...cambios,
  };
}

export function crearDetalleRastreo(cambios: Partial<RastreoDetalle> = {}): RastreoDetalle {
  return {
    ...crearRastreo(),
    rutaInforme: 'ofertas-enviadas.md',
    informe: 'Semana mala.',
    ofertas: [crearOferta()],
    ...cambios,
  };
}

export function crearResultadoEscaneo(cambios: Partial<ResultadoEscaneo> = {}): ResultadoEscaneo {
  return {
    rastreosNuevos: 0,
    rastreosActualizados: 0,
    ofertasNuevas: 0,
    yaEnElTablero: 0,
    lineasSinLeer: 0,
    avisos: [],
    ...cambios,
  };
}
