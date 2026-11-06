import {
  EstadoCandidatura,
  Modalidad,
  MotivoDescarte,
} from '../../candidaturas/interfaces/candidatura.interface';

// Tipos del contrato de /api/importacion (RadarEmpleo.Services/Contratos/Importacion.cs).
// Los enums viajan como texto; `fecha` es un día suelto («2026-10-30»), sin hora ni zona.

export type OrigenLote = 'Tabla';

export type ResultadoFila = 'Nueva' | 'AvanzaEstado' | 'YaExiste' | 'Conflicto';

/** Fila tal como está en el registro: `enlace` aún no está normalizado y puede no llegar a guardarse */
export interface FilaLote {
  linea: number;
  fecha: string;
  fechaAproximada: boolean;
  empresa: string;
  puesto: string;
  modalidad: Modalidad;
  modalidadDetalle: string | null;
  ubicacion: string | null;
  portal: string | null;
  enlace: string | null;
  estado: EstadoCandidatura;
  motivoDescarte: MotivoDescarte | null;
  motivoDescarteTexto: string | null;
  nota: string | null;
  /** El estado sale de una palabra del medio del texto; la fila ya trae el aviso para comprobarlo */
  estadoDeducido: boolean;
}

/** Sale de la vista previa y vuelve sin tocar al aplicar: el servidor lo recalcula todo */
export interface LoteImportacion {
  version: number;
  origen: OrigenLote;
  filas: FilaLote[];
}

export interface ResumenImportacion {
  nuevas: number;
  avanzan: number;
  yaExisten: number;
  paraRevisar: number;
  sinLeer: number;
  /** Líneas con «ENVIADA» fuera de la tabla: solo se cuentan, no se importan */
  enviosEnTexto: number;
}

/** Qué pasaría con una fila al aplicar. `clave` se repite si el registro trae la misma fila dos veces */
export interface FilaVistaPrevia {
  clave: string;
  resultado: ResultadoFila;
  empresa: string;
  puesto: string;
  fecha: string;
  estadoRegistro: EstadoCandidatura;
  estadoActual: EstadoCandidatura | null;
  candidaturaId: number | null;
  /** Por qué es un conflicto, para enseñarlo tal cual */
  motivo: string | null;
  avisos: string[];
}

export interface FilaSinLeer {
  linea: number;
  motivo: string;
}

/** Respuesta de POST /api/importacion/tabla/previsualizar. No se ha escrito nada */
export interface VistaPreviaImportacion {
  lote: LoteImportacion;
  resumen: ResumenImportacion;
  filas: FilaVistaPrevia[];
  sinLeer: FilaSinLeer[];
}

/** Cuerpo de POST /api/importacion/lote/aplicar */
export interface AplicarLote {
  lote: LoteImportacion;
  claves: string[];
}

/** Respuesta de aplicar. Cuenta todo el lote, no solo las `claves` enviadas */
export interface ResultadoAplicacion {
  creadas: number;
  avanzadas: number;
  /** Las que ya existían más las que no iban en `claves` */
  sinCambios: number;
  /** Todos los conflictos del lote */
  paraRevisar: number;
  /** False si no se ha podido escribir la copia en la carpeta: lo importado sigue siendo válido */
  radarActualizado: boolean;
}
