export type SentidoLateral = 'anteriores' | 'siguientes';

/**
 * Hasta dónde desplazar un carril horizontal al pulsar una de sus flechas: hasta el inicio de la
 * columna anterior o de la siguiente, dejando `margen` libre a la izquierda para la flecha y el
 * degradado. `bordes` es dónde empieza cada columna, medido desde el principio del contenido; `maximo`,
 * el desplazamiento máximo. Devuelve null si hacia ese lado no queda nada.
 */
export function destinoLateral(
  bordes: readonly number[],
  actual: number,
  maximo: number,
  margen: number,
  sentido: SentidoLateral,
): number | null {
  if (maximo <= 0) {
    return null;
  }
  const paradas = [0, maximo, ...bordes.map((borde) => Math.round(borde - margen))]
    .map((parada) => Math.min(Math.max(parada, 0), maximo))
    .sort((a, b) => a - b);
  // 1 px de tolerancia: con zoom el desplazamiento se queda a medio píxel de la parada
  return sentido === 'siguientes'
    ? (paradas.find((parada) => parada > actual + 1) ?? null)
    : (paradas.reverse().find((parada) => parada < actual - 1) ?? null);
}

/**
 * Cuánto desplazar el carril para que una columna quede entera a la vista y fuera del margen de las
 * flechas. `izquierda` y `derecha` son sus bordes medidos desde el borde izquierdo visible del carril.
 * Si no cabe entera, manda su inicio.
 */
export function ajusteParaVer(
  izquierda: number,
  derecha: number,
  anchoVisible: number,
  margen: number,
): number {
  return Math.min(Math.max(derecha - (anchoVisible - margen), 0), izquierda - margen);
}
