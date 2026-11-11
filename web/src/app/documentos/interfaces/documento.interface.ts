// Tipos del contrato de documentos (RadarEmpleo.Services/Contratos/Documentos.cs).
// Los enums viajan como texto y las fechas como cadenas ISO en UTC (terminan en «Z»).

export type TipoDocumento = 'CV' | 'Carta' | 'GuiaEntrevista' | 'Correo' | 'Otro';

export type OrigenDocumento = 'Registro' | 'Manual' | 'Preparacion';

/** Por qué se sugiere: lleva el nombre de la empresa, parte de él o es de la semana de la candidatura */
export type CoincidenciaSugerencia = 'Empresa' | 'Parcial' | 'MismaSemana';

/** Documento vinculado a una candidatura (va en la ficha, `documentos`) */
export interface Documento {
  id: number;
  candidaturaId: number;
  tipo: TipoDocumento;
  nombreArchivo: string;
  /** Ruta dentro de Curriculum, siempre con «/» */
  rutaRelativa: string;
  /** Carpeta que lo contiene, con «/» («» si está en la raíz de Curriculum) */
  carpeta: string;
  /** «pdf» o «docx» */
  extension: string;
  /** Si el archivo sigue en su sitio (lo mira la API al pedir la ficha) */
  encontrado: boolean;
  origen: OrigenDocumento;
  /** El que vale de su tipo: solo hay uno vigente por tipo */
  vigente: boolean;
  fechaAlta: string;
}

/** Archivo de Curriculum que se propone (GET /api/documentos/sugeridos?candidaturaId=) */
export interface DocumentoSugerido {
  rutaRelativa: string;
  nombreArchivo: string;
  carpeta: string;
  tipo: TipoDocumento;
  extension: string;
  fechaModificacion: string;
  coincidencia: CoincidenciaSugerencia;
  /** Está en Archivo\ (CV antiguos) */
  esAntiguo: boolean;
  /** Ya está en la ficha de esta candidatura */
  yaVinculado: boolean;
  /** De una semana posterior al envío: no puede ser el CV que se mandó (la API lo pone al final) */
  posteriorAlEnvio: boolean;
}

/** Cuerpo de POST /api/candidaturas/{id}/documentos. Sin tipo, la API lo deduce del nombre */
export interface VincularDocumento {
  rutaRelativa: string;
  tipo?: TipoDocumento | null;
}
