import { Encaje } from '../../candidaturas/interfaces/candidatura.interface';

// Tipos del contrato de /api/rastreos (RadarEmpleo.Services/Contratos/Rastreos.cs).
// Los enums viajan como texto; la fecha del rastreo como «AAAA-MM-DD» y el resto en ISO UTC («Z»).

/** De dónde sale: una sección del registro, un informe de la tarea de los lunes o la app (F7) */
export type OrigenRastreo = 'Registro' | 'TareaLunes' | 'App';

export type EstadoOfertaRastreo = 'Pendiente' | 'Convertida' | 'Ignorada';

/** Un rastreo en la lista (GET /api/rastreos), del más reciente al más antiguo */
export interface RastreoResumen {
  id: number;
  /** Día del rastreo, «AAAA-MM-DD» */
  fecha: string;
  titulo: string;
  origen: OrigenRastreo;
  total: number;
  pendientes: number;
  fechaEscaneo: string;
}

/** Un rastreo con su informe y sus ofertas (GET /api/rastreos/{id}) */
export interface RastreoDetalle extends RastreoResumen {
  /** Archivo del que sale, dentro de Curriculum */
  rutaInforme: string;
  /** El informe en Markdown, sin el título */
  informe: string;
  ofertas: OfertaRastreo[];
}

export interface OfertaRastreo {
  id: number;
  rastreoId: number;
  /** Línea del informe en la que estaba: ordena las ofertas como las ordenó el equipo */
  linea: number;
  empresa: string;
  puesto: string;
  enlace: string;
  encaje: Encaje;
  pega: string | null;
  estado: EstadoOfertaRastreo;
  /** Su candidatura del tablero, si se ha pasado */
  candidaturaId: number | null;
  fechaEstado: string;
  /** La línea del informe tal cual, con su Markdown */
  textoOriginal: string;
}

/** POST /api/rastreos/escanear */
export interface ResultadoEscaneo {
  rastreosNuevos: number;
  rastreosActualizados: number;
  /** Contando las que ya estaban en el tablero */
  ofertasNuevas: number;
  yaEnElTablero: number;
  /** Líneas que parecen una oferta pero no se han podido leer */
  lineasSinLeer: number;
  avisos: string[];
}

/** POST /api/rastreos/ofertas/{id}/detectar */
export interface ResultadoDeteccion {
  oferta: OfertaRastreo;
  candidaturaId: number;
  /** Ya había una candidatura con ese enlace: se ha enlazado sin crear otra */
  yaExistia: boolean;
}
