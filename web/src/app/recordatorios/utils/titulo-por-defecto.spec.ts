import { tituloPorDefecto } from './titulo-por-defecto';

describe('tituloPorDefecto', () => {
  const CANDIDATURA = { empresa: 'Consultora Ejemplo', puesto: 'Desarrollador .NET' };

  it('con la candidatura, nombra la empresa', () => {
    expect(tituloPorDefecto('Entrevista', CANDIDATURA)).toBe('Entrevista en Consultora Ejemplo');
    expect(tituloPorDefecto('Plazo', CANDIDATURA)).toBe('Plazo de Consultora Ejemplo');
  });

  it('el seguimiento es el mismo que programa la API: con el puesto y la empresa', () => {
    expect(tituloPorDefecto('Seguimiento', CANDIDATURA)).toBe(
      'Seguimiento: Desarrollador .NET en Consultora Ejemplo',
    );
    expect(tituloPorDefecto('Seguimiento', { empresa: ' ', puesto: 'Desarrollador .NET' })).toBe(
      'Seguimiento: Desarrollador .NET',
    );
    expect(tituloPorDefecto('Seguimiento', { empresa: 'Acme', puesto: '' })).toBe(
      'Seguimiento de Acme',
    );
  });

  it('sin candidatura, se queda en lo que es', () => {
    expect(tituloPorDefecto('Entrevista')).toBe('Entrevista');
    expect(tituloPorDefecto('Seguimiento', null)).toBe('Seguimiento');
    expect(tituloPorDefecto('Plazo', { empresa: '  ', puesto: '' })).toBe('Plazo');
  });

  it('una publicación no depende de la candidatura', () => {
    expect(tituloPorDefecto('PublicacionLinkedIn')).toBe('Publicar en LinkedIn');
    expect(tituloPorDefecto('PublicacionLinkedIn', CANDIDATURA)).toBe('Publicar en LinkedIn');
  });

  it('«Otro» empieza vacío', () => {
    expect(tituloPorDefecto('Otro')).toBe('');
    expect(tituloPorDefecto('Otro', CANDIDATURA)).toBe('');
  });
});
