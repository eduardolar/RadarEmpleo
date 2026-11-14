import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Recordatorio } from '../../../candidaturas/interfaces/candidatura.interface';
import { RecordatorioAgenda } from '../../../hoy/interfaces/panel-hoy.interface';
import { crearRecordatorio, crearRecordatorioAgenda } from '../../utils/recordatorio-de-prueba';
import { TarjetaRecordatorio } from './tarjeta-recordatorio';

describe('TarjetaRecordatorio', () => {
  let fixture: ComponentFixture<TarjetaRecordatorio>;

  const DE_CANDIDATURA = crearRecordatorioAgenda({
    id: 2,
    candidaturaId: 7,
    tipo: 'Entrevista',
    titulo: 'Entrevista en Consultora Ejemplo',
    candidatura: {
      id: 7,
      empresa: 'Consultora Ejemplo',
      puesto: 'Desarrollador .NET',
      estado: 'Entrevista',
      enlace: null,
    },
  });

  async function crear(
    recordatorio: Recordatorio | RecordatorioAgenda,
    entradas: Record<string, unknown> = {},
  ): Promise<void> {
    TestBed.configureTestingModule({
      imports: [TarjetaRecordatorio],
      providers: [provideRouter([])],
    });
    fixture = TestBed.createComponent(TarjetaRecordatorio);
    fixture.componentRef.setInput('recordatorio', recordatorio);
    fixture.componentRef.setInput('hoy', '2026-11-13');
    for (const [nombre, valor] of Object.entries(entradas)) {
      fixture.componentRef.setInput(nombre, valor);
    }
    await fixture.whenStable();
  }

  function elemento(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function boton(texto: string): HTMLButtonElement | undefined {
    return [...elemento().querySelectorAll('button')].find((b) =>
      b.textContent?.trim().startsWith(texto),
    );
  }

  function botonMas(): HTMLButtonElement {
    return elemento().querySelector<HTMLButtonElement>('button[aria-label^="Más acciones"]')!;
  }

  /** Los menús se pintan fuera de la tarjeta, en la capa de Material */
  function opcionesDelMenu(): HTMLButtonElement[] {
    return [...document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')];
  }

  async function abrirMenu(disparador: HTMLButtonElement): Promise<void> {
    disparador.click();
    await fixture.whenStable();
  }

  function emitidos(
    salida: 'hecho' | 'pendiente' | 'posponer' | 'editar' | 'borrar' | 'seleccionar',
  ): unknown[] {
    const valores: unknown[] = [];
    fixture.componentInstance[salida].subscribe((valor: unknown) => valores.push(valor ?? true));
    return valores;
  }

  it('enseña el tipo con texto, el título, la hora de Madrid y las notas', async () => {
    await crear(crearRecordatorio({ notas: 'Post de Angular' }));

    expect(elemento().querySelector('.insignia')?.textContent).toContain('Publicación en LinkedIn');
    expect(elemento().textContent).toContain('Publicar en LinkedIn');
    expect(elemento().querySelector('time')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'Hoy · 09:00',
    );
    expect(elemento().querySelector('time')?.getAttribute('datetime')).toBe('2026-11-13T08:00:00Z');
    expect(elemento().textContent).toContain('Post de Angular');
  });

  it('lo de otro día dice qué día es', async () => {
    await crear(crearRecordatorio({ fechaHora: '2026-11-17T14:00:00Z' }));

    expect(elemento().querySelector('time')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'mar 17 nov · 15:00',
    );
    expect(elemento().textContent).not.toContain('Vencido');
  });

  it('lo de otro año lleva el año', async () => {
    await crear(crearRecordatorio({ fechaHora: '2027-01-08T08:00:00Z' }));

    expect(elemento().querySelector('time')?.textContent).toContain('vie 8 ene 2027');
  });

  describe('«Vencido»', () => {
    it('sale, con texto y su día, en lo pendiente de un día anterior', async () => {
      await crear(crearRecordatorio({ fechaHora: '2026-11-10T08:00:00Z' }));

      const insignias = [...elemento().querySelectorAll('.insignia')].map((i) =>
        i.textContent!.replace(/\s+/g, ' ').trim(),
      );
      expect(insignias).toContain('Vencido · mar 10 nov');
    });

    it('no sale en lo de hoy, aunque la hora ya haya pasado', async () => {
      await crear(crearRecordatorio({ fechaHora: '2026-11-13T05:00:00Z' }));

      expect(elemento().textContent).not.toContain('Vencido');
    });

    it('no sale en lo ya hecho, aunque fuera de un día anterior', async () => {
      await crear(crearRecordatorio({ fechaHora: '2026-11-10T08:00:00Z', hecho: true }));

      expect(elemento().textContent).not.toContain('Vencido');
    });

    it('cuenta el día de Madrid: las 23:30Z de ayer ya son de hoy', async () => {
      await crear(crearRecordatorio({ fechaHora: '2026-11-12T23:30:00Z' }));

      expect(elemento().textContent).not.toContain('Vencido');
    });
  });

  it('con soloHora (va bajo el encabezado de su día) dice solo la hora', async () => {
    await crear(crearRecordatorio({ fechaHora: '2026-11-17T14:00:00Z' }), { soloHora: true });

    expect(elemento().querySelector('time')?.textContent?.trim()).toBe('15:00');
  });

  it('lo vencido sigue con su día aunque lleve soloHora', async () => {
    await crear(crearRecordatorio({ fechaHora: '2026-11-10T08:00:00Z' }), { soloHora: true });

    expect(elemento().textContent).toContain('Vencido · mar 10 nov');
  });

  describe('la candidatura', () => {
    it('con conCandidatura, el título es un enlace a su ficha y salen la empresa y el estado', async () => {
      await crear(
        { ...DE_CANDIDATURA, titulo: 'Plazo de la prueba técnica' },
        { conCandidatura: true },
      );

      const enlace = elemento().querySelector('a')!;
      expect(enlace.getAttribute('href')).toBe('/candidaturas/7');
      // Un título escrito a mano sale tal cual, y el enlace no necesita otro nombre
      expect(enlace.textContent).toContain('Plazo de la prueba técnica');
      expect(enlace.hasAttribute('aria-label')).toBe(false);
      // 48 px de zona de toque con el ::after, sin que la tarjeta crezca
      expect(enlace.classList.contains('after:-inset-y-3')).toBe(true);
      expect(elemento().textContent).toContain('Consultora Ejemplo');
      expect(elemento().querySelector('[data-estado="Entrevista"]')?.textContent).toContain(
        'Entrevista',
      );
    });

    it('el título que se pone solo se queda en el puesto, sin repetir el tipo ni la empresa', async () => {
      await crear(DE_CANDIDATURA, { conCandidatura: true });

      const enlace = elemento().querySelector<HTMLAnchorElement>('a[data-titulo]')!;
      expect(enlace.textContent?.trim()).toBe('Desarrollador .NET');
      expect(elemento().textContent).not.toContain('Entrevista en Consultora Ejemplo');
      // La empresa sigue en la línea de debajo, junto al estado
      expect(elemento().querySelector('.etiqueta')?.textContent).toBe('Consultora Ejemplo');
    });

    it('el seguimiento que programa la API también: fuera «Seguimiento:» y «en Empresa»', async () => {
      const seguimiento = {
        ...DE_CANDIDATURA,
        tipo: 'Seguimiento' as const,
        titulo: 'Seguimiento: Desarrollador .NET en Consultora Ejemplo',
      };

      await crear(seguimiento, { conCandidatura: true });

      expect(elemento().querySelector('a[data-titulo]')?.textContent?.trim()).toBe(
        'Desarrollador .NET',
      );
    });

    it('los aria-label llevan el título entero, para no perder el contexto', async () => {
      await crear(DE_CANDIDATURA, { conCandidatura: true, conPosponer: true });

      const completo = 'Entrevista en Consultora Ejemplo, Desarrollador .NET';
      expect(elemento().querySelector('article')!.getAttribute('aria-label')).toBe(
        `Entrevista: ${completo}`,
      );
      expect(elemento().querySelector('a[data-titulo]')!.getAttribute('aria-label')).toBe(completo);
      expect(boton('Hecho')!.getAttribute('aria-label')).toBe(`Hecho: ${completo}`);
      expect(boton('Posponer')!.getAttribute('aria-label')).toBe(`Posponer: ${completo}`);
      expect(botonMas().getAttribute('aria-label')).toBe(`Más acciones: ${completo}`);
    });

    it('en la ficha (sin conCandidatura) el título sale entero: allí no hay línea de empresa', async () => {
      await crear(DE_CANDIDATURA);

      expect(elemento().textContent).toContain('Entrevista en Consultora Ejemplo');
    });

    describe('enlace a la oferta', () => {
      const CON_OFERTA = {
        ...DE_CANDIDATURA,
        candidatura: { ...DE_CANDIDATURA.candidatura!, enlace: 'https://ejemplo.test/oferta/1' },
      };

      function oferta(): HTMLAnchorElement | null {
        return elemento().querySelector<HTMLAnchorElement>('a[data-oferta]');
      }

      it('si la candidatura tiene URL, un icono la abre en otra pestaña', async () => {
        await crear(CON_OFERTA, { conCandidatura: true });

        expect(oferta()!.getAttribute('href')).toBe('https://ejemplo.test/oferta/1');
        expect(oferta()!.getAttribute('target')).toBe('_blank');
        expect(oferta()!.getAttribute('rel')).toBe('noopener noreferrer');
        expect(oferta()!.getAttribute('aria-label')).toBe(
          'Abrir la oferta de Desarrollador .NET en otra pestaña',
        );
        expect(oferta()!.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
      });

      it('sin URL no hay icono, pero en Hoy queda su hueco para que las acciones vayan en columna', async () => {
        await crear(DE_CANDIDATURA, { conCandidatura: true });

        expect(oferta()).toBeNull();
        const hueco = elemento().querySelector('[data-hueco-oferta]')!;
        expect(hueco.getAttribute('aria-hidden')).toBe('true');
        // Solo en la tarjeta ancha: en la estrecha los iconos van al final de la fila
        expect(hueco.classList).toContain('hidden');
        expect(hueco.classList).toContain('@lg:block');
      });

      it('con icono, o en la ficha, no hay hueco', async () => {
        await crear(CON_OFERTA, { conCandidatura: true });
        expect(elemento().querySelector('[data-hueco-oferta]')).toBeNull();

        TestBed.resetTestingModule();
        await crear(DE_CANDIDATURA);
        expect(elemento().querySelector('[data-hueco-oferta]')).toBeNull();
      });

      it('en la ficha (sin conCandidatura) tampoco', async () => {
        await crear(CON_OFERTA);

        expect(oferta()).toBeNull();
      });

      it('pulsarlo no elige la tarjeta: sigue abriendo solo la oferta', async () => {
        await crear(CON_OFERTA, { conCandidatura: true, seleccionable: true });
        const elegidas = emitidos('seleccionar');
        // jsdom no navega, pero avisa por consola si no se cancela
        oferta()!.addEventListener('click', (evento) => evento.preventDefault());

        oferta()!.click();

        expect(elegidas).toEqual([]);
      });

      it('también sale en los ya hechos', async () => {
        await crear({ ...CON_OFERTA, hecho: true }, { conCandidatura: true });

        expect(oferta()).not.toBeNull();
      });
    });

    it('uno suelto no lleva enlace', async () => {
      await crear(crearRecordatorioAgenda(), { conCandidatura: true });

      expect(elemento().querySelector('a')).toBeNull();
    });

    it('en la ficha (sin conCandidatura) tampoco: ya se está en ella', async () => {
      await crear(DE_CANDIDATURA);

      expect(elemento().querySelector('a')).toBeNull();
      expect(elemento().querySelector('[data-estado]')).toBeNull();
    });
  });

  describe('seguimiento que se cerró solo al mover la candidatura', () => {
    const CERRADO = {
      ...DE_CANDIDATURA,
      tipo: 'Seguimiento' as const,
      hecho: true,
      fechaHecho: '2026-11-13T09:00:00Z',
      cerradoAlPasarA: 'Entrevista' as const,
    };

    it('lo dice en una línea pequeña', async () => {
      await crear(CERRADO, { conCandidatura: true });

      expect(elemento().querySelector('[data-cerrado-solo]')!.textContent!.trim()).toBe(
        'Se cerró solo al pasar a Entrevista',
      );
    });

    it('al pasar a Oferta dice Oferta', async () => {
      await crear({ ...CERRADO, cerradoAlPasarA: 'Oferta' }, { conCandidatura: true });

      expect(elemento().querySelector('[data-cerrado-solo]')!.textContent).toContain(
        'al pasar a Oferta',
      );
    });

    it('el marcado a mano, o sin el dato, no dice nada', async () => {
      await crear({ ...CERRADO, cerradoAlPasarA: null }, { conCandidatura: true });
      expect(elemento().querySelector('[data-cerrado-solo]')).toBeNull();

      TestBed.resetTestingModule();
      await crear(crearRecordatorio({ hecho: true }));
      expect(elemento().querySelector('[data-cerrado-solo]')).toBeNull();
    });
  });

  describe('«Hecho»', () => {
    it('va con borde, sin acento', async () => {
      await crear(crearRecordatorio());

      expect(boton('Hecho')!.hasAttribute('mat-stroked-button')).toBe(true);
      expect(boton('Hecho')!.classList.contains('boton-acento')).toBe(false);
      expect(elemento().querySelector('[mat-flat-button]')).toBeNull();
    });

    it('la destacada lo lleva con el contorno de acento, nunca relleno', async () => {
      await crear(crearRecordatorio(), { destacada: true, conPosponer: true });

      expect(boton('Hecho')!.hasAttribute('mat-stroked-button')).toBe(true);
      expect(boton('Hecho')!.classList.contains('boton-acento')).toBe(true);
      // El acento es solo de «Hecho»
      expect(boton('Posponer')!.classList.contains('boton-acento')).toBe(false);
      expect(elemento().querySelector('[mat-flat-button]')).toBeNull();
    });

    it('avisa con un toque y su aria-label lleva el título', async () => {
      await crear(crearRecordatorio());
      const hechos = emitidos('hecho');

      boton('Hecho')!.click();

      expect(hechos).toHaveLength(1);
      expect(boton('Hecho')!.getAttribute('aria-label')).toBe('Hecho: Publicar en LinkedIn');
    });

    it('en los hechos pasa a «Pendiente otra vez», sin posponer', async () => {
      await crear(crearRecordatorio({ hecho: true }), { destacada: true, conPosponer: true });
      const pendientes = emitidos('pendiente');

      expect(boton('Hecho')).toBeUndefined();
      expect(boton('Posponer')).toBeUndefined();
      expect(elemento().querySelector('[mat-flat-button]')).toBeNull();
      boton('Pendiente otra vez')!.click();

      expect(pendientes).toHaveLength(1);
    });

    it('enfocarAccion lleva el foco a «Hecho»', async () => {
      await crear(crearRecordatorio());

      expect(fixture.componentInstance.enfocarAccion()).toBe(true);

      expect(document.activeElement).toBe(boton('Hecho'));
    });
  });

  describe('«Posponer»', () => {
    it('solo sale con conPosponer', async () => {
      await crear(crearRecordatorio());

      expect(boton('Posponer')).toBeUndefined();
    });

    function textosDelMenu(): string[] {
      return opcionesDelMenu().map((o) => o.textContent!.replace(/\s+/g, ' ').trim());
    }

    it('ofrece mañana, 3 días y una semana desde hoy, y cambiar la fecha', async () => {
      await crear(crearRecordatorio({ fechaHora: '2026-11-03T08:00:00Z' }), { conPosponer: true });
      // Un martes: mañana es día de diario
      fixture.componentRef.setInput('hoy', '2026-11-10');
      await fixture.whenStable();

      await abrirMenu(boton('Posponer')!);

      expect(textosDelMenu()).toEqual([
        'Mañana mié 11 nov',
        'Dentro de 3 días vie 13 nov',
        'Dentro de una semana mar 17 nov',
        'Cambiar fecha u hora…',
      ]);
    });

    it('un viernes ofrece «El lunes» con su fecha en lugar de «Mañana»', async () => {
      // Hoy es viernes 13: mañana es sábado
      await crear(crearRecordatorio({ fechaHora: '2026-11-10T08:00:00Z' }), { conPosponer: true });

      await abrirMenu(boton('Posponer')!);

      expect(textosDelMenu()).toEqual([
        'El lunes lun 16 nov',
        'Dentro de una semana vie 20 nov',
        'Cambiar fecha u hora…',
      ]);
    });

    it('la primera opción manda el día contado desde hoy, no desde el del recordatorio vencido', async () => {
      await crear(crearRecordatorio({ fechaHora: '2026-11-10T08:00:00Z' }), { conPosponer: true });
      const dias = emitidos('posponer');

      await abrirMenu(boton('Posponer')!);
      opcionesDelMenu()[0].click();

      expect(dias).toEqual(['2026-11-16']);
    });

    it('lo que todavía no toca se pospone desde su día, para no adelantarlo', async () => {
      // Del martes 17, con hoy en viernes 13
      await crear(crearRecordatorio({ fechaHora: '2026-11-17T08:00:00Z' }), { conPosponer: true });
      const dias = emitidos('posponer');

      await abrirMenu(boton('Posponer')!);

      expect(textosDelMenu()).toEqual([
        'Un día después mié 18 nov',
        '3 días después vie 20 nov',
        'Una semana después mar 24 nov',
        'Cambiar fecha u hora…',
      ]);
      opcionesDelMenu()[0].click();
      expect(dias).toEqual(['2026-11-18']);
    });

    it('«Cambiar fecha u hora…» pide editar', async () => {
      await crear(crearRecordatorio(), { conPosponer: true });
      const ediciones = emitidos('editar');

      await abrirMenu(boton('Posponer')!);
      opcionesDelMenu().at(-1)!.click();

      expect(ediciones).toHaveLength(1);
    });
  });

  describe('«⋮»', () => {
    it('su aria-label lleva el título', async () => {
      await crear(DE_CANDIDATURA);

      expect(botonMas().getAttribute('aria-label')).toBe(
        'Más acciones: Entrevista en Consultora Ejemplo',
      );
    });

    it('deja editar y borrar', async () => {
      await crear(crearRecordatorio(), { conPosponer: true });
      const ediciones = emitidos('editar');
      const borrados = emitidos('borrar');

      await abrirMenu(botonMas());
      expect(opcionesDelMenu().map((o) => o.textContent!.trim())).toEqual(['Editar', 'Borrar']);
      opcionesDelMenu()[0].click();
      await fixture.whenStable();
      await abrirMenu(botonMas());
      opcionesDelMenu()[1].click();

      expect(ediciones).toHaveLength(1);
      expect(borrados).toHaveLength(1);
    });

    it('sin «Posponer», la opción de editar dice que también cambia la fecha', async () => {
      await crear(crearRecordatorio());

      await abrirMenu(botonMas());

      expect(opcionesDelMenu()[0].textContent).toContain('Editar o cambiar la fecha…');
    });

    it('también sale en los hechos', async () => {
      await crear(crearRecordatorio({ hecho: true }));

      expect(botonMas()).not.toBeNull();
    });
  });

  it('los botones de texto miden 48 px de alto como mínimo', async () => {
    await crear(crearRecordatorio(), { conPosponer: true });

    expect(boton('Hecho')!.classList.contains('min-h-12')).toBe(true);
    expect(boton('Posponer')!.classList.contains('min-h-12')).toBe(true);
  });
  describe('seleccionable (con panel de detalle al lado)', () => {
    function titulo(): HTMLButtonElement {
      return elemento().querySelector<HTMLButtonElement>('button[data-titulo]')!;
    }

    it('sin serlo, un clic en la tarjeta no avisa de nada', async () => {
      await crear(DE_CANDIDATURA, { conCandidatura: true });
      const elegidas = emitidos('seleccionar');

      elemento().querySelector('article')!.click();

      expect(elegidas).toEqual([]);
      expect(elemento().querySelector('button[data-titulo]')).toBeNull();
    });

    it('el título es un botón con el título en su nombre, en vez del enlace a la ficha', async () => {
      await crear(DE_CANDIDATURA, { conCandidatura: true, seleccionable: true });

      expect(elemento().querySelector('a')).toBeNull();
      // Se ve el puesto; el nombre del botón lleva el título entero
      expect(titulo().textContent?.trim()).toBe('Desarrollador .NET');
      expect(titulo().getAttribute('aria-label')).toBe(
        'Ver detalle: Entrevista en Consultora Ejemplo, Desarrollador .NET',
      );
      expect(titulo().getAttribute('aria-pressed')).toBe('false');
      // 48 px de zona de toque, como el enlace
      expect(titulo().classList.contains('after:-inset-y-3')).toBe(true);
      // La empresa y el estado siguen a la vista
      expect(elemento().querySelector('[data-estado="Entrevista"]')).not.toBeNull();
    });

    it('lo suelto también lleva el botón', async () => {
      await crear(crearRecordatorio(), { seleccionable: true });

      expect(titulo().textContent).toContain('Publicar en LinkedIn');
    });

    it('el botón del título avisa una sola vez', async () => {
      await crear(crearRecordatorio(), { seleccionable: true });
      const elegidas = emitidos('seleccionar');

      titulo().click();

      expect(elegidas).toHaveLength(1);
    });

    it('un clic en la tarjeta, fuera de los botones, también avisa', async () => {
      await crear(crearRecordatorio({ notas: 'Post de Angular' }), { seleccionable: true });
      const elegidas = emitidos('seleccionar');

      elemento().querySelector('time')!.click();

      expect(elegidas).toHaveLength(1);
    });

    it('«Hecho» y «⋮» hacen lo suyo sin elegir la tarjeta', async () => {
      await crear(crearRecordatorio(), { seleccionable: true });
      const elegidas = emitidos('seleccionar');
      const hechos = emitidos('hecho');

      boton('Hecho')!.click();
      await abrirMenu(botonMas());

      expect(hechos).toHaveLength(1);
      expect(elegidas).toEqual([]);
    });

    it('la elegida lo dice en el botón y lleva la marca', async () => {
      await crear(crearRecordatorio(), { seleccionable: true, seleccionada: true });

      expect(titulo().getAttribute('aria-pressed')).toBe('true');
      expect(elemento().querySelector('article')!.hasAttribute('data-seleccionada')).toBe(true);
    });

    it('enfocarTitulo lleva el foco al botón', async () => {
      await crear(crearRecordatorio(), { seleccionable: true });

      expect(fixture.componentInstance.enfocarTitulo()).toBe(true);
      expect(document.activeElement).toBe(titulo());
    });

    it('enfocarTitulo devuelve false si el título es solo texto', async () => {
      await crear(crearRecordatorio());

      expect(fixture.componentInstance.enfocarTitulo()).toBe(false);
    });
  });
});
