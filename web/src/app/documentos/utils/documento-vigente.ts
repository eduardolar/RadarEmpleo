import { Documento, TipoDocumento } from '../interfaces/documento.interface';

/** Tipos que tienen pestaña propia en el panel, en este orden */
export type TipoPestana = Extract<TipoDocumento, 'CV' | 'Carta'>;
export const TIPOS_PESTANA: readonly TipoPestana[] = ['CV', 'Carta'];

/**
 * El documento que vale de ese tipo. La API garantiza uno solo vigente por tipo; si por lo que sea no
 * viene marcado ninguno, se toma el más reciente para no dejar la pestaña vacía.
 */
export function documentoVigente(
  documentos: readonly Documento[],
  tipo: TipoDocumento,
): Documento | null {
  const deEseTipo = documentos.filter((d) => d.tipo === tipo);
  return (
    deEseTipo.find((d) => d.vigente) ??
    [...deEseTipo].sort((a, b) => b.fechaAlta.localeCompare(a.fechaAlta) || b.id - a.id)[0] ??
    null
  );
}

/** Pestañas que se enseñan: solo las de los tipos que tienen algún documento */
export function pestanasConDocumento(documentos: readonly Documento[]): TipoPestana[] {
  return TIPOS_PESTANA.filter((tipo) => documentos.some((d) => d.tipo === tipo));
}

/** Lo que no sale en una pestaña: CV y cartas anteriores y los demás tipos (guías, correos...) */
export function otrosDocumentos(documentos: readonly Documento[]): Documento[] {
  const enPestana = new Set(
    TIPOS_PESTANA.map((tipo) => documentoVigente(documentos, tipo)?.id).filter(
      (id) => id !== undefined,
    ),
  );
  return documentos.filter((d) => !enPestana.has(d.id));
}
