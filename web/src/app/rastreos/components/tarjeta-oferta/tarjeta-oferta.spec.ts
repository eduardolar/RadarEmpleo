import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OfertaRastreo } from '../../interfaces/rastreo.interface';
import { crearOferta } from '../../utils/rastreo-de-prueba';
import { TarjetaOferta } from './tarjeta-oferta';

describe('TarjetaOferta', () => {
  let fixture: ComponentFixture<TarjetaOferta>;

  async function crear(oferta: OfertaRastreo, ocupada = false): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [TarjetaOferta],
      providers: [provideRouter([])],
    });
    fixture = TestBed.createComponent(TarjetaOferta);
    fixture.componentRef.setInput('oferta', oferta);
    fixture.componentRef.setInput('ocupada', ocupada);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function boton(elemento: HTMLElement, texto: string): HTMLButtonElement | undefined {
    return [...elemento.querySelectorAll('button')].find((b) => b.textContent?.includes(texto));
  }

  it('pinta empresa, puesto, encaje, pega y el enlace en otra pestaña', async () => {
    const elemento = await crear(
      crearOferta({
        empresa: 'Acme',
        puesto: 'Backend .NET',
        encaje: 'Medio',
        pega: 'Pide 2 años.',
      }),
    );

    expect(elemento.textContent).toContain('Acme');
    expect(elemento.querySelector('h3')?.textContent).toContain('Backend .NET');
    expect(elemento.textContent).toContain('Encaje: Medio');
    expect(elemento.textContent).toContain('Pide 2 años.');
    const enlace = elemento.querySelector<HTMLAnchorElement>('a[target="_blank"]')!;
    expect(enlace.getAttribute('href')).toBe('https://ejemplo.com/ofertas/1');
    expect(enlace.getAttribute('rel')).toBe('noopener noreferrer');
    expect(enlace.textContent).toContain('Ver la oferta');
  });

  it('pendiente: «Pasar al tablero» y «No me interesa» avisan con la oferta', async () => {
    const oferta = crearOferta();
    const elemento = await crear(oferta);
    const detectadas: OfertaRastreo[] = [];
    const ignoradas: OfertaRastreo[] = [];
    fixture.componentInstance.detectar.subscribe((o) => detectadas.push(o));
    fixture.componentInstance.ignorar.subscribe((o) => ignoradas.push(o));

    boton(elemento, 'Pasar al tablero')!.click();
    boton(elemento, 'No me interesa')!.click();

    expect(detectadas).toEqual([oferta]);
    expect(ignoradas).toEqual([oferta]);
    expect(boton(elemento, 'Pasar al tablero')!.getAttribute('aria-label')).toBe(
      'Pasar al tablero: Desarrollador Angular',
    );
  });

  it('mientras responde, un clic no hace nada', async () => {
    const elemento = await crear(crearOferta(), true);
    const detectadas: OfertaRastreo[] = [];
    fixture.componentInstance.detectar.subscribe((o) => detectadas.push(o));

    boton(elemento, 'Pasar al tablero')!.click();

    expect(detectadas).toEqual([]);
  });

  it('«Pasar al tablero» va con relleno solo si es la destacada; si no, con borde', async () => {
    const elemento = await crear(crearOferta());
    expect(boton(elemento, 'Pasar al tablero')!.classList).toContain('mat-mdc-unelevated-button');

    fixture.componentRef.setInput('destacada', false);
    await fixture.whenStable();

    expect(boton(elemento, 'Pasar al tablero')!.classList).toContain('mat-mdc-outlined-button');
    expect(boton(elemento, 'Pasar al tablero')!.classList).not.toContain(
      'mat-mdc-unelevated-button',
    );
  });

  it('ignorada: solo se puede pasar al tablero', async () => {
    const elemento = await crear(crearOferta({ estado: 'Ignorada' }));

    expect(boton(elemento, 'Pasar al tablero')).toBeDefined();
    expect(boton(elemento, 'No me interesa')).toBeUndefined();
  });

  it('en el tablero: lo dice y lleva a su ficha', async () => {
    const elemento = await crear(crearOferta({ estado: 'Convertida', candidaturaId: 33 }));

    expect(elemento.textContent).toContain('En el tablero');
    expect(elemento.querySelector('a[href="/candidaturas/33"]')?.textContent).toContain(
      'Abrir ficha',
    );
    expect(elemento.querySelectorAll('button')).toHaveLength(0);
  });

  it('pasándola al tablero todavía no hay ficha a la que ir', async () => {
    const elemento = await crear(crearOferta({ estado: 'Convertida' }), true);

    expect(elemento.textContent).toContain('Pasando al tablero...');
    expect(elemento.querySelector('a[href^="/candidaturas"]')).toBeNull();
  });

  it('con candidaturas de la misma empresa avisa encima de los botones y enlaza a sus fichas', async () => {
    TestBed.configureTestingModule({ imports: [TarjetaOferta], providers: [provideRouter([])] });
    fixture = TestBed.createComponent(TarjetaOferta);
    fixture.componentRef.setInput('oferta', crearOferta({ empresa: 'Acme' }));
    fixture.componentRef.setInput(
      'parecidas',
      [1, 2, 3, 4].map((id) => ({
        id,
        empresa: 'Acme S.L.',
        puesto: `Puesto ${id}`,
        estado: 'Enviada' as const,
      })),
    );
    await fixture.whenStable();
    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.textContent).toContain('Puede que ya la tengas.');
    expect(elemento.textContent).toContain('candidaturas');
    expect(elemento.querySelectorAll('a[href^="/candidaturas/"]')).toHaveLength(3);
    expect(elemento.textContent).toContain('Y 1 más en el tablero.');
    expect(boton(elemento, 'Pasar al tablero')).toBeDefined();
  });

  it('ya en el tablero no avisa de repetidas', async () => {
    TestBed.configureTestingModule({ imports: [TarjetaOferta], providers: [provideRouter([])] });
    fixture = TestBed.createComponent(TarjetaOferta);
    fixture.componentRef.setInput(
      'oferta',
      crearOferta({ estado: 'Convertida', candidaturaId: 3 }),
    );
    fixture.componentRef.setInput('parecidas', [
      { id: 9, empresa: 'Acme', puesto: 'Otra', estado: 'Detectada' },
    ]);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain(
      'Puede que ya la tengas',
    );
  });
});
