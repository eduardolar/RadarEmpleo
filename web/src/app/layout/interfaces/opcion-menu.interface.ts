/** Una sección de la app en el menú lateral (PC) y en la barra de abajo (móvil) */
export interface OpcionMenu {
  etiqueta: string;
  descripcion: string;
  ruta: string;
  /** Otras rutas que pertenecen a esta sección (la ficha cuenta como Tablero) */
  subrutas?: readonly string[];
  icono: IconoMenuNombre;
}

export type IconoMenuNombre = 'hoy' | 'tablero' | 'apuntar' | 'rastreos' | 'mas';
