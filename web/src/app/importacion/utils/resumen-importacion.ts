import { ETIQUETAS_ESTADO } from '../../candidaturas/utils/etiquetas';
import {
  FilaVistaPrevia,
  ResultadoAplicacion,
  ResultadoFila,
  ResumenImportacion,
} from '../interfaces/importacion.interface';

/** Solo estas filas cambian algo al aplicar */
const RESULTADOS_APLICABLES: readonly ResultadoFila[] = ['Nueva', 'AvanzaEstado'];

/** Va en la misma zona que el resultado: el import es válido aunque falle esta copia */
export const AVISO_RADAR =
  'Tus candidaturas están guardadas. La copia de tu carpeta no se ha podido actualizar: se intentará en el próximo cambio.';

function contar(total: number, singular: string, plural: string): string {
  return `${total} ${total === 1 ? singular : plural}`;
}

export function filasAplicables(filas: FilaVistaPrevia[]): FilaVistaPrevia[] {
  return filas.filter((f) => RESULTADOS_APLICABLES.includes(f.resultado));
}

export function filasConResultado(
  filas: FilaVistaPrevia[],
  resultado: ResultadoFila,
): FilaVistaPrevia[] {
  return filas.filter((f) => f.resultado === resultado);
}

/** «3 nuevas», «1 cambia de estado»... sin las que están a cero */
export function lineasResumen(resumen: ResumenImportacion): string[] {
  const lineas: [number, string, string][] = [
    [resumen.nuevas, 'nueva', 'nuevas'],
    [resumen.avanzan, 'cambia de estado', 'cambian de estado'],
    [resumen.yaExisten, 'ya la tienes', 'ya las tienes'],
    [resumen.paraRevisar, 'para revisar', 'para revisar'],
    // No «sin leer»: sonaría a correo pendiente de abrir
    [resumen.sinLeer, 'no se puede leer', 'no se pueden leer'],
  ];
  return lineas
    .filter(([total]) => total > 0)
    .map(([total, singular, plural]) => contar(total, singular, plural));
}

/** Qué pasa con cada grupo plegado y qué puede hacer Eduardo con él */
export const EXPLICACION_YA_EXISTEN = 'Ya están en la app. No se tocan.';
export const EXPLICACION_PARA_REVISAR =
  'Estas no se guardan. Cada una dice por qué; si hace falta, corrígela a mano en la app o en tu archivo.';
export const EXPLICACION_SIN_LEER =
  'Corrige estas líneas en tu archivo, guárdalo y pulsa «Volver a leer».';

/** «Añadir 3 candidaturas», «Actualizar 1 candidatura» o «Añadir 3 candidaturas y actualizar 2» */
export function textoBotonAplicar({ nuevas, avanzan }: ResumenImportacion): string {
  const candidaturas = (total: number) => contar(total, 'candidatura', 'candidaturas');
  if (nuevas > 0 && avanzan > 0) {
    return `Añadir ${candidaturas(nuevas)} y actualizar ${avanzan}`;
  }
  return nuevas > 0 ? `Añadir ${candidaturas(nuevas)}` : `Actualizar ${candidaturas(avanzan)}`;
}

/**
 * Lo que ha hecho la aplicación. `sinCambios` no se enseña: mezcla las que ya existían con las que no
 * se pidieron. Con 0 y 0 es que otra aplicación se ha adelantado, y no es ningún fallo.
 */
export function textoResultado({ creadas, avanzadas, paraRevisar }: ResultadoAplicacion): string {
  const hecho = textoHecho(creadas, avanzadas);
  if (paraRevisar === 0) {
    return hecho;
  }
  return `${hecho} ${paraRevisar === 1 ? 'Queda 1' : `Quedan ${paraRevisar}`} para revisar.`;
}

function textoHecho(creadas: number, avanzadas: number): string {
  if (creadas === 0 && avanzadas === 0) {
    return 'No había nada nuevo que añadir.';
  }
  if (creadas === 0) {
    return avanzadas === 1
      ? 'Se ha actualizado 1 candidatura.'
      : `Se han actualizado ${avanzadas} candidaturas.`;
  }
  const anadidas =
    creadas === 1 ? 'Se ha añadido 1 candidatura' : `Se han añadido ${creadas} candidaturas`;
  if (avanzadas === 0) {
    return `${anadidas}.`;
  }
  return `${anadidas} y ${avanzadas === 1 ? 'se ha actualizado 1' : `se han actualizado ${avanzadas}`}.`;
}

function quedaAlgoPorMirar({ paraRevisar, sinLeer }: ResumenImportacion): boolean {
  return paraRevisar + sinLeer > 0;
}

/**
 * Título cuando no queda nada que guardar. «Todo al día» solo si tampoco hay filas para revisar ni sin
 * leer: con ellas no lo está, aunque ya no haya nada que añadir
 */
export function tituloNadaQueAplicar(resumen: ResumenImportacion, recienAplicado: boolean): string {
  if (!quedaAlgoPorMirar(resumen)) {
    return 'Todo al día';
  }
  return recienAplicado ? 'Importación hecha' : 'No queda nada por añadir';
}

/** Debajo del título cuando no hay nada que guardar (y no se acaba de aplicar) */
export function textoNadaQueAnadir(resumen: ResumenImportacion): string {
  return quedaAlgoPorMirar(resumen)
    ? 'Abajo tienes las que conviene revisar.'
    : 'No hay candidaturas nuevas que añadir.';
}

/** null si no hay ninguno. Son una estimación: por eso «unos» */
export function textoEnviosEnTexto(enviosEnTexto: number): string | null {
  if (enviosEnTexto === 0) {
    return null;
  }
  return enviosEnTexto === 1
    ? 'Hay 1 envío escrito como texto; ese se podrá importar más adelante.'
    : `Hay unos ${enviosEnTexto} envíos escritos como texto; esos se podrán importar más adelante.`;
}

/** «De Enviada a Entrevista» si cambia; si ya existe, cómo está en la app; si no, cómo llega */
export function textoEstadoFila(fila: FilaVistaPrevia): string {
  const registro = ETIQUETAS_ESTADO[fila.estadoRegistro];
  if (!fila.estadoActual) {
    return registro;
  }
  const actual = ETIQUETAS_ESTADO[fila.estadoActual];
  if (fila.resultado === 'AvanzaEstado') {
    return `De ${actual} a ${registro}`;
  }
  return fila.resultado === 'YaExiste' ? actual : registro;
}
