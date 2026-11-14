import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Estadisticas } from '../../interfaces/estadisticas.interface';
import EstadisticasPageComponent from './estadisticas-page.component';

const ESTADOS = [
  'Detectada',
  'Preparada',
  'Enviada',
  'Entrevista',
  'Oferta',
  'Descartada',
  'Cerrada',
] as const;

function estadisticas(cambios: Partial<Estadisticas> = {}): Estadisticas {
  return {
    total: 5,
    porEstado: ESTADOS.map((estado, i) => ({ estado, total: [1, 0, 2, 1, 0, 1, 0][i] })),
    enviadasPorSemana: Array.from({ length: 8 }, (_, i) => ({
      lunes: `2026-11-${String(2 + i).padStart(2, '0')}`,
      total: i === 7 ? 2 : 0,
    })),
    respuesta: { enviadas: 4, conRespuesta: 1, porcentaje: 25, diasMedios: 9, casosDias: 3 },
    motivosDescarte: [{ motivo: 'Ingles', total: 1 }],
    ...cambios,
  };
}

describe('EstadisticasPageComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function pintar(datos: Estadisticas) {
    const fixture = TestBed.createComponent(EstadisticasPageComponent);
    fixture.detectChanges();
    http.expectOne('/api/estadisticas').flush(datos);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const texto = (elemento: HTMLElement) => elemento.textContent!.replace(/\s+/g, ' ');

  it('pinta las cuatro secciones con sus cifras y la letra pequeña', () => {
    const elemento = pintar(estadisticas());

    expect([...elemento.querySelectorAll('h2')].map((h) => h.textContent!.trim())).toEqual([
      'Tus candidaturas',
      'Enviadas por semana',
      'Respuestas',
      'Por qué se descartan',
    ]);
    expect(texto(elemento)).toContain('5 en total');
    expect(texto(elemento)).toContain('2 en 8 semanas');
    expect(texto(elemento)).toContain('1 de 4 (25 %) ha llegado a entrevista u oferta.');
    expect(texto(elemento)).toContain('De media tardan 9 días en responder (calculado con 3).');
    expect(texto(elemento)).toContain('Inglés');
    expect(texto(elemento)).toContain('Solo cuenta lo que está en la app.');
    expect(elemento.querySelectorAll('[aria-label="Candidaturas por estado"] li').length).toBe(7);
  });

  it('cada sección sin datos lo dice, sin inventar ceros ni porcentajes', () => {
    const elemento = pintar(
      estadisticas({
        enviadasPorSemana: [],
        respuesta: {
          enviadas: 0,
          conRespuesta: 0,
          porcentaje: null,
          diasMedios: null,
          casosDias: 0,
        },
        motivosDescarte: [],
      }),
    );

    expect(texto(elemento).match(/Todavía no hay datos para esto\./g)?.length).toBe(3);
    expect(texto(elemento)).not.toContain('%');
  });

  it('con 0 días de media dice «el mismo día»', () => {
    const elemento = pintar(
      estadisticas({
        respuesta: { enviadas: 2, conRespuesta: 2, porcentaje: 100, diasMedios: 0, casosDias: 2 },
      }),
    );

    expect(texto(elemento)).toContain('2 de 2 (100 %) han llegado');
    expect(texto(elemento)).toContain('De media responden el mismo día (calculado con 2).');
  });

  it('sin candidaturas enseña el estado vacío con «Ir al tablero» con borde', () => {
    const elemento = pintar(estadisticas({ total: 0 }));

    expect(elemento.querySelector('h2')).toBeNull();
    const enlace = [...elemento.querySelectorAll('a')].find((a) =>
      a.textContent!.includes('Ir al tablero'),
    )!;
    expect(enlace.getAttribute('href')).toBe('/tablero');
    expect(enlace.hasAttribute('mat-stroked-button')).toBe(true);
  });

  it('no tiene ningún relleno lima: es una página sin acción principal', () => {
    const elemento = pintar(estadisticas());

    expect(elemento.querySelector('[mat-flat-button], .boton-acento')).toBeNull();
  });
});
