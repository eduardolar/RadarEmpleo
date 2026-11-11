import { normalizarEmpresa } from './nombre-empresa';

// Mismos casos que NombresEmpresaTests.cs: el front y la API tienen que dar la misma forma
describe('normalizarEmpresa', () => {
  it.each([
    ['Inditec Sistemas, S.A.', 'inditec sistemas'],
    ['INDITEC SISTEMAS SA', 'inditec sistemas'],
    ['  Inditec   Sistemas  ', 'inditec sistemas'],
    ['Acme Software S. L.', 'acme software'],
    ['Telefónica Tech S.A.U.', 'telefonica tech'],
    ['Ingeniería Ñandú Sociedad Limitada', 'ingenieria ñandu'],
  ])('«%s» se compara como «%s»', (nombre, esperado) => {
    expect(normalizarEmpresa(nombre)).toBe(esperado);
  });

  it.each([
    'Acme S.L.L.',
    'Acme S.Coop. And.',
    'Acme, Inc.',
    'Acme GmbH',
    'Acme SAS',
    'Acme ( S.L.U. )',
  ])('quita la forma jurídica de «%s»', (nombre) => {
    expect(normalizarEmpresa(nombre)).toBe('acme');
  });

  it.each(['Nasa', 'Visa', 'Grupo Sasa', 'Talleres Saltec', 'Hotel Spa', 'Mar y Sal'])(
    'no recorta «%s», que solo se parece a una forma',
    (nombre) => {
      expect(normalizarEmpresa(nombre)).toBe(nombre.toLowerCase());
    },
  );

  it('da lo mismo con el texto descompuesto (NFD) y conserva la ñ', () => {
    expect(normalizarEmpresa('Ingeniería España'.normalize('NFD'))).toBe('ingenieria españa');
  });

  it('vacío o nulo da cadena vacía', () => {
    expect(normalizarEmpresa('   ')).toBe('');
    expect(normalizarEmpresa(null)).toBe('');
  });
});
