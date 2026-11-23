import { EstadoCandidatura } from '../../candidaturas/interfaces/candidatura.interface';

// Tipos del contrato de /api/importacion (RadarEmpleo.Services/Contratos/Importacion.cs).
// Los enums viajan como texto; `fecha` es un día suelto («2026-10-30»), sin hora ni zona.

export type OrigenLote = 'Tabla' | 'RegistroConClaude';

export type ResultadoFila = 'Nueva' | 'AvanzaEstado' | 'YaExiste' | 'Conflicto';

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

/** `linea` puede repetirse (o ser 0): no sirve para identificar la fila */
export interface FilaSinLeer {
  linea: number;
  motivo: string;
}

/** Extraída por Claude que se queda fuera porque ya estaba, con el aviso para enseñarlo tal cual */
export interface FilaFuera {
  linea: number;
  empresa: string;
  puesto: string;
  fecha: string;
  aviso: string;
}

/** Respuesta de POST /api/importacion/tabla/previsualizar. No se ha escrito nada */
export interface VistaPreviaImportacion {
  origen: OrigenLote;
  /** SHA-256 del registro leído: aplicar lo exige igual, y si ha cambiado da un 409 */
  hashRegistro: string;
  ejecucionId: number | null;
  resumen: ResumenImportacion;
  filas: FilaVistaPrevia[];
  sinLeer: FilaSinLeer[];
  fuera: FilaFuera[];
}

/** Cuerpo de POST /api/importacion/lote/aplicar: el servidor rehace el lote, el cliente no manda filas */
export interface AplicarImportacion {
  origen: OrigenLote;
  ejecucionId: number | null;
  hashRegistro: string;
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
