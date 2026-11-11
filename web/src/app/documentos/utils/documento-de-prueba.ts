import { Documento, DocumentoSugerido } from '../interfaces/documento.interface';

// Solo para los tests: datos mínimos que cada prueba completa con lo que le importa

const SEMANA = 'Candidaturas/Semana 2026-08-31 (31 ago - 6 sep)';

export function crearDocumento(cambios: Partial<Documento> = {}): Documento {
  return {
    id: 1,
    candidaturaId: 1,
    tipo: 'CV',
    nombreArchivo: 'CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf',
    rutaRelativa: `${SEMANA}/CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf`,
    carpeta: SEMANA,
    extension: 'pdf',
    encontrado: true,
    origen: 'Manual',
    vigente: true,
    fechaAlta: '2026-11-06T10:00:00Z',
    ...cambios,
  };
}

export function crearSugerido(cambios: Partial<DocumentoSugerido> = {}): DocumentoSugerido {
  return {
    rutaRelativa: `${SEMANA}/CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf`,
    nombreArchivo: 'CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf',
    carpeta: SEMANA,
    tipo: 'CV',
    extension: 'pdf',
    fechaModificacion: '2026-09-04T08:00:00Z',
    coincidencia: 'Empresa',
    esAntiguo: false,
    yaVinculado: false,
    posteriorAlEnvio: false,
    ...cambios,
  };
}
