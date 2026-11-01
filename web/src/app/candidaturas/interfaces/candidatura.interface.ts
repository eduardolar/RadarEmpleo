// Tipos del contrato de /api/candidaturas (RadarEmpleo.Services/Contratos/Candidaturas.cs).
// Los enums viajan como texto y las fechas como cadenas ISO en UTC (terminan en «Z»).

export type EstadoCandidatura =
  'Detectada' | 'Preparada' | 'Enviada' | 'Entrevista' | 'Oferta' | 'Descartada' | 'Cerrada';

export type Modalidad = 'NoIndicada' | 'Remoto' | 'Hibrido' | 'Presencial';

export type Encaje = 'SinValorar' | 'Alto' | 'Medio' | 'TiroLargo' | 'No';

export type MotivoDescarte =
  | 'Experiencia'
  | 'Ingles'
  | 'Titulacion'
  | 'Zona'
  | 'Modalidad'
  | 'Perfil'
  | 'Salario'
  | 'Cerrada'
  | 'DecisionPropia'
  | 'Otro';

export type Origen = 'Manual' | 'Registro' | 'Rastreo' | 'Movil' | 'Agente';

export type TipoRecordatorio =
  'Seguimiento' | 'Entrevista' | 'PublicacionLinkedIn' | 'Plazo' | 'Otro';

/** Fila del listado y del tablero (GET /api/candidaturas) */
export interface CandidaturaResumen {
  id: number;
  empresaId: number;
  empresa: string;
  puesto: string;
  enlace: string | null;
  portal: string | null;
  modalidad: Modalidad;
  ubicacion: string | null;
  encaje: Encaje;
  pega: string | null;
  estado: EstadoCandidatura;
  fechaDeteccion: string;
  fechaEnvio: string | null;
  /** Cuándo entró en el estado actual: la que vale para los «días en el estado» */
  fechaEstado: string;
  /** Última modificación de cualquier tipo (también editar datos) */
  fechaUltimoCambio: string;
  motivoDescarte: MotivoDescarte | null;
  origen: Origen;
}

/** Ficha completa (GET /api/candidaturas/{id}) */
export interface CandidaturaDetalle {
  id: number;
  empresaId: number;
  empresa: string;
  puesto: string;
  enlace: string | null;
  portal: string | null;
  modalidad: Modalidad;
  modalidadDetalle: string | null;
  ubicacion: string | null;
  encaje: Encaje;
  pega: string | null;
  analisis: string | null;
  salario: string | null;
  estado: EstadoCandidatura;
  fechaDeteccion: string;
  fechaEnvio: string | null;
  /** Cuándo entró en el estado actual: la que vale para los «días en el estado» */
  fechaEstado: string;
  /** Última modificación de cualquier tipo (también editar datos) */
  fechaUltimoCambio: string;
  motivoDescarte: MotivoDescarte | null;
  motivoDescarteTexto: string | null;
  origen: Origen;
  seccionOrigen: string | null;
  estadosPosibles: EstadoCandidatura[];
  /** Del más antiguo al más reciente */
  historial: CambioEstado[];
  /** De la más reciente a la más antigua */
  notas: Nota[];
  recordatorios: Recordatorio[];
}

/** Apunte del historial: `de` es null en el alta */
export interface CambioEstado {
  id: number;
  de: EstadoCandidatura | null;
  a: EstadoCandidatura;
  fecha: string;
  comentario: string | null;
  origen: Origen;
}

export interface Nota {
  id: number;
  candidaturaId: number;
  texto: string;
  fecha: string;
  origen: Origen;
}

export interface Recordatorio {
  id: number;
  candidaturaId: number | null;
  tipo: TipoRecordatorio;
  titulo: string;
  fechaHora: string;
  hecho: boolean;
  notas: string | null;
}

/** Cuerpo de POST /api/candidaturas y PUT /api/candidaturas/{id}. La empresa va por nombre */
export interface GuardarCandidatura {
  empresa: string;
  puesto: string;
  enlace?: string | null;
  portal?: string | null;
  modalidad?: Modalidad;
  modalidadDetalle?: string | null;
  ubicacion?: string | null;
  encaje?: Encaje;
  pega?: string | null;
  analisis?: string | null;
  salario?: string | null;
}

/** Cuerpo de POST /api/candidaturas/{id}/estado (el motivo es obligatorio al descartar) */
export interface CambiarEstado {
  estado: EstadoCandidatura;
  comentario?: string | null;
  motivoDescarte?: MotivoDescarte | null;
  motivoDescarteTexto?: string | null;
}

export interface FiltroCandidaturas {
  estado?: EstadoCandidatura | null;
  texto?: string | null;
}
