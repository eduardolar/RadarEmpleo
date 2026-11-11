import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

// Siempre relativa, sin environment.apiUrl: la API solo deja enmarcar el PDF desde su mismo origen
// (frame-ancestors 'self'), así que una URL a otro origen dejaría el visor en blanco
const BASE = '/api/documentos';

function comprobarId(id: number): number {
  if (!Number.isSafeInteger(id) || id <= 0) {
    throw new Error(`Id de documento no válido: ${id}`);
  }
  return id;
}

/** Dirección del archivo para abrirlo o descargarlo. Solo se construye desde un id entero positivo */
export function urlArchivo(id: number): string {
  return `${BASE}/${comprobarId(id)}/archivo`;
}

/** Lo mismo con el visor de PDF ajustado al ancho */
export function urlVisor(id: number): string {
  return `${urlArchivo(id)}#view=FitH`;
}

/**
 * El [src] de un iframe necesita una SafeResourceUrl. Solo se marca como segura la que sale de
 * urlVisor, que únicamente admite un id numérico: nunca un texto que venga de la API o del usuario.
 */
export function urlVisorSegura(sanitizer: DomSanitizer, id: number): SafeResourceUrl {
  return sanitizer.bypassSecurityTrustResourceUrl(urlVisor(id));
}
