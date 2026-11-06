import { opcionDeUrl } from './opciones-menu';

describe('opcionDeUrl', () => {
  it('reconoce cada sección por su ruta', () => {
    expect(opcionDeUrl('/hoy')?.etiqueta).toBe('Hoy');
    expect(opcionDeUrl('/tablero')?.etiqueta).toBe('Tablero');
    expect(opcionDeUrl('/apuntar')?.etiqueta).toBe('Apuntar');
  });

  it('la ficha cuenta como Tablero', () => {
    expect(opcionDeUrl('/candidaturas/12')?.etiqueta).toBe('Tablero');
  });

  it('la importación cuenta como Más', () => {
    expect(opcionDeUrl('/importacion')?.etiqueta).toBe('Más');
  });

  it('no tiene en cuenta los parámetros de la URL', () => {
    expect(opcionDeUrl('/tablero?texto=angular')?.etiqueta).toBe('Tablero');
    expect(opcionDeUrl('/candidaturas/12#notas')?.etiqueta).toBe('Tablero');
  });

  it('no confunde rutas que solo empiezan igual ni inventa una sección', () => {
    expect(opcionDeUrl('/hoyos')).toBeUndefined();
    expect(opcionDeUrl('/')).toBeUndefined();
  });
});
