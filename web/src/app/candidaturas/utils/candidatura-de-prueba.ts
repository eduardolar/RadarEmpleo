import { CandidaturaDetalle, CandidaturaResumen } from '../interfaces/candidatura.interface';

// Solo para los tests: datos mínimos que cada prueba completa con lo que le importa

export function crearResumen(cambios: Partial<CandidaturaResumen> = {}): CandidaturaResumen {
  return {
    id: 1,
    empresaId: 1,
    empresa: 'Acme',
    puesto: 'Desarrollador Angular',
    enlace: null,
    portal: null,
    modalidad: 'Remoto',
    ubicacion: null,
    encaje: 'Alto',
    pega: null,
    estado: 'Detectada',
    fechaDeteccion: '2026-10-27T08:00:00Z',
    fechaEnvio: null,
    fechaEstado: '2026-10-27T08:00:00Z',
    fechaUltimoCambio: '2026-10-27T08:00:00Z',
    motivoDescarte: null,
    origen: 'Manual',
    ...cambios,
  };
}

export function crearDetalle(cambios: Partial<CandidaturaDetalle> = {}): CandidaturaDetalle {
  return {
    ...crearResumen(),
    modalidadDetalle: null,
    analisis: null,
    salario: null,
    motivoDescarteTexto: null,
    seccionOrigen: null,
    estadosPosibles: ['Preparada', 'Enviada', 'Entrevista', 'Oferta', 'Descartada', 'Cerrada'],
    historial: [
      {
        id: 1,
        de: null,
        a: 'Detectada',
        fecha: '2026-10-27T08:00:00Z',
        comentario: null,
        origen: 'Manual',
      },
    ],
    notas: [],
    recordatorios: [],
    documentos: [],
    hayCarpetaCurriculum: true,
    ...cambios,
  };
}
