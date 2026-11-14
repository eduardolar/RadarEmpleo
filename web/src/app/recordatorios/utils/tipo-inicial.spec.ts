import { tipoInicialSegunEstado } from './tipo-inicial';

describe('tipoInicialSegunEstado', () => {
  it('en Entrevista empieza como entrevista y en Oferta, como plazo', () => {
    expect(tipoInicialSegunEstado('Entrevista')).toBe('Entrevista');
    expect(tipoInicialSegunEstado('Oferta')).toBe('Plazo');
  });

  it.each(['Detectada', 'Preparada', 'Enviada'] as const)('en %s, como seguimiento', (estado) => {
    expect(tipoInicialSegunEstado(estado)).toBe('Seguimiento');
  });
});
