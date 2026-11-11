import { ResultadoEscaneo } from '../interfaces/rastreo.interface';

/** «He encontrado 3 ofertas nuevas (1 ya estaba en el tablero).» o «No hay nada nuevo.» */
export function textoEscaneo(resultado: ResultadoEscaneo): string {
  const nuevas = resultado.ofertasNuevas;
  if (nuevas === 0) {
    return resultado.rastreosNuevos + resultado.rastreosActualizados > 0
      ? 'He leído los informes: no traen ofertas nuevas.'
      : 'No hay nada nuevo.';
  }

  const ofertas = nuevas === 1 ? '1 oferta nueva' : `${nuevas} ofertas nuevas`;
  const enTablero = resultado.yaEnElTablero;
  const yaEstaban =
    enTablero === 0
      ? ''
      : enTablero === 1
        ? ' (1 ya estaba en el tablero)'
        : ` (${enTablero} ya estaban en el tablero)`;
  return `He encontrado ${ofertas}${yaEstaban}.`;
}

/** Aviso de las líneas que parecen una oferta y no se han podido leer, o null si no hay */
export function textoSinLeer(lineas: number): string | null {
  if (lineas === 0) {
    return null;
  }
  const motivo = 'el enlace o «Empresa — Puesto» en negrita';
  return lineas === 1
    ? `1 línea de los informes parece una oferta pero no la he podido leer (le falta ${motivo}). La verás en el informe.`
    : `${lineas} líneas de los informes parecen una oferta pero no las he podido leer (les falta ${motivo}). Las verás en el informe.`;
}
