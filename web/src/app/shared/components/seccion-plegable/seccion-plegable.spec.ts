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

  it('respeta «abierta»: plegada por defecto y abierta si se pide', async () => {
    expect((await crear(false)).querySelector('details')!.open).toBe(false);
    TestBed.resetTestingModule();
    expect((await crear(true)).querySelector('details')!.open).toBe(true);
  });
});
