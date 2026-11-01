// Portales cuyos enlaces llevan el título de la oferta. En LinkedIn no: sus enlaces son solo un número
const RUTAS_CON_PUESTO: readonly { dominio: string; ruta: RegExp }[] = [
  // /madrid/desarrollador-frontend/of-i123
  { dominio: 'infojobs.net', ruta: /^\/[^/]+\/([^/]+)\/of-/ },
  // /desarrollador-frontend-acme/angular/rf-123 (aquí el título acaba con el nombre de la empresa)
  { dominio: 'tecnoempleo.com', ruta: /^\/([^/]+)\/[^/]+\/rf-/ },
];

// Coletillas de género del final: -h-m, -m-h, -m-f, -f-m, -h-m-x, -m-f-d...
const COLETILLA_GENERO = /-+[hmf]-[hmf](-[dx])?$/;

/** Un % suelto en la dirección haría fallar decodeURIComponent: entonces se queda como venía */
function decodificar(texto: string): string {
  try {
    return decodeURIComponent(texto);
  } catch {
    return texto;
  }
}

/**
 * Título de la oferta sacado de un enlace de InfoJobs o Tecnoempleo («Desarrollador frontend»), para
 * no tener que escribirlo en el móvil. Casi siempre sale sin tildes y puede llevar de más: es un
 * borrador a la vista en el campo Puesto. Si el enlace no lo trae, null.
 */
export function puestoDesdeEnlace(enlace: string | null | undefined): string | null {
  let url: URL;
  try {
    url = new URL(enlace?.trim() ?? '');
  } catch {
    return null;
  }
  const host = url.hostname.toLowerCase();
  const portal = RUTAS_CON_PUESTO.find((p) => host === p.dominio || host.endsWith(`.${p.dominio}`));
  const titulo = portal?.ruta.exec(url.pathname)?.[1];
  const palabras =
    titulo &&
    decodificar(titulo).replace(COLETILLA_GENERO, '').split('-').filter(Boolean).join(' ');
  // Hay ofertas con un título de relleno («/madrid/x/of-...»)
  if (!palabras || palabras.length < 3) {
    return null;
  }
  return palabras.charAt(0).toUpperCase() + palabras.slice(1);
}
