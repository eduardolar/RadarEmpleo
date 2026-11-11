import { DomSanitizer } from '@angular/platform-browser';
import { urlArchivo, urlVisor, urlVisorSegura } from './url-archivo';

describe('urlArchivo', () => {
  it('es relativa y lleva el id', () => {
    expect(urlArchivo(5)).toBe('/api/documentos/5/archivo');
  });

  it('el visor añade el ajuste al ancho', () => {
    expect(urlVisor(12)).toBe('/api/documentos/12/archivo#view=FitH');
  });

  it.each([0, -1, 1.5, Number.NaN, Number.POSITIVE_INFINITY, 2 ** 60])(
    'rechaza un id que no es un entero positivo (%s)',
    (id) => {
      expect(() => urlArchivo(id)).toThrow();
      expect(() => urlVisor(id)).toThrow();
    },
  );

  it('rechaza un id que en realidad es un texto', () => {
    expect(() => urlArchivo('1/../../perfil' as unknown as number)).toThrow();
  });
});

describe('urlVisorSegura', () => {
  it('marca como segura solo la URL del visor', () => {
    const sanitizer = { bypassSecurityTrustResourceUrl: vi.fn((url: string) => ({ url })) };

    const segura = urlVisorSegura(sanitizer as unknown as DomSanitizer, 7);

    expect(sanitizer.bypassSecurityTrustResourceUrl).toHaveBeenCalledWith(
      '/api/documentos/7/archivo#view=FitH',
    );
    expect(segura).toEqual({ url: '/api/documentos/7/archivo#view=FitH' });
  });

  it('con un id no válido no llega a marcar nada', () => {
    const sanitizer = { bypassSecurityTrustResourceUrl: vi.fn() };

    expect(() => urlVisorSegura(sanitizer as unknown as DomSanitizer, -3)).toThrow();
    expect(sanitizer.bypassSecurityTrustResourceUrl).not.toHaveBeenCalled();
  });
});
