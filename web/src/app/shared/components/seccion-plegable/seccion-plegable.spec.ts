import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SeccionPlegable } from './seccion-plegable';

@Component({
  imports: [SeccionPlegable],
  template: `
    <app-seccion-plegable titulo="Filtros de búsqueda" [abierta]="abierta()">
      <p>Remoto</p>
    </app-seccion-plegable>
  `,
})
class Anfitrion {
  readonly abierta = signal(false);
}

describe('SeccionPlegable', () => {
  async function crear(abierta: boolean): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(Anfitrion);
    fixture.componentInstance.abierta.set(abierta);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('el título va en el summary y el contenido dentro del details', async () => {
    const elemento = await crear(false);

    const details = elemento.querySelector('details')!;
    expect(details.querySelector('summary')?.textContent?.trim()).toBe('Filtros de búsqueda');
    expect(details.querySelector('p')?.textContent).toBe('Remoto');
  });

  it('sin nivel no añade ningún encabezado', async () => {
    const elemento = await crear(false);

    expect(elemento.querySelector('h2, h3, [role="heading"]')).toBeNull();
  });

  it('con nivel, el encabezado va oculto antes del bloque, no dentro del summary', async () => {
    const fixture = TestBed.createComponent(SeccionPlegable);
    fixture.componentRef.setInput('titulo', 'Hechos hoy · 2');
    fixture.componentRef.setInput('nivelTitulo', 2);
    await fixture.whenStable();
    const elemento = fixture.nativeElement as HTMLElement;

    const encabezado = elemento.querySelector('h2')!;
    expect(encabezado.textContent?.trim()).toBe('Hechos hoy · 2');
    expect(encabezado.classList).toContain('sr-only');
    expect(encabezado.nextElementSibling?.tagName).toBe('DETAILS');
    expect(elemento.querySelector('summary [role="heading"]')).toBeNull();
  });

  it('respeta «abierta»: plegada por defecto y abierta si se pide', async () => {
    expect((await crear(false)).querySelector('details')!.open).toBe(false);
    TestBed.resetTestingModule();
    expect((await crear(true)).querySelector('details')!.open).toBe(true);
  });
});
