import { EstadoCandidatura } from '../interfaces/candidatura.interface';
import { ESTADOS_CANDIDATURA, estadosPosibles, puedePasar } from './transiciones-permitidas';

// Lo que devuelve la API para cada uno de los siete estados
const POSIBLES: Record<EstadoCandidatura, EstadoCandidatura[]> = {
  Detectada: ['Preparada', 'Enviada', 'Entrevista', 'Oferta', 'Descartada', 'Cerrada'],
  Preparada: ['Enviada', 'Entrevista', 'Oferta', 'Descartada', 'Cerrada'],
  Enviada: ['Entrevista', 'Oferta', 'Descartada', 'Cerrada'],
  Entrevista: ['Oferta', 'Descartada', 'Cerrada'],
  Oferta: ['Descartada', 'Cerrada'],
  Descartada: ['Detectada', 'Cerrada'],
  Cerrada: ['Detectada', 'Descartada'],
};

describe('puedePasar', () => {
  it('deja avanzar en el camino normal, también saltando pasos', () => {
    expect(puedePasar('Detectada', 'Preparada')).toBe(true);
    expect(puedePasar('Detectada', 'Enviada')).toBe(true);
    expect(puedePasar('Enviada', 'Oferta')).toBe(true);
  });

  it('no deja retroceder ni quedarse en el mismo estado', () => {
    expect(puedePasar('Enviada', 'Preparada')).toBe(false);
    expect(puedePasar('Oferta', 'Detectada')).toBe(false);
    expect(puedePasar('Enviada', 'Enviada')).toBe(false);
  });

  it('deja descartar o cerrar desde cualquier otro estado', () => {
    expect(puedePasar('Detectada', 'Descartada')).toBe(true);
    expect(puedePasar('Oferta', 'Cerrada')).toBe(true);
    expect(puedePasar('Cerrada', 'Descartada')).toBe(true);
    expect(puedePasar('Descartada', 'Cerrada')).toBe(true);
  });

  it('desde un estado final solo deja reabrir a Detectada', () => {
    expect(puedePasar('Descartada', 'Detectada')).toBe(true);
    expect(puedePasar('Descartada', 'Enviada')).toBe(false);
    expect(puedePasar('Cerrada', 'Preparada')).toBe(false);
  });
});

describe('estadosPosibles', () => {
  it('la tabla de esta prueba tiene los siete estados', () => {
    expect(Object.keys(POSIBLES).sort()).toEqual([...ESTADOS_CANDIDATURA].sort());
    expect(ESTADOS_CANDIDATURA).toHaveLength(7);
  });

  it.each(ESTADOS_CANDIDATURA)('desde %s coincide con lo que devuelve la API', (estado) => {
    expect(estadosPosibles(estado)).toEqual(POSIBLES[estado]);
  });

  it.each(ESTADOS_CANDIDATURA)('desde %s, puedePasar dice no a todo lo demás', (estado) => {
    const prohibidos = ESTADOS_CANDIDATURA.filter((a) => !POSIBLES[estado].includes(a));

    expect(prohibidos.filter((a) => puedePasar(estado, a))).toEqual([]);
  });
});
