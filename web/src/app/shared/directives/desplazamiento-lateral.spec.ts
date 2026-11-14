import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DesplazamientoLateral } from './desplazamiento-lateral';

@Component({
  imports: [DesplazamientoLateral],
  template: `
    <div appDesplazamientoLateral data-carril>
      @for (columna of columnas(); track columna) {
        <section [attr.data-columna]="columna">{{ columna }}</section>
      }
    </div>
  `,
})
class Anfitrion {
  readonly columnas = signal(['Detectada', 'Preparada', 'Enviada']);
}

describe('DesplazamientoLateral', () => {
  // jsdom no trae ResizeObserver: uno falso que apunta lo que se observa
  let observados: Set<Element>;
  let desconectados: number;

  beforeEach(() => {
    observados = new Set();
    desconectados = 0;
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe(elemento: Element): void {
          observados.add(elemento);
        }
        unobserve(elemento: Element): void {
          observados.delete(elemento);
        }
        disconnect(): void {
          desconectados++;
          observados.clear();
        }
      },
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  async function crear(): Promise<ComponentFixture<Anfitrion>> {
    const fixture = TestBed.createComponent(Anfitrion);
    await fixture.whenStable();
    return fixture;
  }

  function carril(fixture: ComponentFixture<Anfitrion>): HTMLElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[data-carril]')!;
  }

  function columna(fixture: ComponentFixture<Anfitrion>, nombre: string): Element {
    return carril(fixture).querySelector(`[data-columna="${nombre}"]`)!;
  }

  /** El MutationObserver avisa en una microtarea */
  async function esperarMutaciones(fixture: ComponentFixture<Anfitrion>): Promise<void> {
    await fixture.whenStable();
    await Promise.resolve();
  }

  it('observa el carril y cada columna', async () => {
    const fixture = await crear();

    expect(observados.has(carril(fixture))).toBe(true);
    expect(observados.size).toBe(4);
  });

  it('deja de observar las columnas que se quitan y observa las que llegan', async () => {
    const fixture = await crear();
    const quitada = columna(fixture, 'Preparada');

    fixture.componentInstance.columnas.set(['Detectada', 'Enviada', 'Entrevista']);
    await esperarMutaciones(fixture);

    expect(observados.has(quitada)).toBe(false);
    expect(observados.has(columna(fixture, 'Entrevista'))).toBe(true);
    expect(observados.size).toBe(4);
  });

  it('al destruirse desconecta los dos observadores y quita sus listeners', async () => {
    const fixture = await crear();
    const elemento = carril(fixture);
    const quitar = vi.spyOn(elemento, 'removeEventListener');
    const desconectarMutaciones = vi.spyOn(MutationObserver.prototype, 'disconnect');

    fixture.destroy();

    expect(desconectados).toBe(1);
    expect(desconectarMutaciones).toHaveBeenCalled();
    expect(quitar.mock.calls.map(([tipo]) => tipo)).toEqual(
      expect.arrayContaining(['scroll', 'scrollend', 'focusin']),
    );
  });
});
