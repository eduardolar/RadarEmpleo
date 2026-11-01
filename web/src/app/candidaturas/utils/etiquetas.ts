import {
  Encaje,
  EstadoCandidatura,
  Modalidad,
  MotivoDescarte,
  Origen,
  TipoRecordatorio,
} from '../interfaces/candidatura.interface';

// Textos en español de los enums de la API. Con Record<...>, si el backend añade un valor y se
// añade a su tipo, TypeScript obliga a darle etiqueta aquí.

export const ETIQUETAS_ESTADO: Record<EstadoCandidatura, string> = {
  Detectada: 'Detectada',
  Preparada: 'Preparada',
  Enviada: 'Enviada',
  Entrevista: 'Entrevista',
  Oferta: 'Oferta',
  Descartada: 'Descartada',
  Cerrada: 'Cerrada',
};

export const ETIQUETAS_MODALIDAD: Record<Modalidad, string> = {
  NoIndicada: 'Sin indicar',
  Remoto: 'Remoto',
  Hibrido: 'Híbrido',
  Presencial: 'Presencial',
};

export const ETIQUETAS_ENCAJE: Record<Encaje, string> = {
  SinValorar: 'Sin valorar',
  Alto: 'Alto',
  Medio: 'Medio',
  TiroLargo: 'Tiro largo',
  No: 'No encaja',
};

export const ETIQUETAS_MOTIVO_DESCARTE: Record<MotivoDescarte, string> = {
  Experiencia: 'Experiencia',
  Ingles: 'Inglés',
  Titulacion: 'Titulación',
  Zona: 'Zona',
  Modalidad: 'Modalidad',
  Perfil: 'Perfil',
  Salario: 'Salario',
  Cerrada: 'Oferta cerrada',
  DecisionPropia: 'Decisión propia',
  Otro: 'Otro',
};

export const ETIQUETAS_ORIGEN: Record<Origen, string> = {
  Manual: 'Manual',
  Registro: 'Registro',
  Rastreo: 'Rastreo',
  Movil: 'Móvil',
  Agente: 'Agente',
};

export const ETIQUETAS_TIPO_RECORDATORIO: Record<TipoRecordatorio, string> = {
  Seguimiento: 'Seguimiento',
  Entrevista: 'Entrevista',
  PublicacionLinkedIn: 'Publicación en LinkedIn',
  Plazo: 'Plazo',
  Otro: 'Otro',
};

export interface Opcion<T extends string> {
  valor: T;
  etiqueta: string;
}

/** Opciones para un mat-select, en el orden en que están las etiquetas */
export function opciones<T extends string>(etiquetas: Record<T, string>): Opcion<T>[] {
  return (Object.entries(etiquetas) as [T, string][]).map(([valor, etiqueta]) => ({
    valor,
    etiqueta,
  }));
}

/** Texto del botón que mueve una candidatura de `actual` a `destino` */
export function textoAccionEstado(destino: EstadoCandidatura, actual: EstadoCandidatura): string {
  switch (destino) {
    case 'Preparada':
      return 'Marcar preparada';
    case 'Enviada':
      return 'Marcar enviada';
    case 'Descartada':
      return 'Descartar';
    case 'Cerrada':
      // «Cerrar» a secas se confunde con cerrar el diálogo o la ficha
      return 'Cerrar candidatura';
    case 'Detectada':
      return actual === 'Descartada' || actual === 'Cerrada' ? 'Reabrir' : 'Pasar a Detectada';
    default:
      return `Pasar a ${ETIQUETAS_ESTADO[destino]}`;
  }
}
