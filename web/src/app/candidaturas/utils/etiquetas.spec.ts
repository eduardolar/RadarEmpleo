import {
  ETIQUETAS_ENCAJE,
  ETIQUETAS_MODALIDAD,
  ETIQUETAS_MOTIVO_DESCARTE,
  opciones,
  textoAccionEstado,
} from './etiquetas';

describe('etiquetas', () => {
  it('escribe los valores de la API en español, con tildes y espacios', () => {
    expect(ETIQUETAS_MODALIDAD.Hibrido).toBe('Híbrido');
    expect(ETIQUETAS_MODALIDAD.NoIndicada).toBe('Sin indicar');
    expect(ETIQUETAS_ENCAJE.TiroLargo).toBe('Tiro largo');
    expect(ETIQUETAS_MOTIVO_DESCARTE.Ingles).toBe('Inglés');
    expect(ETIQUETAS_MOTIVO_DESCARTE.DecisionPropia).toBe('Decisión propia');
  });

  it('convierte las etiquetas en opciones para un desplegable, en el mismo orden', () => {
    expect(opciones(ETIQUETAS_MODALIDAD)).toEqual([
      { valor: 'NoIndicada', etiqueta: 'Sin indicar' },
      { valor: 'Remoto', etiqueta: 'Remoto' },
      { valor: 'Hibrido', etiqueta: 'Híbrido' },
      { valor: 'Presencial', etiqueta: 'Presencial' },
    ]);
  });
});

describe('textoAccionEstado', () => {
  it('da el texto de cada botón de cambio de estado', () => {
    expect(textoAccionEstado('Preparada', 'Detectada')).toBe('Marcar preparada');
    expect(textoAccionEstado('Enviada', 'Preparada')).toBe('Marcar enviada');
    expect(textoAccionEstado('Descartada', 'Enviada')).toBe('Descartar');
    expect(textoAccionEstado('Cerrada', 'Oferta')).toBe('Cerrar candidatura');
    expect(textoAccionEstado('Entrevista', 'Enviada')).toBe('Pasar a Entrevista');
  });

  it('llama «Reabrir» a volver a Detectada desde un estado final', () => {
    expect(textoAccionEstado('Detectada', 'Descartada')).toBe('Reabrir');
    expect(textoAccionEstado('Detectada', 'Cerrada')).toBe('Reabrir');
  });
});
