import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { firstValueFrom, isObservable, of } from 'rxjs';
import { AvisosService } from '../../../shared/services/avisos.service';
import { crearDetalle } from '../../utils/candidatura-de-prueba';
import ApuntarPageComponent from './apuntar-page.component';

describe('ApuntarPageComponent', () => {
  let fixture: ComponentFixture<ApuntarPageComponent>;
  let http: HttpTestingController;
  let avisos: { exito: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let navegar: ReturnType<typeof vi.fn>;
  let abrirDialogo: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    avisos = { exito: vi.fn(), error: vi.fn() };
    navegar = vi.fn(() => Promise.resolve(true));
    abrirDialogo = vi.fn(() => ({ afterClosed: () => of(false) }));
    TestBed.configureTestingModule({
      imports: [ApuntarPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AvisosService, useValue: avisos },
        { provide: Router, useValue: { navigate: navegar } },
        { provide: MatDialog, useValue: { open: abrirDialogo } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ApuntarPageComponent);
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function escribir(nombre: string, valor: string): void {
    const entrada = elemento().querySelector<HTMLInputElement>(`[formcontrolname="${nombre}"]`)!;
    entrada.value = valor;
    entrada.dispatchEvent(new Event('input'));
  }

  async function apuntar(): Promise<void> {
    escribir('empresa', 'Acme');
    escribir('puesto', 'Dev');
    elemento().querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await fixture.whenStable();
  }

  function alta(): ReturnType<HttpTestingController['expectOne']> {
    return http.expectOne((r) => r.method === 'POST' && r.url === '/api/candidaturas');
  }

  it('al guardar avisa y lleva a la ficha', async () => {
    await apuntar();

    alta().flush(crearDetalle({ id: 5 }), { status: 201, statusText: 'Created' });

    expect(avisos.exito).toHaveBeenCalledWith('Candidatura apuntada en «Detectada».');
    expect(navegar).toHaveBeenCalledWith(['/candidaturas', 5]);
  });

  describe('si se sale de la página mientras guarda', () => {
    it('el alta sigue adelante, avisa y no saca a nadie de donde esté', async () => {
      await apuntar();

      fixture.destroy();
      const peticion = alta();
      expect(peticion.cancelled).toBe(false);
      peticion.flush(crearDetalle({ id: 5 }), { status: 201, statusText: 'Created' });

      expect(avisos.exito).toHaveBeenCalledWith('Candidatura apuntada en «Detectada».');
      expect(navegar).not.toHaveBeenCalled();
    });

    it('un error por campo sale como aviso: ya no hay formulario donde enseñarlo', async () => {
      await apuntar();

      fixture.destroy();
      alta().flush(
        { detail: 'El puesto es demasiado largo.', errors: { puesto: ['Demasiado largo.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

      expect(avisos.error).toHaveBeenCalledWith('El puesto es demasiado largo.');
    });
  });

  describe('candado de salida', () => {
    function puedeSalir(): boolean | Promise<boolean> {
      const respuesta = fixture.componentInstance.puedeSalir();
      return isObservable(respuesta) ? firstValueFrom(respuesta) : respuesta;
    }

    it('sin nada escrito deja salir sin preguntar', async () => {
      expect(await puedeSalir()).toBe(true);
      expect(abrirDialogo).not.toHaveBeenCalled();
    });

    it('con algo escrito pregunta y obedece la respuesta', async () => {
      escribir('empresa', 'Acme');

      expect(await puedeSalir()).toBe(false);

      abrirDialogo.mockReturnValue({ afterClosed: () => of(true) });
      expect(await puedeSalir()).toBe(true);
      expect(abrirDialogo).toHaveBeenCalledTimes(2);
    });

    it('mientras guarda, o ya guardado, no pregunta: si no, frenaría el paso a la ficha', async () => {
      await apuntar();

      expect(await puedeSalir()).toBe(true);
      alta().flush(crearDetalle(), { status: 201, statusText: 'Created' });
      expect(await puedeSalir()).toBe(true);
      expect(abrirDialogo).not.toHaveBeenCalled();
    });

    it('si el alta falla vuelve a preguntar', async () => {
      await apuntar();
      alta().flush({ detail: 'Ya existe.' }, { status: 409, statusText: 'Conflict' });

      expect(await puedeSalir()).toBe(false);
      expect(abrirDialogo).toHaveBeenCalledTimes(1);
    });
  });
});
