import { crearResultadoEscaneo } from './rastreo-de-prueba';
import { textoEscaneo, textoSinLeer } from './resumen-escaneo';

describe('textoEscaneo', () => {
  it('sin nada nuevo', () => {
    expect(textoEscaneo(crearResultadoEscaneo())).toBe('No hay nada nuevo.');
  });

  it('informes nuevos o cambiados sin ofertas nuevas', () => {
    expect(textoEscaneo(crearResultadoEscaneo({ rastreosActualizados: 1 }))).toBe(
      'He leído los informes: no traen ofertas nuevas.',
    );
  });

  it('una o varias ofertas nuevas', () => {
    expect(textoEscaneo(crearResultadoEscaneo({ ofertasNuevas: 1 }))).toBe(
      'He encontrado 1 oferta nueva.',
    );
    expect(textoEscaneo(crearResultadoEscaneo({ ofertasNuevas: 47 }))).toBe(
      'He encontrado 47 ofertas nuevas.',
    );
  });

  it('dice cuántas ya estaban en el tablero', () => {
    expect(textoEscaneo(crearResultadoEscaneo({ ofertasNuevas: 3, yaEnElTablero: 1 }))).toBe(
      'He encontrado 3 ofertas nuevas (1 ya estaba en el tablero).',
    );
    expect(textoEscaneo(crearResultadoEscaneo({ ofertasNuevas: 3, yaEnElTablero: 2 }))).toBe(
      'He encontrado 3 ofertas nuevas (2 ya estaban en el tablero).',
    );
  });
});

describe('textoSinLeer', () => {
  it('null si no hay', () => {
    expect(textoSinLeer(0)).toBeNull();
  });

  it('en singular y en plural', () => {
    expect(textoSinLeer(1)).toMatch(/^1 línea de los informes/);
    expect(textoSinLeer(27)).toMatch(/^27 líneas de los informes/);
  });
});
