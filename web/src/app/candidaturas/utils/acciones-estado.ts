import { EstadoCandidatura } from '../interfaces/candidatura.interface';
import { textoAccionEstado } from './etiquetas';
import { esEstadoFinal } from './transiciones-permitidas';

/** Botón de cambio de estado de la ficha */
export interface AccionEstado {
  destino: EstadoCandidatura;
  texto: string;
  /** Es el siguiente paso: va primero y se pinta destacado. Nunca hay más de uno */
  destacada: boolean;
  /** Se usa poco: va dentro de «Más opciones» para dejar pocos botones a la vista */
  enMasOpciones: boolean;
}

const SIGUIENTE_PASO: Partial<Record<EstadoCandidatura, EstadoCandidatura>> = {
  Detectada: 'Preparada',
  Preparada: 'Enviada',
  Enviada: 'Entrevista',
  Entrevista: 'Oferta',
};

/** El paso que toca después de cada estado. Oferta y los estados finales no tienen */
export function siguientePaso(estado: EstadoCandidatura): EstadoCandidatura | undefined {
  return SIGUIENTE_PASO[estado];
}

/**
 * Además del siguiente paso, a la vista va lo habitual: Descartar, Reabrir y, desde Detectada,
 * Enviar, porque hay ofertas a las que se aplica en el momento sin prepararlas antes.
 */
function aLaVista(destino: EstadoCandidatura, actual: EstadoCandidatura): boolean {
  return (
    destino === siguientePaso(actual) ||
    destino === 'Descartada' ||
    destino === 'Detectada' ||
    (actual === 'Detectada' && destino === 'Enviada')
  );
}

/** 0: siguiente paso · 1: otros avances y «Reabrir» · 2: Descartar · 3: Cerrar */
function grupo(destino: EstadoCandidatura, actual: EstadoCandidatura): number {
  if (destino === siguientePaso(actual)) {
    return 0;
  }
  if (esEstadoFinal(destino)) {
    return destino === 'Descartada' ? 2 : 3;
  }
  return 1;
}

/**
 * Botones de la ficha a partir de los estados posibles que da la API: primero el siguiente paso,
 * luego los demás avances y al final Descartar y Cerrar. Dentro de cada grupo se respeta el orden
 * de la API, que es el del tablero.
 */
export function accionesEstado(
  actual: EstadoCandidatura,
  posibles: readonly EstadoCandidatura[],
): AccionEstado[] {
  return posibles
    .map((destino) => ({
      destino,
      texto: textoAccionEstado(destino, actual),
      destacada: destino === siguientePaso(actual),
      enMasOpciones: !aLaVista(destino, actual),
    }))
    .sort((a, b) => grupo(a.destino, actual) - grupo(b.destino, actual));
}
