import { crearDocumento } from './documento-de-prueba';
import { documentoVigente, otrosDocumentos, pestanasConDocumento } from './documento-vigente';

describe('documentoVigente', () => {
  it('devuelve el marcado como vigente de ese tipo', () => {
    const viejo = crearDocumento({ id: 1, vigente: false, fechaAlta: '2026-11-07T10:00:00Z' });
    const vigente = crearDocumento({ id: 2, vigente: true, fechaAlta: '2026-11-01T10:00:00Z' });
    const carta = crearDocumento({ id: 3, tipo: 'Carta', vigente: true });

    expect(documentoVigente([viejo, vigente, carta], 'CV')).toBe(vigente);
    expect(documentoVigente([viejo, vigente, carta], 'Carta')).toBe(carta);
  });

  it('sin ninguno marcado, el más reciente', () => {
    const viejo = crearDocumento({ id: 1, vigente: false, fechaAlta: '2026-11-01T10:00:00Z' });
    const nuevo = crearDocumento({ id: 2, vigente: false, fechaAlta: '2026-11-05T10:00:00Z' });

    expect(documentoVigente([viejo, nuevo], 'CV')).toBe(nuevo);
  });

  it('sin documentos de ese tipo, null', () => {
    expect(documentoVigente([crearDocumento()], 'Carta')).toBeNull();
    expect(documentoVigente([], 'CV')).toBeNull();
  });
});

describe('pestanasConDocumento', () => {
  it('solo las que tienen documento y siempre CV antes que carta', () => {
    const carta = crearDocumento({ id: 1, tipo: 'Carta' });
    const cv = crearDocumento({ id: 2, tipo: 'CV' });

    expect(pestanasConDocumento([carta, cv])).toEqual(['CV', 'Carta']);
    expect(pestanasConDocumento([carta])).toEqual(['Carta']);
    expect(pestanasConDocumento([crearDocumento({ tipo: 'Otro' })])).toEqual([]);
  });
});

describe('otrosDocumentos', () => {
  it('todo menos el CV y la carta vigentes', () => {
    const cv = crearDocumento({ id: 1, vigente: true });
    const cvAnterior = crearDocumento({ id: 2, vigente: false });
    const carta = crearDocumento({ id: 3, tipo: 'Carta', vigente: true });
    const dosier = crearDocumento({ id: 4, tipo: 'Otro', vigente: true });

    expect(otrosDocumentos([cv, cvAnterior, carta, dosier])).toEqual([cvAnterior, dosier]);
  });

  it('sin documentos, lista vacía', () => {
    expect(otrosDocumentos([])).toEqual([]);
  });
});
