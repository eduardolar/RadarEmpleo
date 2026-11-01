import { puestoDesdeEnlace } from './puesto-desde-enlace';

describe('puestoDesdeEnlace', () => {
  it('saca el título de un enlace de InfoJobs', () => {
    expect(
      puestoDesdeEnlace('https://www.infojobs.net/madrid/desarrollador-frontend/of-i12ab'),
    ).toBe('Desarrollador frontend');
  });

  it('quita la coletilla «h-m-x» del final', () => {
    expect(
      puestoDesdeEnlace('https://www.infojobs.net/madrid/programador-junior-h-m-x/of-i12ab'),
    ).toBe('Programador junior');
  });

  it.each(['h-m', 'h-m-x', 'm-f', 'f-m', 'm-h', 'm-f-d'])(
    'quita la coletilla «-%s»',
    (coletilla) => {
      expect(
        puestoDesdeEnlace(
          `https://www.infojobs.net/madrid/programador-junior-${coletilla}/of-i12ab`,
        ),
      ).toBe('Programador junior');
    },
  );

  it('no confunde con una coletilla el final normal de un título', () => {
    expect(puestoDesdeEnlace('https://www.infojobs.net/madrid/programador-net-m/of-i12ab')).toBe(
      'Programador net m',
    );
  });

  it('decodifica las letras con tilde del enlace', () => {
    expect(puestoDesdeEnlace('https://www.infojobs.net/madrid/Dise%C3%B1ador-web/of-i12ab')).toBe(
      'Diseñador web',
    );
  });

  it('si el enlace trae un % roto, no falla y deja el texto como venía', () => {
    expect(puestoDesdeEnlace('https://www.infojobs.net/madrid/dise%E0%A4%A-web/of-i12ab')).toBe(
      'Dise%E0%A4%A web',
    );
  });

  it('saca el título de un enlace de Tecnoempleo', () => {
    expect(
      puestoDesdeEnlace('https://www.tecnoempleo.com/programador-angular-acme/angular/rf-12ab'),
    ).toBe('Programador angular acme');
  });

  it('no adivina si el enlace no trae el título', () => {
    expect(puestoDesdeEnlace('https://www.linkedin.com/jobs/view/4312345678')).toBeNull();
    expect(puestoDesdeEnlace('https://www.infojobs.net/madrid/x/of-i12ab')).toBeNull();
    expect(puestoDesdeEnlace('https://www.infojobs.net/empresa/acme')).toBeNull();
    expect(puestoDesdeEnlace('https://careers.acme.com/madrid/dev/of-i12ab')).toBeNull();
    expect(puestoDesdeEnlace('no es un enlace')).toBeNull();
    expect(puestoDesdeEnlace(null)).toBeNull();
  });
});
