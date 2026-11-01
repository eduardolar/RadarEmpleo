import { HttpErrorResponse } from '@angular/common/http';
import { ErrorApi } from '../interfaces/error-api.interface';

const SIN_CONEXION =
  'No se puede conectar con RadarEmpleo. Comprueba que está abierto en tu ordenador y vuelve a probar.';
const VOLVER_A_PROBAR = 'Vuelve a probar en un momento.';

// 502-504 sin cuerpo: los da el proxy de ng serve (o Tailscale en el móvil) cuando la API está parada
const POR_CODIGO: Record<number, string> = {
  404: 'No se ha encontrado.',
  409: 'No se puede hacer esa operación ahora.',
  502: SIN_CONEXION,
  503: SIN_CONEXION,
  504: SIN_CONEXION,
};

/**
 * Convierte la respuesta de error de la API (ProblemDetails en español) en un mensaje legible y los
 * errores por campo de los 400. Si no hay ProblemDetails (sin conexión, un 500 sin cuerpo...), da
 * un mensaje genérico.
 */
export function convertirErrorApi(error: unknown): ErrorApi {
  if (!(error instanceof HttpErrorResponse)) {
    return {
      estado: 0,
      mensaje: `Ha ocurrido un error inesperado. ${VOLVER_A_PROBAR}`,
      errores: {},
    };
  }

  if (error.status === 0) {
    return { estado: 0, mensaje: SIN_CONEXION, errores: {} };
  }

  const problema = esObjeto(error.error) ? error.error : {};
  const errores = leerErrores(problema['errors']);
  const inesperado = `Ha ocurrido un error inesperado (código ${error.status}). ${VOLVER_A_PROBAR}`;
  // El título de un 500 solo dice que ha fallado: se sustituye por el mensaje que dice qué hacer
  const titulo = error.status >= 500 ? null : textoNoVacio(problema['title']);
  const mensaje =
    textoNoVacio(problema['detail']) ?? titulo ?? POR_CODIGO[error.status] ?? inesperado;

  return { estado: error.status, mensaje, errores };
}

function leerErrores(valor: unknown): Record<string, string[]> {
  if (!esObjeto(valor)) {
    return {};
  }

  const errores: Record<string, string[]> = {};
  for (const [campo, mensajes] of Object.entries(valor)) {
    const lista = Array.isArray(mensajes) ? mensajes.filter((m) => typeof m === 'string') : [];
    if (lista.length > 0) {
      errores[campo] = lista;
    }
  }
  return errores;
}

function esObjeto(valor: unknown): valor is Record<string, unknown> {
  return typeof valor === 'object' && valor !== null && !Array.isArray(valor);
}

function textoNoVacio(valor: unknown): string | null {
  return typeof valor === 'string' && valor.trim() !== '' ? valor : null;
}
