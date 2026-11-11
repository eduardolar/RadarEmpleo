import { posiblesRepetidas } from './posibles-repetidas';
import { crearOferta } from './rastreo-de-prueba';

describe('posiblesRepetidas', () => {
  const ENVIADA = {
    id: 7,
    empresa: 'Acme Software, S.L.',
    puesto: 'Backend',
    estado: 'Enviada',
  } as const;
  const OTRA = { id: 8, empresa: 'Globex', puesto: 'Front', estado: 'Detectada' } as const;

  it('avisa si hay una candidatura de la misma empresa, aunque se escriba distinto', () => {
    const oferta = crearOferta({ id: 1, empresa: 'ACME SOFTWARE' });

    expect(posiblesRepetidas([oferta], [ENVIADA, OTRA]).get(1)).toEqual([ENVIADA]);
  });

  it('las de otras empresas no salen', () => {
    const oferta = crearOferta({ id: 1, empresa: 'Initech' });

    expect(posiblesRepetidas([oferta], [ENVIADA, OTRA]).has(1)).toBe(false);
  });

  it('las que ya están en el tablero no avisan, ni su propia candidatura cuenta', () => {
    const convertida = crearOferta({
      id: 1,
      empresa: 'Acme Software',
      estado: 'Convertida',
      candidaturaId: 7,
    });
    const ignorada = crearOferta({
      id: 2,
      empresa: 'Acme Software',
      estado: 'Ignorada',
      candidaturaId: 7,
    });

    const resultado = posiblesRepetidas([convertida, ignorada], [ENVIADA]);

    expect(resultado.size).toBe(0);
  });

  it('con varias de la misma empresa salen todas', () => {
    const oferta = crearOferta({ id: 1, empresa: 'Globex' });
    const segunda = { ...OTRA, id: 9, puesto: 'QA' };

    expect(posiblesRepetidas([oferta], [OTRA, segunda]).get(1)).toEqual([OTRA, segunda]);
  });
});
