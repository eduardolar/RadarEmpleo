import { markdownAHtml } from './markdown';

describe('markdownAHtml', () => {
  it('convierte listas, negritas y párrafos', () => {
    const html = markdownAHtml('Semana mala.\n\n- **Acme — Backend** remoto\n- Otra');

    expect(html).toContain('<p>Semana mala.</p>');
    expect(html).toContain('<li><strong>Acme — Backend</strong> remoto</li>');
    expect(html).toContain('<ul>');
  });

  it('los enlaces se abren en otra pestaña', () => {
    const html = markdownAHtml('[Ver la oferta](https://ejemplo.com/o/1 "La oferta")');

    expect(html).toBe(
      '<p><a href="https://ejemplo.com/o/1" title="La oferta" target="_blank" rel="noopener noreferrer">Ver la oferta</a></p>\n',
    );
  });

  it('las URL sueltas del informe también son enlaces', () => {
    const html = markdownAHtml('Pega: sin salario. https://ejemplo.com/o/2 — CV de operario');

    expect(html).toContain(
      '<a href="https://ejemplo.com/o/2" target="_blank" rel="noopener noreferrer">https://ejemplo.com/o/2</a>',
    );
  });

  it('codifica las tildes de la URL sin volver a codificar lo ya codificado', () => {
    const html = markdownAHtml('https://ejemplo.com/técnico-%C3%A1/x');

    expect(html).toContain('href="https://ejemplo.com/t%C3%A9cnico-%C3%A1/x"');
  });

  it('escapa las comillas del título para no salirse del atributo', () => {
    const html = markdownAHtml('[x](https://ejemplo.com "a" onclick="b")');

    expect(html).not.toContain('onclick="b"');
  });

  it('sin texto devuelve una cadena vacía', () => {
    expect(markdownAHtml('')).toBe('');
    expect(markdownAHtml('   ')).toBe('');
    expect(markdownAHtml(null)).toBe('');
    expect(markdownAHtml(undefined)).toBe('');
  });
});
