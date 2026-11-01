import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CandidaturaResumen } from '../../interfaces/candidatura.interface';
import { crearResumen } from '../../utils/candidatura-de-prueba';
import { TarjetaCandidatura } from './tarjeta-candidatura';

describe('TarjetaCandidatura', () => {
  let fixture: ComponentFixture<TarjetaCandidatura>;

  async function crear(candidatura: CandidaturaResumen): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [TarjetaCandidatura],
      providers: [provideRouter([])],
    });
    fixture = TestBed.createComponent(TarjetaCandidatura);
    fixture.componentRef.setInput('candidatura', candidatura);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('cuenta los días en el estado desde fechaEstado, no desde la última modificación', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-30T10:00:00Z'));

    const elemento = await crear(
      crearResumen({
        fechaEstado: '2026-10-25T10:00:00Z',
        // Editada hoy: no debe poner el contador a cero
        fechaUltimoCambio: '2026-10-30T09:00:00Z',
      }),
    );

    expect(elemento.textContent).toContain('5 días');
    expect(elemento.textContent).not.toContain('Hoy');
  });

  it('al volver a la pestaña al día siguiente, «Hoy» pasa a ser «1 día»', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-30T22:50:00Z'));
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    const elemento = await crear(crearResumen({ fechaEstado: '2026-10-30T10:00:00Z' }));
    expect(elemento.textContent).toContain('Hoy');

    // En Madrid ya es 31: son las 00:10
    vi.setSystemTime(new Date('2026-10-30T23:10:00Z'));
    document.dispatchEvent(new Event('visibilitychange'));
    await fixture.whenStable();

    expect(elemento.textContent).toContain('1 día');
    expect(elemento.textContent).not.toContain('Hoy');
  });

  it('enseña el estado con su etiqueta y enlaza con la ficha', async () => {
    const elemento = await crear(crearResumen({ id: 7, estado: 'Entrevista' }));

    expect(elemento.textContent).toContain('Entrevista');
    expect(elemento.querySelector('a')?.getAttribute('href')).toBe('/candidaturas/7');
  });

  it('dice el encaje en español y sin repetirse', async () => {
    const elemento = await crear(crearResumen({ encaje: 'No' }));

    expect(elemento.textContent).toContain('No encaja');
    expect(elemento.textContent).not.toContain('Encaje:');

    fixture.componentRef.setInput('candidatura', crearResumen({ encaje: 'TiroLargo' }));
    await fixture.whenStable();

    expect(elemento.textContent).toContain('Encaje: Tiro largo');
  });
});
