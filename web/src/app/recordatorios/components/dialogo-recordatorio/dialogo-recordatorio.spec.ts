import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { EMPTY, of, Subject, throwError } from 'rxjs';
import { Recordatorio } from '../../../candidaturas/interfaces/candidatura.interface';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import { AvisosService } from '../../../shared/services/avisos.service';
import { RecordatoriosService } from '../../services/recordatorios.service';
import { crearRecordatorio } from '../../utils/recordatorio-de-prueba';
import { DatosDialogoRecordatorio, DialogoRecordatorio } from './dialogo-recordatorio';

describe('DialogoRecordatorio', () => {
  let fixture: ComponentFixture<DialogoRecordatorio>;
  let cerrar: ReturnType<typeof vi.fn>;
  let abrirConfirmacion: ReturnType<typeof vi.fn>;
  let crearRecordatorioApi: ReturnType<typeof vi.fn>;
  let editar: ReturnType<typeof vi.fn>;
  let avisos: Record<'exito' | 'error', ReturnType<typeof vi.fn>>;
  let teclas: Subject<KeyboardEvent>;
  let clicFuera: Subject<MouseEvent>;

  const CANDIDATURA = {
    id: 7,
    empresa: 'Consultora Ejemplo',
    puesto: 'Desarrollador .NET',
    estado: 'Entrevista' as const,
    enlace: null,
  };

  async function crear(datos: DatosDialogoRecordatorio): Promise<void> {
    cerrar = vi.fn();
    abrirConfirmacion = vi.fn();
    crearRecordatorioApi = vi.fn(() =>
      of(crearRecordatorio({ fechaHora: '2026-11-17T08:00:00Z' })),
    );
    editar = vi.fn(() => of(crearRecordatorio()));
    avisos = { exito: vi.fn(), error: vi.fn() };
    teclas = new Subject();
    clicFuera = new Subject();
    TestBed.configureTestingModule({
      imports: [DialogoRecordatorio],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: datos },
        {
          provide: MatDialogRef,
          useValue: { close: cerrar, keydownEvents: () => teclas, backdropClick: () => clicFuera },
        },
        { provide: RecordatoriosService, useValue: { crear: crearRecordatorioApi, editar } },
        { provide: AvisosService, useValue: avisos },
      ],
    });
    // overrideProvider: MatDialogModule, importado por el componente, trae su propio MatDialog
    TestBed.overrideProvider(MatDialog, { useValue: { open: abrirConfirmacion } });
    fixture = TestBed.createComponent(DialogoRecordatorio);
    await fixture.whenStable();
  }

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function campo(nombre: string): HTMLInputElement {
    return elemento().querySelector<HTMLInputElement>(`[formcontrolname="${nombre}"]`)!;
  }

  async function escribir(nombre: string, valor: string): Promise<void> {
    const entrada = campo(nombre);
    entrada.value = valor;
    entrada.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  function tipos(): HTMLInputElement[] {
    return [...elemento().querySelectorAll<HTMLInputElement>('mat-radio-button input')];
  }

  async function elegirTipo(etiqueta: string): Promise<void> {
    const opcion = [...elemento().querySelectorAll('mat-radio-button')].find((o) =>
      o.textContent?.includes(etiqueta),
    )!;
    opcion.querySelector<HTMLInputElement>('input')!.click();
    await fixture.whenStable();
  }

  function tipoElegido(): string {
    const marcado = tipos().find((t) => t.checked)!;
    return marcado.closest('mat-radio-button')!.textContent!.trim();
  }

  async function enviar(): Promise<void> {
    elemento().querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await fixture.whenStable();
  }

  afterEach(() => vi.useRealTimers());

  describe('uno nuevo', () => {
    it('desde Hoy empieza como publicación de LinkedIn, con el título puesto y a las 09:00', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });

      expect(elemento().querySelector('h2')?.textContent).toContain('Nuevo recordatorio');
      expect(tipos()).toHaveLength(5);
      expect(tipoElegido()).toBe('Publicación en LinkedIn');
      expect(campo('titulo').value).toBe('Publicar en LinkedIn');
      expect(campo('dia').value).toBe('');
      expect(campo('hora').value).toBe('09:00');
      expect(elemento().textContent).not.toContain('Para:');
    });

    it('desde la ficha empieza con el tipo que se le pide y nombra la candidatura', async () => {
      await crear({ tipoInicial: 'Entrevista', candidatura: CANDIDATURA });

      expect(tipoElegido()).toBe('Entrevista');
      expect(campo('titulo').value).toBe('Entrevista en Consultora Ejemplo');
      expect(elemento().textContent).toContain('Para:');
      expect(elemento().textContent).toContain('Desarrollador .NET');
    });

    it('los cinco tipos van en dos columnas también en el móvil, como grupo de radios', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });

      const grupo = elemento().querySelector('mat-radio-group')!;
      expect(grupo.getAttribute('role')).toBe('radiogroup');
      expect(grupo.getAttribute('aria-labelledby')).toBe('titulo-tipo-recordatorio');
      expect(grupo.classList.contains('grid-cols-2')).toBe(true);
      expect(grupo.classList.contains('grid-cols-1')).toBe(false);
    });

    it('el título cambia con el tipo mientras no se haya tocado', async () => {
      await crear({ tipoInicial: 'Entrevista', candidatura: CANDIDATURA });

      await elegirTipo('Plazo');
      expect(campo('titulo').value).toBe('Plazo de Consultora Ejemplo');

      // El mismo que pone la API al programarlo sola
      await elegirTipo('Seguimiento');
      expect(campo('titulo').value).toBe('Seguimiento: Desarrollador .NET en Consultora Ejemplo');

      await elegirTipo('Otro');
      expect(campo('titulo').value).toBe('');
    });

    it('si se ha escrito un título, cambiar el tipo no lo pisa', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      await escribir('titulo', 'Post de Supabase');

      await elegirTipo('Otro');

      expect(campo('titulo').value).toBe('Post de Supabase');
    });

    it('sin día no guarda: marca el error y lleva el foco al campo', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });

      await enviar();

      expect(crearRecordatorioApi).not.toHaveBeenCalled();
      expect(elemento().textContent).toContain('El día es obligatorio.');
      expect(document.activeElement).toBe(campo('dia'));
    });

    it('un atajo pone el día sin abrir el calendario, y queda marcado', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      const atajos = [...elemento().querySelectorAll<HTMLButtonElement>('[role="group"] button')];
      expect(atajos.map((a) => a.textContent?.trim())).toEqual(['Hoy', 'Mañana', 'En una semana']);

      atajos[1].click();
      await fixture.whenStable();

      expect(campo('dia').value).toMatch(/^\d{4}-\d{2}-\d{2}$/);
      expect(atajos[1].getAttribute('aria-pressed')).toBe('true');
      expect(atajos[0].getAttribute('aria-pressed')).toBe('false');
    });

    it('con el diálogo abierto a medianoche, los atajos pasan a contar desde el día nuevo', async () => {
      vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'] });
      vi.setSystemTime(new Date('2026-11-13T22:59:50Z')); // viernes 13, 23:59:50 en Madrid
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      const atajo = (texto: string) =>
        [...elemento().querySelectorAll<HTMLButtonElement>('[role="group"] button')].find(
          (a) => a.textContent?.trim() === texto,
        )!;
      atajo('Hoy').click();
      await fixture.whenStable();
      expect(campo('dia').value).toBe('2026-11-13');

      vi.advanceTimersByTime(15_000);
      await fixture.whenStable();
      atajo('Hoy').click();
      await fixture.whenStable();

      expect(campo('dia').value).toBe('2026-11-14');
    });

    it('debajo del día dice en qué cae y avisa si ya ha pasado', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });

      await escribir('dia', '2020-03-10');
      expect(elemento().textContent).toContain(
        'Martes 10 de marzo de 2020: ya ha pasado, saldrá como vencido.',
      );

      await escribir('dia', '2099-03-10');
      expect(elemento().textContent).toContain('Martes 10 de marzo de 2099.');
      expect(elemento().textContent).not.toContain('ya ha pasado');
    });

    describe('la hora de una entrevista', () => {
      function etiquetaHora(): string {
        return campo('hora').closest('mat-form-field')!.querySelector('mat-label')!.textContent!;
      }

      it('en los demás tipos es opcional y empieza a las 09:00', async () => {
        await crear({ tipoInicial: 'PublicacionLinkedIn' });

        expect(campo('hora').value).toBe('09:00');
        expect(campo('hora').required).toBe(false);
        expect(etiquetaHora()).toContain('Hora (opcional)');
        expect(elemento().textContent).toContain('Si la dejas vacía, a las 09:00.');
      });

      it('una entrevista nueva empieza sin hora, y el campo dice que es obligatoria', async () => {
        await crear({ tipoInicial: 'Entrevista', candidatura: CANDIDATURA });

        expect(campo('hora').value).toBe('');
        expect(campo('hora').required).toBe(true);
        expect(etiquetaHora()).not.toContain('opcional');
        expect(elemento().textContent).toContain('Obligatoria: es una entrevista.');
        expect(elemento().textContent).not.toContain('Si la dejas vacía');
      });

      it('sin hora no guarda: marca el error y lleva el foco al campo', async () => {
        await crear({ tipoInicial: 'Entrevista', candidatura: CANDIDATURA });
        await escribir('dia', '2026-11-17');

        await enviar();

        expect(crearRecordatorioApi).not.toHaveBeenCalled();
        expect(cerrar).not.toHaveBeenCalled();
        expect(elemento().textContent).toContain('Pon la hora de la entrevista.');
        expect(document.activeElement).toBe(campo('hora'));
      });

      it('con la hora puesta guarda', async () => {
        await crear({ tipoInicial: 'Entrevista', candidatura: CANDIDATURA });
        await escribir('dia', '2026-11-17');
        await escribir('hora', '09:00');

        await enviar();

        expect(crearRecordatorioApi.mock.calls[0][0].hora).toBe('09:00');
      });

      it('al pasar a Entrevista, las 09:00 que se pusieron solas se vacían', async () => {
        await crear({ tipoInicial: 'Seguimiento', candidatura: CANDIDATURA });
        expect(campo('hora').value).toBe('09:00');

        await elegirTipo('Entrevista');

        expect(campo('hora').value).toBe('');
        expect(campo('hora').required).toBe(true);
        await escribir('dia', '2026-11-17');
        await enviar();
        expect(crearRecordatorioApi).not.toHaveBeenCalled();
      });

      it('y al volver a otro tipo sin haberla tocado, vuelven', async () => {
        await crear({ tipoInicial: 'Entrevista', candidatura: CANDIDATURA });

        await elegirTipo('Plazo');

        expect(campo('hora').value).toBe('09:00');
        expect(campo('hora').required).toBe(false);
        await escribir('dia', '2026-11-17');
        await enviar();
        expect(crearRecordatorioApi).toHaveBeenCalled();
      });

      it('la hora que ha puesto el usuario se respeta al pasar a Entrevista, aunque sean las 09:00', async () => {
        await crear({ tipoInicial: 'Seguimiento', candidatura: CANDIDATURA });
        await escribir('hora', '09:00');

        await elegirTipo('Entrevista');

        expect(campo('hora').value).toBe('09:00');
        await escribir('dia', '2026-11-17');
        await enviar();
        expect(crearRecordatorioApi.mock.calls[0][0]).toEqual(
          expect.objectContaining({ tipo: 'Entrevista', hora: '09:00' }),
        );
      });

      it('en otro tipo, vaciarla sigue valiendo aunque antes fuera una entrevista', async () => {
        await crear({ tipoInicial: 'Entrevista', candidatura: CANDIDATURA });
        await escribir('hora', '15:00');
        await elegirTipo('Plazo');
        await escribir('hora', '');
        await escribir('dia', '2026-11-17');

        await enviar();

        expect(crearRecordatorioApi.mock.calls[0][0].hora).toBeNull();
      });
    });

    describe('una hora que no existe por el cambio de hora de marzo', () => {
      const MENSAJE = 'Esa hora no existe ese día: el reloj se adelanta de 02:00 a 03:00.';

      it('no se manda: avisa en la hora y lleva el foco al campo', async () => {
        await crear({ tipoInicial: 'Otro' });
        await escribir('titulo', 'Renovar la demanda');
        await escribir('dia', '2027-03-28');
        await escribir('hora', '02:30');

        await enviar();

        expect(crearRecordatorioApi).not.toHaveBeenCalled();
        expect(elemento().textContent).toContain(MENSAJE);
        expect(document.activeElement).toBe(campo('hora'));
      });

      it('si se elige el día después de la hora, avisa sin esperar a guardar', async () => {
        await crear({ tipoInicial: 'Otro' });
        await escribir('hora', '02:30');

        await escribir('dia', '2027-03-28');

        expect(elemento().textContent).toContain(MENSAJE);
      });

      it('con otro día, la misma hora vale', async () => {
        await crear({ tipoInicial: 'Otro' });
        await escribir('titulo', 'Renovar la demanda');
        await escribir('dia', '2027-03-28');
        await escribir('hora', '02:30');

        await escribir('dia', '2027-03-27');
        await enviar();

        expect(elemento().textContent).not.toContain(MENSAJE);
        expect(crearRecordatorioApi.mock.calls[0][0]).toMatchObject({
          dia: '2027-03-27',
          hora: '02:30',
        });
      });
    });

    it('con «Otro» y sin título pide el título', async () => {
      await crear({ tipoInicial: 'Otro' });
      await escribir('dia', '2026-11-17');

      await enviar();

      expect(crearRecordatorioApi).not.toHaveBeenCalled();
      expect(elemento().textContent).toContain('El título es obligatorio.');
    });

    it('guarda uno suelto con el día y la hora tal cual, avisa con la fecha y se cierra', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      await escribir('dia', '2026-11-17');

      await enviar();

      expect(crearRecordatorioApi).toHaveBeenCalledWith(
        {
          tipo: 'PublicacionLinkedIn',
          titulo: 'Publicar en LinkedIn',
          dia: '2026-11-17',
          hora: '09:00',
          notas: null,
        },
        undefined,
      );
      expect(avisos.exito).toHaveBeenCalledWith(
        'Recordatorio guardado para el mar 17 nov a las 09:00.',
      );
      expect(cerrar).toHaveBeenCalledWith(expect.objectContaining({ id: 1 }));
    });

    it('guarda el de una candidatura con su id, la hora elegida y las notas', async () => {
      await crear({ tipoInicial: 'Entrevista', candidatura: CANDIDATURA });
      await escribir('dia', '2026-11-11');
      await escribir('hora', '15:00');
      await escribir('notas', '  Por Teams  ');

      await enviar();

      expect(crearRecordatorioApi).toHaveBeenCalledWith(
        {
          tipo: 'Entrevista',
          titulo: 'Entrevista en Consultora Ejemplo',
          dia: '2026-11-11',
          hora: '15:00',
          notas: 'Por Teams',
        },
        7,
      );
    });

    it('con la hora vacía manda null (la API pone las 09:00)', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      await escribir('dia', '2026-11-17');
      await escribir('hora', '');

      await enviar();

      expect(crearRecordatorioApi.mock.calls[0][0].hora).toBeNull();
    });

    it('los errores del servidor van a su campo y el diálogo sigue abierto', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      await escribir('dia', '2026-11-17');
      const error: ErrorApi = {
        estado: 400,
        mensaje: 'Hay datos que no son válidos.',
        errores: { dia: ['El día tiene que estar entre 2000 y 2100.'] },
      };
      crearRecordatorioApi.mockReturnValue(throwError(() => error));

      await enviar();

      expect(cerrar).not.toHaveBeenCalled();
      expect(elemento().textContent).toContain('El día tiene que estar entre 2000 y 2100.');
      expect(avisos.error).not.toHaveBeenCalled();
      expect(
        elemento().querySelector<HTMLButtonElement>('button[type="submit"]')!.textContent,
      ).toContain('Guardar recordatorio');
    });

    it('un error sin campo sale como aviso', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      await escribir('dia', '2026-11-17');
      crearRecordatorioApi.mockReturnValue(
        throwError(() => ({ estado: 0, mensaje: 'No se puede conectar.', errores: {} })),
      );

      await enviar();

      expect(avisos.error).toHaveBeenCalledWith('No se puede conectar.');
      expect(cerrar).not.toHaveBeenCalled();
    });

    it('mientras guarda no manda otra petición', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      await escribir('dia', '2026-11-17');
      crearRecordatorioApi.mockReturnValue(new Subject<Recordatorio>());

      await enviar();
      await enviar();

      expect(crearRecordatorioApi).toHaveBeenCalledTimes(1);
      expect(
        elemento().querySelector<HTMLButtonElement>('button[type="submit"]')!.textContent,
      ).toContain('Guardando...');
    });
  });

  describe('editar', () => {
    // 11/11/2026 a las 15:00 de Madrid; en julio serían las 16:00
    const RECORDATORIO = crearRecordatorio({
      id: 4,
      candidaturaId: 7,
      tipo: 'Entrevista',
      titulo: 'Entrevista con RR. HH.',
      fechaHora: '2026-11-11T14:00:00Z',
      notas: 'Por Teams',
    });

    it('enseña sus datos, con el día y la hora de Madrid', async () => {
      await crear({ recordatorio: RECORDATORIO, candidatura: CANDIDATURA });

      expect(elemento().querySelector('h2')?.textContent).toContain('Editar recordatorio');
      expect(tipoElegido()).toBe('Entrevista');
      expect(campo('titulo').value).toBe('Entrevista con RR. HH.');
      expect(campo('dia').value).toBe('2026-11-11');
      expect(campo('hora').value).toBe('15:00');
      expect(campo('notas').value).toBe('Por Teams');
    });

    it('un recordatorio de pasada la medianoche de Madrid sale en su día de Madrid', async () => {
      await crear({ recordatorio: { ...RECORDATORIO, fechaHora: '2026-07-14T22:30:00Z' } });

      expect(campo('dia').value).toBe('2026-07-15');
      expect(campo('hora').value).toBe('00:30');
    });

    it('cambiar el tipo no le cambia el título', async () => {
      await crear({ recordatorio: RECORDATORIO, candidatura: CANDIDATURA });

      await elegirTipo('Plazo');

      expect(campo('titulo').value).toBe('Entrevista con RR. HH.');
    });

    describe('la hora de una entrevista', () => {
      // Apuntada cuando la hora no era obligatoria: a las 09:00 de Madrid
      const A_LAS_NUEVE = { ...RECORDATORIO, fechaHora: '2026-11-11T08:00:00Z' };

      it('una entrevista ya guardada a las 09:00 conserva su hora y deja cambiar lo demás', async () => {
        await crear({ recordatorio: A_LAS_NUEVE, candidatura: CANDIDATURA });
        expect(campo('hora').value).toBe('09:00');

        await escribir('notas', 'Por Meet');
        await enviar();

        expect(editar).toHaveBeenCalledWith(A_LAS_NUEVE, {
          tipo: 'Entrevista',
          titulo: 'Entrevista con RR. HH.',
          dia: '2026-11-11',
          hora: '09:00',
          notas: 'Por Meet',
        });
      });

      it('si se le borra la hora, no guarda', async () => {
        await crear({ recordatorio: RECORDATORIO, candidatura: CANDIDATURA });
        await escribir('hora', '');

        await enviar();

        expect(editar).not.toHaveBeenCalled();
        expect(elemento().textContent).toContain('Pon la hora de la entrevista.');
      });

      it('pasar a Entrevista uno ya guardado no le quita su hora', async () => {
        const seguimiento = { ...A_LAS_NUEVE, tipo: 'Seguimiento' as const };
        await crear({ recordatorio: seguimiento, candidatura: CANDIDATURA });

        await elegirTipo('Entrevista');

        expect(campo('hora').value).toBe('09:00');
        expect(campo('hora').required).toBe(true);
      });
    });

    it('guarda con editar y no con crear', async () => {
      await crear({ recordatorio: RECORDATORIO, candidatura: CANDIDATURA });
      await escribir('dia', '2026-11-12');

      await enviar();

      expect(crearRecordatorioApi).not.toHaveBeenCalled();
      expect(editar).toHaveBeenCalledWith(RECORDATORIO, {
        tipo: 'Entrevista',
        titulo: 'Entrevista con RR. HH.',
        dia: '2026-11-12',
        hora: '15:00',
        notas: 'Por Teams',
      });
      expect(cerrar).toHaveBeenCalled();
    });

    it('si ese recordatorio tenía otra operación en marcha, vuelve a dejar guardar', async () => {
      await crear({ recordatorio: RECORDATORIO });
      editar.mockReturnValue(EMPTY);

      await enviar();

      expect(cerrar).not.toHaveBeenCalled();
      expect(
        elemento().querySelector<HTMLButtonElement>('button[type="submit"]')!.textContent,
      ).toContain('Guardar recordatorio');
    });
  });

  describe('cerrar sin guardar', () => {
    function cancelar(): void {
      [...elemento().querySelectorAll('button')]
        .find((b) => b.textContent?.includes('Cancelar'))!
        .click();
    }

    it('sin nada escrito se cierra sin preguntar', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });

      cancelar();

      expect(abrirConfirmacion).not.toHaveBeenCalled();
      expect(cerrar).toHaveBeenCalledWith();
    });

    it('con algo escrito pregunta y solo se cierra si se confirma', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      await escribir('notas', 'Algo');
      abrirConfirmacion.mockReturnValue({ afterClosed: () => of(false) });

      cancelar();

      expect(abrirConfirmacion).toHaveBeenCalledTimes(1);
      expect(cerrar).not.toHaveBeenCalled();

      abrirConfirmacion.mockReturnValue({ afterClosed: () => of(true) });
      cancelar();

      expect(cerrar).toHaveBeenCalledWith();
    });

    it('Esc también pregunta si hay algo escrito', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      await escribir('titulo', 'Otro título');
      abrirConfirmacion.mockReturnValue({ afterClosed: () => of(false) });

      teclas.next(new KeyboardEvent('keydown', { key: 'Escape' }));

      expect(abrirConfirmacion).toHaveBeenCalledTimes(1);
      expect(cerrar).not.toHaveBeenCalled();
    });

    it('mientras guarda no se cierra', async () => {
      await crear({ tipoInicial: 'PublicacionLinkedIn' });
      await escribir('dia', '2026-11-17');
      crearRecordatorioApi.mockReturnValue(new Subject<Recordatorio>());
      await enviar();

      teclas.next(new KeyboardEvent('keydown', { key: 'Escape' }));

      expect(cerrar).not.toHaveBeenCalled();
      expect(abrirConfirmacion).not.toHaveBeenCalled();
    });
  });
});
