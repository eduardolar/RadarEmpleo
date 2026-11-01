import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CandidaturaDetalle, GuardarCandidatura } from '../../interfaces/candidatura.interface';
import { crearDetalle } from '../../utils/candidatura-de-prueba';
import { FormularioCandidatura } from './formulario-candidatura';

describe('FormularioCandidatura', () => {
  let fixture: ComponentFixture<FormularioCandidatura>;
  let enviado: GuardarCandidatura | undefined;
  let envios: number;
  let http: HttpTestingController;

  const ENLACE_INFOJOBS = 'https://www.infojobs.net/madrid/desarrollador-frontend/of-i12ab';

  async function crear(
    entradas: { rapido?: boolean; inicial?: CandidaturaDetalle } = {},
  ): Promise<void> {
    enviado = undefined;
    envios = 0;
    TestBed.configureTestingModule({
      imports: [FormularioCandidatura],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FormularioCandidatura);
    fixture.componentRef.setInput('rapido', entradas.rapido ?? false);
    fixture.componentRef.setInput('inicial', entradas.inicial ?? null);
    fixture.componentInstance.guardar.subscribe((datos) => {
      enviado = datos;
      envios++;
    });
    await fixture.whenStable();
  }

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

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

  async function enviar(): Promise<void> {
    elemento().querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await fixture.whenStable();
  }

  function botonEnviar(): HTMLButtonElement {
    return elemento().querySelector<HTMLButtonElement>('button[type="submit"]')!;
  }

  describe('enlace', () => {
    it('sin esquema le pone https:// y detecta el portal igual', async () => {
      await crear({ rapido: true });
      await escribir('enlace', 'www.linkedin.com/jobs/1');
      await escribir('empresa', 'Acme');
      await escribir('puesto', 'Dev');

      expect(elemento().textContent).toContain('Se apuntará el portal: LinkedIn');
      await enviar();

      expect(enviado).toEqual({
        empresa: 'Acme',
        puesto: 'Dev',
        enlace: 'https://www.linkedin.com/jobs/1',
        portal: 'LinkedIn',
      });
    });

    it('sin esquema también saca el puesto', async () => {
      await crear({ rapido: true });

      await escribir('enlace', 'www.infojobs.net/madrid/desarrollador-frontend/of-i12ab');

      expect(campo('puesto').value).toBe('Desarrollador frontend');
    });

    it('rechaza otro esquema con un mensaje en español y no envía nada', async () => {
      await crear({ rapido: true });
      await escribir('enlace', 'mailto:rrhh@acme.com');
      await escribir('empresa', 'Acme');
      await escribir('puesto', 'Dev');

      await enviar();

      expect(enviado).toBeUndefined();
      expect(elemento().textContent).toContain('El enlace tiene que ser una dirección web');
    });
  });

  describe('doble envío', () => {
    it('ignora el segundo envío hecho en el mismo instante', async () => {
      await crear({ rapido: true });
      await escribir('empresa', 'Acme');
      await escribir('puesto', 'Dev');

      botonEnviar().click();
      botonEnviar().click();

      expect(envios).toBe(1);
    });

    it('tras pintarse deja volver a enviar: el bloqueo no se queda puesto', async () => {
      await crear({ rapido: true });
      await escribir('empresa', 'Acme');
      await escribir('puesto', 'Dev');

      botonEnviar().click();
      await fixture.whenStable();
      botonEnviar().click();

      expect(envios).toBe(2);
    });

    it('mientras «guardando» está activo no envía', async () => {
      await crear({ rapido: true });
      await escribir('empresa', 'Acme');
      await escribir('puesto', 'Dev');
      fixture.componentRef.setInput('guardando', true);
      await fixture.whenStable();

      botonEnviar().click();

      expect(envios).toBe(0);
    });
  });

  describe('autocompletado de empresa', () => {
    const empresas = () => http.match((r) => r.url === '/api/empresas');

    /** Se teclea letra a letra, como lo haría una persona */
    function teclear(valor: string): void {
      const entrada = campo('empresa');
      entrada.value = valor;
      entrada.dispatchEvent(new Event('input'));
    }

    it('espera a que se deje de escribir y busca una sola vez en /api/empresas', async () => {
      await crear({ rapido: true });
      vi.useFakeTimers();

      teclear('Ac');
      await vi.advanceTimersByTimeAsync(200);
      teclear('Acm');
      await vi.advanceTimersByTimeAsync(249);
      expect(empresas()).toHaveLength(0);

      await vi.advanceTimersByTimeAsync(1);

      const peticion = http.expectOne((r) => r.url === '/api/empresas');
      expect(peticion.request.params.get('texto')).toBe('Acm');
      const encontradas = [{ id: 1, nombre: 'Acme', candidaturas: 2 }];
      peticion.flush(encontradas);
      expect(fixture.componentInstance['sugerenciasEmpresa']()).toEqual(encontradas);
    });

    it('si la búsqueda falla, se queda sin sugerencias y se puede seguir escribiendo', async () => {
      await crear({ rapido: true });
      vi.useFakeTimers();

      teclear('Acm');
      await vi.advanceTimersByTimeAsync(250);
      http.expectOne((r) => r.url === '/api/empresas').error(new ProgressEvent('error'));

      expect(fixture.componentInstance['sugerenciasEmpresa']()).toEqual([]);
    });

    it('al abrir la edición no busca, pero sí cuando se cambia la empresa', async () => {
      await crear({ inicial: crearDetalle({ empresa: 'Acme' }) });
      vi.useFakeTimers();

      await vi.advanceTimersByTimeAsync(1000);
      expect(empresas()).toHaveLength(0);

      teclear('Acme Iberia');
      await vi.advanceTimersByTimeAsync(250);
      http.expectOne((r) => r.url === '/api/empresas').flush([]);
    });
  });

  describe('en el alta', () => {
    it('manda solo lo básico: la API pone los valores por defecto del resto', async () => {
      await crear({ rapido: true });
      await escribir('empresa', 'Acme');
      await escribir('puesto', 'Dev');

      await enviar();

      expect(enviado).toEqual({ empresa: 'Acme', puesto: 'Dev', enlace: null });
    });

    it('el enlace de un portal conocido apunta el portal y lo dice', async () => {
      await crear({ rapido: true });

      await escribir('enlace', 'https://es.linkedin.com/jobs/view/123');

      expect(elemento().textContent).toContain('Se apuntará el portal: LinkedIn');
    });

    it('deja escrito el puesto si el enlace lo trae y avisa de que hay que revisarlo', async () => {
      await crear({ rapido: true });

      await escribir('enlace', ENLACE_INFOJOBS);

      expect(campo('puesto').value).toBe('Desarrollador frontend');
      expect(elemento().textContent).toContain('Sacado del enlace: revísalo');
    });

    it('no pisa el puesto tecleado ni al poner el enlace ni al cambiarlo', async () => {
      await crear({ rapido: true });
      await escribir('puesto', 'Frontend Angular');

      await escribir('enlace', ENLACE_INFOJOBS);

      expect(campo('puesto').value).toBe('Frontend Angular');
      expect(elemento().textContent).not.toContain('Sacado del enlace');
    });

    it('si se corrige el puesto sugerido, deja de ser una sugerencia', async () => {
      await crear({ rapido: true });
      await escribir('enlace', ENLACE_INFOJOBS);

      await escribir('puesto', 'Desarrollador Frontend Angular');
      await escribir('enlace', '');

      expect(campo('puesto').value).toBe('Desarrollador Frontend Angular');
      expect(elemento().textContent).not.toContain('Sacado del enlace');
    });

    it('si se quita el enlace, se quita también el puesto que salió de él', async () => {
      await crear({ rapido: true });
      await escribir('enlace', ENLACE_INFOJOBS);

      await escribir('enlace', '');

      expect(campo('puesto').value).toBe('');
    });
  });

  describe('al editar', () => {
    const FICHA = crearDetalle({
      puesto: 'Título bueno',
      enlace: 'https://acme.com/empleo',
      portal: 'Web propia',
    });

    it('cambiar el enlace no toca el puesto', async () => {
      await crear({ inicial: FICHA });

      await escribir('enlace', ENLACE_INFOJOBS);

      expect(campo('puesto').value).toBe('Título bueno');
    });

    it('no pisa un portal escrito a mano', async () => {
      await crear({ inicial: FICHA });

      await escribir('enlace', ENLACE_INFOJOBS);

      expect(campo('portal').value).toBe('Web propia');
    });

    it('rellenar la ficha no apaga lo que depende del enlace: el portal puesto se actualiza', async () => {
      await crear({
        inicial: crearDetalle({ enlace: 'https://www.linkedin.com/jobs/1', portal: 'LinkedIn' }),
      });

      await escribir('enlace', ENLACE_INFOJOBS);

      expect(campo('portal').value).toBe('InfoJobs');
    });

    it('al abrirse no cuenta como cambios sin guardar', async () => {
      await crear({ inicial: FICHA });

      expect(fixture.componentInstance.tieneCambios()).toBe(false);
    });
  });
});
