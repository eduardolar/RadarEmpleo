import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Perfil } from '../../interfaces/perfil.interface';
import PerfilPageComponent from './perfil-page.component';

describe('PerfilPageComponent', () => {
  let fixture: ComponentFixture<PerfilPageComponent>;
  let http: HttpTestingController;

  const PERFIL: Perfil = {
    titulo: 'Perfil de ejemplo',
    fechaModificacion: '2026-11-06T10:00:00Z',
    introduccion: 'Ficha **del equipo**.',
    secciones: [
      { titulo: 'Experiencia', markdown: '- Acme' },
      { titulo: 'Filtros de búsqueda (confirmados)', markdown: '- Remoto' },
    ],
  };

  async function abrir(): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [PerfilPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PerfilPageComponent);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => http.verify());

  it('enseña título, fecha, introducción y secciones; solo los filtros van abiertos', async () => {
    const elemento = await abrir();
    http.expectOne('/api/perfil').flush(PERFIL);
    await fixture.whenStable();

    expect(elemento.textContent).toContain('Perfil de ejemplo');
    expect(elemento.textContent).toContain('Actualizado el 6 nov 2026');
    expect(elemento.textContent).toContain('Para cambiarlo, díselo al equipo /empleo.');
    expect(elemento.querySelector('app-texto-markdown strong')?.textContent).toBe('del equipo');
    const secciones = [...elemento.querySelectorAll('details')];
    expect(secciones.map((s) => s.hasAttribute('open'))).toEqual([false, true]);
    expect(elemento.querySelector('details[open] li')?.textContent).toBe('Remoto');
    // Solo se lee: nada de botones para editar
    expect(elemento.querySelectorAll('button')).toHaveLength(0);
  });

  it('abre los filtros aunque el título no empiece por «Filtros»; sin filtros, la primera', async () => {
    const elemento = await abrir();
    http.expectOne('/api/perfil').flush({
      ...PERFIL,
      secciones: [
        { titulo: 'Experiencia', markdown: '- Acme' },
        { titulo: '3. Mis filtros', markdown: '- Remoto' },
      ],
    });
    await fixture.whenStable();
    expect([...elemento.querySelectorAll('details')].map((s) => s.open)).toEqual([false, true]);

    TestBed.resetTestingModule();
    const otra = await abrir();
    http.expectOne('/api/perfil').flush({
      ...PERFIL,
      secciones: [
        { titulo: 'Experiencia', markdown: '- Acme' },
        { titulo: 'Notas', markdown: '- Nada' },
      ],
    });
    await fixture.whenStable();
    expect([...otra.querySelectorAll('details')].map((s) => s.open)).toEqual([true, false]);
  });

  it('si no lo encuentra, lo dice y deja reintentar', async () => {
    const elemento = await abrir();
    http
      .expectOne('/api/perfil')
      .flush(
        { detail: 'No encuentro tu carpeta Curriculum.' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();

    expect(elemento.querySelector('[role="alert"]')?.textContent).toContain(
      'No encuentro tu carpeta Curriculum.',
    );
    elemento.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    http.expectOne('/api/perfil').flush(PERFIL);
  });
});
