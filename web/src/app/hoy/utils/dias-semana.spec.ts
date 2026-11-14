import { crearResumenHoy } from '../../recordatorios/utils/recordatorio-de-prueba';
import { diasCalendario, siguienteFueraDeSemana } from './dias-semana';

describe('diasCalendario', () => {
  // Del lunes 9 al domingo 15 de noviembre de 2026
  const SEMANA = crearResumenHoy().semana.map((d) => ({
    ...d,
    recordatorios: { '2026-11-11': 2, '2026-11-13': 1 }[d.dia] ?? 0,
  }));

  it('da la letra y el número de cada día, de lunes a domingo', () => {
    const dias = diasCalendario(SEMANA, '2026-11-13');

    expect(dias.map((d) => d.letra).join('')).toBe('LMXJVSD');
    expect(dias.map((d) => d.numero)).toEqual(['9', '10', '11', '12', '13', '14', '15']);
  });

  it('marca solo hoy', () => {
    const dias = diasCalendario(SEMANA, '2026-11-13');

    expect(dias.filter((d) => d.esHoy).map((d) => d.dia)).toEqual(['2026-11-13']);
  });

  it('la etiqueta dice el día entero y cuántas tareas tiene', () => {
    const etiquetas = diasCalendario(SEMANA, '2026-11-13').map((d) => d.etiqueta);

    expect(etiquetas[0]).toBe('lunes 9 de noviembre: sin tareas');
    expect(etiquetas[2]).toBe('miércoles 11 de noviembre: 2 tareas');
    expect(etiquetas[4]).toBe('viernes 13 de noviembre, hoy: 1 tarea');
  });

  it('una semana con cambio de mes lleva el mes de cada día', () => {
    const semana = ['2026-11-30', '2026-12-01'].map((dia) => ({ dia, recordatorios: 0 }));

    const dias = diasCalendario(semana, '2026-12-01');

    expect(dias.map((d) => d.numero)).toEqual(['30', '1']);
    expect(dias[0].etiqueta).toBe('lunes 30 de noviembre: sin tareas');
    expect(dias[1].etiqueta).toBe('martes 1 de diciembre, hoy: sin tareas');
  });
});

describe('siguienteFueraDeSemana', () => {
  // Del lunes 9 al domingo 15 de noviembre de 2026
  function semana(pendientes: Record<string, number>): { dia: string; recordatorios: number }[] {
    return crearResumenHoy().semana.map((d) => ({ ...d, recordatorios: pendientes[d.dia] ?? 0 }));
  }

  it('si de aquí al domingo no queda nada, es el día que da la API', () => {
    expect(siguienteFueraDeSemana(semana({}), '2026-11-13', '2026-11-23')).toBe('2026-11-23');
  });

  it('lo de hoy y lo vencido de esta semana no cuentan como «lo que queda»', () => {
    const conVencidos = semana({ '2026-11-10': 1, '2026-11-13': 2 });

    expect(siguienteFueraDeSemana(conVencidos, '2026-11-13', '2026-11-23')).toBe('2026-11-23');
  });

  it('si queda algo esta semana, no hace falta: ya se ve en el calendario', () => {
    const conSabado = semana({ '2026-11-14': 1 });

    expect(siguienteFueraDeSemana(conSabado, '2026-11-13', '2026-11-23')).toBeNull();
  });

  it('sin nada más adelante, null', () => {
    expect(siguienteFueraDeSemana(semana({}), '2026-11-13', null)).toBeNull();
  });
});
