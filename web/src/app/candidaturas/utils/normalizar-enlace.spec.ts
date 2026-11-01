import { FormControl } from '@angular/forms';
import { enlaceWeb, normalizarEnlace } from './normalizar-enlace';

describe('normalizarEnlace', () => {
  it('deja como está lo que ya es una dirección web', () => {
    expect(normalizarEnlace('https://www.linkedin.com/jobs/1')).toBe(
      'https://www.linkedin.com/jobs/1',
    );
    expect(normalizarEnlace('http://acme.com')).toBe('http://acme.com');
    expect(normalizarEnlace('HTTPS://ACME.COM/x')).toBe('HTTPS://ACME.COM/x');
    expect(normalizarEnlace('https://intranet/empleo')).toBe('https://intranet/empleo');
  });

  it('quita los espacios de los extremos', () => {
    expect(normalizarEnlace('  https://acme.com  ')).toBe('https://acme.com');
  });

  it('pone https:// a lo que no trae esquema', () => {
    expect(normalizarEnlace('www.linkedin.com/jobs/1')).toBe('https://www.linkedin.com/jobs/1');
    expect(normalizarEnlace('acme.com')).toBe('https://acme.com');
    expect(normalizarEnlace('acme.com:8080/empleo')).toBe('https://acme.com:8080/empleo');
    expect(normalizarEnlace('//acme.com/empleo')).toBe('https://acme.com/empleo');
  });

  it('un campo vacío no es un error', () => {
    expect(normalizarEnlace('')).toBe('');
    expect(normalizarEnlace('   ')).toBe('');
    expect(normalizarEnlace(null)).toBe('');
    expect(normalizarEnlace(undefined)).toBe('');
  });

  it('rechaza otros esquemas', () => {
    expect(normalizarEnlace('mailto:rrhh@acme.com')).toBeNull();
    expect(normalizarEnlace('javascript:alert(1)')).toBeNull();
    expect(normalizarEnlace('ftp://acme.com/oferta')).toBeNull();
    expect(normalizarEnlace('tel:600123123')).toBeNull();
  });

  it('rechaza lo que no es una dirección', () => {
    expect(normalizarEnlace('no es un enlace')).toBeNull();
    expect(normalizarEnlace('oferta')).toBeNull();
    expect(normalizarEnlace('https://')).toBeNull();
  });
});

describe('enlaceWeb', () => {
  it('da error solo si lo escrito no se puede convertir en una dirección web', () => {
    expect(enlaceWeb(new FormControl('mailto:a@b.com'))).toEqual({ enlaceWeb: true });
    expect(enlaceWeb(new FormControl('www.acme.com'))).toBeNull();
    expect(enlaceWeb(new FormControl(''))).toBeNull();
  });
});
