import { OpcionMenu } from '../interfaces/opcion-menu.interface';

// Las comparten el menú lateral (escritorio) y la barra de abajo (móvil)
export const OPCIONES_MENU: readonly OpcionMenu[] = [
  { etiqueta: 'Hoy', descripcion: 'Pendientes y avisos', ruta: '/hoy', icono: 'hoy' },
  {
    etiqueta: 'Tablero',
    descripcion: 'Candidaturas por estado',
    ruta: '/tablero',
    subrutas: ['/candidaturas'],
    icono: 'tablero',
  },
  { etiqueta: 'Apuntar', descripcion: 'Alta rápida', ruta: '/apuntar', icono: 'apuntar' },
  {
    etiqueta: 'Rastreos',
    descripcion: 'Ofertas encontradas',
    ruta: '/rastreos',
    icono: 'rastreos',
  },
  {
    etiqueta: 'Más',
    descripcion: 'Perfil e importación',
    ruta: '/mas',
    subrutas: ['/importacion', '/perfil'],
    icono: 'mas',
  },
];

/** Opción del menú a la que pertenece una URL, con sus páginas interiores */
export function opcionDeUrl(url: string): OpcionMenu | undefined {
  const ruta = url.split(/[?#]/)[0];
  const pertenece = (base: string) => ruta === base || ruta.startsWith(`${base}/`);
  return OPCIONES_MENU.find((o) => [o.ruta, ...(o.subrutas ?? [])].some(pertenece));
}
