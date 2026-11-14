import { TestBed } from '@angular/core/testing';
import { FilaBarra, ListaBarras } from './lista-barras';

function pintar(filas: FilaBarra[], color: 'estado' | 'enviada' | 'neutro' = 'neutro') {
  const fixture = TestBed.createComponent(ListaBarras);
  fixture.componentRef.setInput('filas', filas);
  fixture.componentRef.setInput('color', color);
  fixture.componentRef.setInput('etiqueta', 'Por estado');
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('ListaBarras', () => {
  it('es una lista con nombre en la que cada fila dice su etiqueta y su cifra', () => {
    const elemento = pintar([
      { etiqueta: 'Enviada', valor: 12 },
      { etiqueta: 'Oferta', valor: 0 },
    ]);

    const lista = elemento.querySelector('ul')!;
    expect(lista.getAttribute('aria-label')).toBe('Por estado');
    const filas = [...lista.querySelectorAll('li')].map((li) =>
      li.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(filas).toEqual(['Enviada, 12', 'Oferta, 0']);
  });

  it('la barra es decorativa y proporcional a la mayor; con 0 no hay barra', () => {
    const elemento = pintar([
      { etiqueta: 'Enviada', valor: 12 },
      { etiqueta: 'Entrevista', valor: 3 },
      { etiqueta: 'Oferta', valor: 0 },
    ]);

    const barras = [...elemento.querySelectorAll<HTMLElement>('[aria-hidden="true"]')];
    expect(barras.length).toBe(3);
    expect(barras.map((b) => b.querySelector<HTMLElement>('span')?.style.width ?? null)).toEqual([
      '100%',
      '25%',
      null,
    ]);
  });

  it('en color por estado cada fila lleva su estado, y en Enviada la lista entera', () => {
    const porEstado = pintar(
      [{ etiqueta: 'Entrevista', valor: 1, estado: 'Entrevista' }],
      'estado',
    );
    expect(porEstado.querySelector('li')!.getAttribute('data-estado')).toBe('Entrevista');

    const enviadas = pintar([{ etiqueta: 'Semana del 2 nov', valor: 1 }], 'enviada');
    expect(enviadas.querySelector('ul')!.getAttribute('data-estado')).toBe('Enviada');
    expect(enviadas.querySelector('li')!.getAttribute('data-estado')).toBeNull();
  });
});
