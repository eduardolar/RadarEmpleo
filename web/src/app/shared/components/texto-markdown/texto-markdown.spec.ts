import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TextoMarkdown } from './texto-markdown';

describe('TextoMarkdown', () => {
  let fixture: ComponentFixture<TextoMarkdown>;

  function pintar(markdown: string | null): HTMLElement {
    fixture = TestBed.createComponent(TextoMarkdown);
    fixture.componentRef.setInput('markdown', markdown);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('pinta el Markdown como HTML', () => {
    const elemento = pintar('- **Acme — Backend** remoto');

    expect(elemento.querySelector('li strong')?.textContent).toBe('Acme — Backend');
  });

  it('el saneado de Angular deja target y rel en los enlaces', () => {
    const enlace = pintar('Mira https://ejemplo.com/o/1').querySelector('a')!;

    expect(enlace.getAttribute('href')).toBe('https://ejemplo.com/o/1');
    expect(enlace.getAttribute('target')).toBe('_blank');
    expect(enlace.getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('quita lo que no es seguro', () => {
    const elemento = pintar(
      '<script>alert(1)</script><img src="x" onerror="alert(2)"> [x](javascript:alert(3))',
    );

    expect(elemento.querySelector('script')).toBeNull();
    expect(elemento.querySelector('img')?.getAttribute('onerror')).toBeNull();
    expect(elemento.querySelector('a')?.getAttribute('href') ?? '').not.toMatch(/^javascript:/);
  });

  it('sin texto no pinta nada', () => {
    expect(pintar(null).textContent?.trim()).toBe('');
  });
});
