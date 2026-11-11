import { TipoDocumento } from '../interfaces/documento.interface';

export const ETIQUETAS_TIPO_DOCUMENTO: Record<TipoDocumento, string> = {
  CV: 'CV',
  Carta: 'Carta',
  GuiaEntrevista: 'Guía de entrevista',
  Correo: 'Correo',
  Otro: 'Otro documento',
};

/** Para los avisos: «He quitado el CV...», «La carta ya está en la ficha» */
export const TIPO_DOCUMENTO_CON_ARTICULO: Record<TipoDocumento, string> = {
  CV: 'el CV',
  Carta: 'la carta',
  GuiaEntrevista: 'la guía de entrevista',
  Correo: 'el correo',
  Otro: 'el documento',
};

// El nombre de la persona va en todos los archivos y no ayuda a distinguirlos
const NOMBRE_PERSONA = /_?Eduardo_Lara_Navarro/gi;
const PREFIJOS: readonly [RegExp, string][] = [
  [/^Carta_Presentacion(?=_|$)/i, 'Carta'],
  [/^Carta_Motivacion(?=_|$)/i, 'Carta de motivación'],
];
const SEMANA = /^Semana \d{4}-\d{2}-\d{2} \((.+)\)$/;
// «CV_Eduardo_Lara_Navarro.pdf» se quedaría en «CV» a secas: es el que no es para ninguna oferta
const GENERALES: Record<string, string> = { CV: 'CV general', Carta: 'Carta general' };

/**
 * Nombre de archivo para leerlo de un vistazo: sin extensión, sin el nombre de la persona y con «·»
 * entre las partes. «CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf» queda «CV · NET · MarcoTalento».
 */
export function nombreLegible(nombreArchivo: string): string {
  let nombre = nombreArchivo.replace(/\.[^.]+$/, '').replace(NOMBRE_PERSONA, '');
  for (const [prefijo, texto] of PREFIJOS) {
    nombre = nombre.replace(prefijo, texto);
  }
  const partes = nombre.split('_').filter((p) => p.trim() !== '');
  if (partes.length === 0) {
    return nombreArchivo;
  }
  return partes.length === 1 && GENERALES[partes[0]] ? GENERALES[partes[0]] : partes.join(' · ');
}

/**
 * Carpeta para leerla de un vistazo: de una semana, solo los días («31 ago - 6 sep»); de Archivo, que
 * es antigua; y la raíz de Curriculum, por su nombre.
 */
export function carpetaLegible(carpeta: string): string {
  if (carpeta === '') {
    return 'Carpeta Curriculum';
  }
  const partes = carpeta.split('/');
  const ultima = partes[partes.length - 1];
  const semana = SEMANA.exec(ultima);
  if (semana) {
    return semana[1];
  }
  if (partes[0].toLowerCase() === 'archivo') {
    const subcarpeta = partes.slice(1).join(' / ').replace(/_/g, ' ').trim();
    return subcarpeta ? `Archivo · ${subcarpeta}` : 'Archivo';
  }
  return ultima;
}
