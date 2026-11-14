import { tituloAccesible, tituloVisible } from './titulo-visible';

describe('tituloVisible', () => {
  const CANDIDATURA = { empresa: 'Consultora Ejemplo', puesto: 'Desarrollador/a .NET (ERP)' };

  it('el seguimiento que programa la API se queda en el puesto', () => {
    const titulo = 'Seguimiento: Desarrollador/a .NET (ERP) en Consultora Ejemplo';

    expect(tituloVisible({ tipo: 'Seguimiento', titulo }, CANDIDATURA)).toBe(
      'Desarrollador/a .NET (ERP)',
    );
  });

  it.each([
    ['Seguimiento', 'Seguimiento: Desarrollador/a .NET (ERP)'],
    ['Seguimiento', 'Seguimiento de Consultora Ejemplo'],
    ['Seguimiento', 'Seguimiento'],
    ['Seguimiento', 'Preguntar cómo va Consultora Ejemplo'],
    ['Seguimiento', 'Preguntar cómo va'],
    ['Entrevista', 'Entrevista en Consultora Ejemplo'],
    ['Entrevista', 'Entrevista'],
    ['Plazo', 'Plazo de Consultora Ejemplo'],
    ['Plazo', 'Plazo'],
  ] as const)('%s con el título por defecto «%s» enseña el puesto', (tipo, titulo) => {
    expect(tituloVisible({ tipo, titulo }, CANDIDATURA)).toBe('Desarrollador/a .NET (ERP)');
  });

  it('no mira mayúsculas ni espacios de más', () => {
    const titulo = '  entrevista  en consultora ejemplo ';

    expect(tituloVisible({ tipo: 'Entrevista', titulo }, CANDIDATURA)).toBe(
      'Desarrollador/a .NET (ERP)',
    );
  });

  it.each([
    ['Plazo', 'Plazo de la prueba técnica'],
    ['Seguimiento', 'Seguimiento: llamar a Marta'],
    ['Entrevista', 'Entrevista técnica con el equipo'],
    // El título por defecto de otro tipo no es el de este
    ['Plazo', 'Entrevista en Consultora Ejemplo'],
    // Una publicación o un «Otro» dicen qué hay que hacer: el puesto no lo sustituye
    ['PublicacionLinkedIn', 'Publicar en LinkedIn'],
    ['Otro', 'Plazo'],
  ] as const)('%s con un título propio («%s») lo enseña tal cual', (tipo, titulo) => {
    expect(tituloVisible({ tipo, titulo }, CANDIDATURA)).toBe(titulo);
  });

  it('sin candidatura o sin puesto, el título guardado', () => {
    expect(tituloVisible({ tipo: 'Entrevista', titulo: 'Entrevista' }, null)).toBe('Entrevista');
    expect(
      tituloVisible({ tipo: 'Entrevista', titulo: 'Entrevista' }, { empresa: 'Acme', puesto: ' ' }),
    ).toBe('Entrevista');
  });
});

describe('tituloAccesible', () => {
  const CANDIDATURA = { empresa: 'Consultora Ejemplo', puesto: 'Desarrollador .NET' };

  it('es el título guardado, entero, si ya lleva dentro lo que se ve', () => {
    const titulo = 'Seguimiento: Desarrollador .NET en Consultora Ejemplo';

    expect(tituloAccesible({ tipo: 'Seguimiento', titulo }, CANDIDATURA)).toBe(titulo);
    expect(tituloAccesible({ tipo: 'Plazo', titulo: 'Plazo de la prueba' }, CANDIDATURA)).toBe(
      'Plazo de la prueba',
    );
    expect(tituloAccesible({ tipo: 'Otro', titulo: 'Renovar la demanda' }, null)).toBe(
      'Renovar la demanda',
    );
  });

  it('si se ve el puesto y el título no lo lleva, lo añade', () => {
    const titulo = 'Entrevista en Consultora Ejemplo';

    expect(tituloAccesible({ tipo: 'Entrevista', titulo }, CANDIDATURA)).toBe(
      'Entrevista en Consultora Ejemplo, Desarrollador .NET',
    );
  });
});
