import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// «mailto:», «ftp://»... Lo que va seguido de un número es un puerto, no un esquema («acme.com:8080»)
const OTRO_ESQUEMA = /^[a-z][a-z0-9+.-]*:(?!\d)/i;
const ESQUEMA_WEB = /^https?:\/\//i;

function esUrl(texto: string): URL | null {
  try {
    return new URL(texto);
  } catch {
    return null;
  }
}

/**
 * Deja el enlace listo para guardar: sin esquema, el navegador abriría «www.linkedin.com/jobs/1» como
 * una ruta de la propia app, así que se le pone «https://». Devuelve '' si no hay enlace y null si lo
 * escrito no es una dirección web (otro esquema como mailto:, o un texto que no es un dominio).
 */
export function normalizarEnlace(enlace: string | null | undefined): string | null {
  const limpio = enlace?.trim() ?? '';
  if (limpio === '') {
    return '';
  }
  if (ESQUEMA_WEB.test(limpio)) {
    return esUrl(limpio) ? limpio : null;
  }
  if (OTRO_ESQUEMA.test(limpio)) {
    return null;
  }
  const conEsquema = `https://${limpio.replace(/^\/+/, '')}`;
  return esUrl(conEsquema)?.hostname.includes('.') ? conEsquema : null;
}

export const enlaceWeb: ValidatorFn = (control: AbstractControl): ValidationErrors | null =>
  normalizarEnlace(control.value as string | null) === null ? { enlaceWeb: true } : null;
