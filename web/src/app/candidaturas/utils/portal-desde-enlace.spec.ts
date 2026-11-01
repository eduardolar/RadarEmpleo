import { portalDesdeEnlace } from './portal-desde-enlace';

describe('portalDesdeEnlace', () => {
  it('reconoce los portales por el dominio, con o sin www y subdominio del país', () => {
    expect(portalDesdeEnlace('https://www.infojobs.net/madrid/dev/of-i123')).toBe('InfoJobs');
    expect(portalDesdeEnlace('https://es.linkedin.com/jobs/view/123')).toBe('LinkedIn');
    expect(portalDesdeEnlace('https://es.indeed.com/viewjob?jk=1')).toBe('Indeed');
    expect(portalDesdeEnlace('  https://www.tecnoempleo.com/oferta  ')).toBe('Tecnoempleo');
  });

  it('no adivina con webs de empresas, dominios parecidos o textos que no son enlaces', () => {
    expect(portalDesdeEnlace('https://careers.acme.com/jobs/1')).toBeNull();
    expect(portalDesdeEnlace('https://falsolinkedin.com/jobs')).toBeNull();
    expect(portalDesdeEnlace('linkedin')).toBeNull();
    expect(portalDesdeEnlace('')).toBeNull();
    expect(portalDesdeEnlace(null)).toBeNull();
  });
});
