import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { AvisosService } from '../../../shared/services/avisos.service';
import { CandidaturaDetalle } from '../../interfaces/candidatura.interface';
import { crearDetalle } from '../../utils/candidatura-de-prueba';
import { DialogoCandidatura } from './dialogo-candidatura';

describe('DialogoCandidatura', () => {
  afterEach(() => vi.useRealTimers());

  let fixture: ComponentFixture<DialogoCandidatura>;
  let http: HttpTestingController;
  let cerrar: ReturnType<typeof vi.fn>;
  let abrirConfirmacion: ReturnType<typeof vi.fn>;
  let avisos: { exito: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let teclas: Subject<KeyboardEvent>;
  let clicFuera: Subject<MouseEvent>;

  async function crear(candidatura: CandidaturaDetalle | null = null): Promise<void> {
    cerrar = vi.fn();
    abrirConfirmacion = vi.fn();
    avisos = { exito: vi.fn(), error: vi.fn() };
    teclas = new Subject();
    clicFuera = new Subject();

    TestBed.configureTestingModule({
      imports: [DialogoCandidatura],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MAT_DIALOG_DATA, useValue: { candidatura } },
        {
          provide: MatDialogRef,
          useValue: { close: cerrar, keydownEvents: () => teclas, backdropClick: () => clicFuera },
        },
        { provide: AvisosService, useValue: avisos },
      ],
    });
    // overrideProvider: MatDialogModule, importado por el componente, trae su propio MatDialog
    TestBed.overrideProvider(MatDialog, { useValue: { open: abrirConfirmacion } });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DialogoCandidatura);
    await fixture.whenStable();
  }

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function campo(nombre: string): HTMLInputElement {
    return elemento().querySelector<HTMLInputElement>(`[formcontrolname="${nombre}"]`)!;
  }

  function escribir(nombre: string, valor: string): void {
    const entrada = campo(nombre);
    entrada.value = valor;
    entrada.dispatchEvent(new Event('input'));
  }

  function boton(texto: string): HTMLButtonElement {
    const encontrado = [...elemento().querySelectorAll('button')].find((b) =>
      b.textContent?.includes(texto),
    );
    return encontrado!;
  }

  async function pulsar(texto: string): Promise<void> {
    boton(texto).click();
    await fixture.whenStable();
  }

  /** La respuesta de la confirmación «¿Salir sin guardar?» */
  function responderConfirmacion(respuesta: boolean): void {
    abrirConfirmacion.mockReturnValue({ afterClosed: () => of(respuesta) });
  }

  function pulsarEsc(): void {
    teclas.next(new KeyboardEvent('keydown', { key: 'Escape' }));
  }

  function peticionAlta(): ReturnType<HttpTestingController['expectOne']> {
    return http.expectOne((r) => r.method === 'POST' && r.url === '/api/candidaturas');
  }

  const ERROR_400 = {
    title: 'Hay datos que no son válidos.',
    status: 400,
    detail: 'El puesto no puede tener más de 300 caracteres.',
    errors: { puesto: ['El puesto no puede tener más de 300 caracteres.'] },
  };

  describe('botones fijos', () => {
    it('van en mat-dialog-actions, fuera del <form>, y el de guardar queda asociado a él', async () => {
      await crear();

      const guardar = boton('Crear candidatura');
      const formulario = elemento().querySelector('form')!;

      expect(guardar.closest('mat-dialog-actions')).not.toBeNull();
      expect(guardar.closest('form')).toBeNull();
      expect(guardar.getAttribute('form')).toBe(formulario.id);
      expect(guardar.type).toBe('submit');
    });

    it('el clic en el botón de fuera envía el alta, avisa y cierra con la ficha', async () => {
      await crear();
      escribir('enlace', 'https://www.linkedin.com/jobs/view/123');
      escribir('empresa', ' Acme ');
      escribir('puesto', 'Desarrollador');

      await pulsar('Crear candidatura');

      const peticion = peticionAlta();
      expect(peticion.request.body).toEqual({
        empresa: 'Acme',
        puesto: 'Desarrollador',
        enlace: 'https://www.linkedin.com/jobs/view/123',
        portal: 'LinkedIn',
      });
      const guardada = crearDetalle({ id: 5 });
      peticion.flush(guardada, { status: 201, statusText: 'Created' });

      expect(avisos.exito).toHaveBeenCalledWith(
        'Candidatura creada en «Detectada».',
        expect.objectContaining({ texto: 'Ver ficha' }),
      );
      expect(cerrar).toHaveBeenCalledWith(guardada);
    });

    it('sin los obligatorios no envía nada, lo explica y lleva el foco al primero', async () => {
      await crear();

      await pulsar('Crear candidatura');
      await fixture.whenStable();

      http.expectNone('/api/candidaturas');
      expect(elemento().textContent).toContain('La empresa es obligatoria.');
      expect(elemento().textContent).toContain('El puesto es obligatorio.');
      expect(document.activeElement).toBe(campo('empresa'));
      expect(cerrar).not.toHaveBeenCalled();
    });

    it('dos envíos instantáneos, sin tiempo a repintar, crean una sola candidatura', async () => {
      await crear();
      escribir('empresa', 'Acme');
      escribir('puesto', 'Dev');

      boton('Crear candidatura').click();
      boton('Crear candidatura').click();
      await fixture.whenStable();

      peticionAlta().flush(crearDetalle(), { status: 201, statusText: 'Created' });
    });

    it('si se cierra por navegar mientras guarda, el alta sigue y no toca el diálogo', async () => {
      await crear();
      escribir('empresa', 'Acme');
      escribir('puesto', 'Dev');
      await pulsar('Crear candidatura');

      fixture.destroy();
      const peticion = peticionAlta();
      expect(peticion.cancelled).toBe(false);
      peticion.flush(crearDetalle(), { status: 201, statusText: 'Created' });

      expect(avisos.exito).toHaveBeenCalled();
      expect(cerrar).not.toHaveBeenCalled();
    });

    it('mientras guarda enseña «Guardando...», no envía dos veces y no deja cancelar', async () => {
      await crear();
      escribir('empresa', 'Acme');
      escribir('puesto', 'Dev');

      await pulsar('Crear candidatura');

      expect(boton('Guardando...')).toBeTruthy();
      expect(boton('Cancelar').disabled).toBe(true);
      await pulsar('Guardando...');
      peticionAlta().flush(crearDetalle(), { status: 201, statusText: 'Created' });
    });
  });

  describe('errores de la API', () => {
    async function enviarConError(cuerpo: object, status: number): Promise<void> {
      escribir('empresa', 'Acme');
      escribir('puesto', 'Dev');
      await pulsar('Crear candidatura');
      peticionAlta().flush(cuerpo, { status, statusText: 'Error' });
      await fixture.whenStable();
      await fixture.whenStable();
    }

    it('un 400 sale en su campo, deja el foco ahí y no cierra', async () => {
      await crear();

      await enviarConError(ERROR_400, 400);

      expect(elemento().textContent).toContain('El puesto no puede tener más de 300 caracteres.');
      expect(document.activeElement).toBe(campo('puesto'));
      expect(cerrar).not.toHaveBeenCalled();
      expect(avisos.error).not.toHaveBeenCalled();
      expect(boton('Crear candidatura').disabled).toBe(false);
    });

    it('el error del campo se quita al corregirlo y se puede volver a enviar', async () => {
      await crear();
      await enviarConError(ERROR_400, 400);

      escribir('puesto', 'Dev Angular');
      await pulsar('Crear candidatura');

      expect(peticionAlta().request.body).toMatchObject({ puesto: 'Dev Angular' });
    });

    it('un 400 de un campo que no está en el formulario sale arriba, con role="alert"', async () => {
      await crear();

      await enviarConError({ ...ERROR_400, errors: { fecha: ['La fecha no vale.'] } }, 400);

      expect(elemento().querySelector('[role="alert"]')?.textContent).toContain(
        'La fecha no vale.',
      );
      expect(cerrar).not.toHaveBeenCalled();
    });

    it('un error sin campos (409, sin conexión...) va al aviso y conserva lo escrito', async () => {
      await crear();

      await enviarConError({ detail: 'Ya existe un registro con esos mismos datos.' }, 409);

      expect(avisos.error).toHaveBeenCalledWith('Ya existe un registro con esos mismos datos.');
      expect(cerrar).not.toHaveBeenCalled();
      expect(campo('empresa').value).toBe('Acme');
    });
  });

  describe('salir sin guardar', () => {
    it('Esc sin cambios cierra sin preguntar', async () => {
      await crear();

      pulsarEsc();

      expect(abrirConfirmacion).not.toHaveBeenCalled();
      expect(cerrar).toHaveBeenCalled();
    });

    it('Esc con cambios pregunta y no cierra si se decide seguir', async () => {
      await crear();
      escribir('empresa', 'Acme');
      responderConfirmacion(false);

      pulsarEsc();

      expect(abrirConfirmacion).toHaveBeenCalledTimes(1);
      expect(cerrar).not.toHaveBeenCalled();
    });

    it('Esc con cambios cierra si se confirma', async () => {
      await crear();
      escribir('empresa', 'Acme');
      responderConfirmacion(true);

      pulsarEsc();

      expect(cerrar).toHaveBeenCalled();
    });

    it('el clic fuera y Cancelar preguntan igual que Esc', async () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      await crear();
      escribir('empresa', 'Acme');
      responderConfirmacion(false);

      vi.setSystemTime(Date.now() + 500);
      clicFuera.next(new MouseEvent('click'));
      await pulsar('Cancelar');

      expect(abrirConfirmacion).toHaveBeenCalledTimes(2);
      expect(cerrar).not.toHaveBeenCalled();
    });

    it('mientras guarda, Esc no hace nada', async () => {
      await crear();
      escribir('empresa', 'Acme');
      escribir('puesto', 'Dev');
      await pulsar('Crear candidatura');

      pulsarEsc();

      expect(abrirConfirmacion).not.toHaveBeenCalled();
      expect(cerrar).not.toHaveBeenCalled();
      peticionAlta().flush(crearDetalle(), { status: 201, statusText: 'Created' });
    });
  });

  describe('alta y edición', () => {
    it('el alta empieza por el enlace y solo enseña lo básico', async () => {
      await crear();

      const campos = [...elemento().querySelectorAll('[formcontrolname]')].map((c) =>
        c.getAttribute('formcontrolname'),
      );
      expect(campos).toEqual(['enlace', 'empresa', 'puesto']);
      expect(boton('Añadir más datos')).toBeTruthy();
    });

    it('«Añadir más datos» despliega el resto, sin perder lo escrito, y los envía', async () => {
      await crear();
      escribir('empresa', 'Acme');
      escribir('puesto', 'Dev');

      await pulsar('Añadir más datos');
      expect(campo('empresa').value).toBe('Acme');
      expect(document.activeElement).toBe(campo('portal'));
      expect(boton('Añadir más datos')).toBeUndefined();
      escribir('salario', '30.000 €');

      await pulsar('Crear candidatura');

      expect(peticionAlta().request.body).toMatchObject({
        empresa: 'Acme',
        salario: '30.000 €',
        modalidad: 'NoIndicada',
        encaje: 'SinValorar',
      });
    });

    it('al editar enseña todo relleno y guarda con PUT sin tocar el estado', async () => {
      await crear(crearDetalle({ id: 3, empresa: 'Acme', puesto: 'Dev', salario: '30.000 €' }));

      expect(boton('Añadir más datos')).toBeUndefined();
      expect(campo('salario').value).toBe('30.000 €');
      escribir('puesto', 'Dev senior');

      await pulsar('Guardar cambios');

      const peticion = http.expectOne('/api/candidaturas/3');
      expect(peticion.request.method).toBe('PUT');
      expect(peticion.request.body).toMatchObject({ puesto: 'Dev senior', salario: '30.000 €' });
      expect(peticion.request.body).not.toHaveProperty('estado');
      peticion.flush(crearDetalle({ id: 3 }));

      expect(avisos.exito).toHaveBeenCalledWith('Cambios guardados.');
      expect(cerrar).toHaveBeenCalled();
    });
  });
});
