import { ajusteParaVer, destinoLateral } from './desplazamiento-lateral';

describe('destinoLateral', () => {
  // Cuatro columnas de 200 px con 10 px de hueco, empezando a 4 px; caben 600 px a la vista
  const BORDES = [4, 214, 424, 634];
  const MAXIMO = 238;

  it('avanza hasta la columna siguiente dejando el margen de la flecha a la izquierda', () => {
    expect(destinoLateral(BORDES, 0, MAXIMO, 64, 'siguientes')).toBe(150);
  });

  it('no pasa del final aunque la columna siguiente quede más allá', () => {
    expect(destinoLateral(BORDES, 150, MAXIMO, 64, 'siguientes')).toBe(MAXIMO);
  });

  it('vuelve a la columna anterior y, desde la primera, al principio', () => {
    expect(destinoLateral(BORDES, MAXIMO, MAXIMO, 64, 'anteriores')).toBe(150);
    expect(destinoLateral(BORDES, 150, MAXIMO, 64, 'anteriores')).toBe(0);
  });

  it('a medio camino entre dos columnas va a la más cercana en ese sentido', () => {
    expect(destinoLateral(BORDES, 100, MAXIMO, 64, 'siguientes')).toBe(150);
    expect(destinoLateral(BORDES, 100, MAXIMO, 64, 'anteriores')).toBe(0);
  });

  it('en los extremos, o si todo cabe, no hay adónde ir', () => {
    expect(destinoLateral(BORDES, 0, MAXIMO, 64, 'anteriores')).toBeNull();
    expect(destinoLateral(BORDES, MAXIMO, MAXIMO, 64, 'siguientes')).toBeNull();
    expect(destinoLateral(BORDES, 0, 0, 64, 'siguientes')).toBeNull();
  });

  it('se salta las paradas a menos de 1 px, como las de medio píxel con zoom', () => {
    expect(destinoLateral(BORDES, 149.6, MAXIMO, 64, 'siguientes')).toBe(MAXIMO);
  });
});

describe('ajusteParaVer', () => {
  // 600 px a la vista y 64 px de margen a cada lado para las flechas
  it('no mueve nada si la columna ya se ve entera', () => {
    expect(ajusteParaVer(100, 300, 600, 64)).toBe(0);
  });

  it('a medio asomar por la derecha, la trae hasta que se ve entera', () => {
    expect(ajusteParaVer(450, 650, 600, 64)).toBe(114);
  });

  it('tapada por la izquierda, vuelve hacia atrás', () => {
    expect(ajusteParaVer(-120, 80, 600, 64)).toBe(-184);
  });

  it('si es más ancha que lo visible, deja a la vista su inicio', () => {
    expect(ajusteParaVer(300, 1000, 600, 64)).toBe(236);
  });
});
