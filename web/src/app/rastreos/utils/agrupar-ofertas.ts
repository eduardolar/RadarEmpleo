import { OfertaRastreo } from '../interfaces/rastreo.interface';

/** Las ofertas de un rastreo repartidas como se enseñan, cada grupo en el orden del informe */
export interface OfertasAgrupadas {
  /** Las que quedan por decidir y, en su sitio, las que se acaban de pasar al tablero */
  pendientes: OfertaRastreo[];
  enElTablero: OfertaRastreo[];
  ignoradas: OfertaRastreo[];
}

/**
 * Pendientes primero, luego las que ya están en el tablero y al final las que no interesan.
 * `enSuSitio`: ofertas pasadas al tablero en esta visita. Se quedan donde estaban, con «En el
 * tablero · Abrir ficha», para que la lista no salte bajo el dedo y la tarjeta siguiente no reciba
 * por error un segundo toque.
 */
export function agruparOfertas(
  ofertas: readonly OfertaRastreo[],
  enSuSitio: ReadonlySet<number> = new Set(),
): OfertasAgrupadas {
  const ordenadas = [...ofertas].sort((a, b) => a.linea - b.linea || a.id - b.id);
  const arriba = (o: OfertaRastreo) =>
    o.estado === 'Pendiente' || (o.estado === 'Convertida' && enSuSitio.has(o.id));
  return {
    pendientes: ordenadas.filter(arriba),
    enElTablero: ordenadas.filter((o) => o.estado === 'Convertida' && !arriba(o)),
    ignoradas: ordenadas.filter((o) => o.estado === 'Ignorada'),
  };
}

/** Cuántas quedan por decidir (lo que la lista de rastreos llama «pendientes») */
export function contarPendientes(ofertas: readonly OfertaRastreo[]): number {
  return ofertas.filter((o) => o.estado === 'Pendiente').length;
}
