import { crearRecordatorioAgenda } from '../../recordatorios/utils/recordatorio-de-prueba';
import {
  agruparPorDia,
  chipsTipo,
  filtrar,
  leerDia,
  leerTipo,
  semanaDe,
  textoTareas,
} from './filtros-hoy';

// Viernes 13, martes 17 (dos) y miércoles 18 de noviembre de 2026, a las 09:00 de Madrid
const POST = crearRecordatorioAgenda({ id: 1 });
const SEGUIMIENTO = crearRecordatorioAgenda({
  id: 2,
  tipo: 'Seguimiento',
  fechaHora: '2026-11-17T08:00:00Z',
});
const ENTREVISTA = crearRecordatorioAgenda({
  id: 3,
  tipo: 'Entrevista',
  fechaHora: '2026-11-17T15:00:00Z',
});
const OTRO_SEGUIMIENTO = crearRecordatorioAgenda({
  id: 4,
  tipo: 'Seguimiento',
  fechaHora: '2026-11-18T08:00:00Z',
});
const TODOS = [POST, SEGUIMIENTO, ENTREVISTA, OTRO_SEGUIMIENTO];

describe('textoTareas', () => {
  it('va en singular con una', () => {
    expect(textoTareas(1)).toBe('1 tarea');
    expect(textoTareas(0)).toBe('0 tareas');
    expect(textoTareas(5)).toBe('5 tareas');
  });
});

describe('leerTipo', () => {
  it('reconoce el tipo sin mirar mayúsculas', () => {
    expect(leerTipo('Entrevista')).toBe('Entrevista');
    expect(leerTipo('entrevista')).toBe('Entrevista');
    expect(leerTipo('publicacionlinkedin')).toBe('PublicacionLinkedIn');
  });

  it('lo que no es un tipo, o nada, es null', () => {
    expect(leerTipo('Cumple')).toBeNull();
    expect(leerTipo('')).toBeNull();
    expect(leerTipo(null)).toBeNull();
  });
});

describe('leerDia', () => {
  it('admite un día del calendario', () => {
    expect(leerDia('2026-11-18')).toBe('2026-11-18');
  });

  it.each(['18/11/2026', '2026-11-1', '2026-02-31', '2026-13-01', 'hoy', ''])(
    '«%s» no es un día',
    (texto) => {
      expect(leerDia(texto)).toBeNull();
    },
  );

  it('sin parámetro, null', () => {
    expect(leerDia(null)).toBeNull();
  });
});

describe('filtrar', () => {
  it('sin filtro devuelve la misma lista', () => {
    expect(filtrar(TODOS, { tipo: null, dia: null })).toBe(TODOS);
  });

  it('por tipo', () => {
    expect(filtrar(TODOS, { tipo: 'Seguimiento', dia: null })).toEqual([
      SEGUIMIENTO,
      OTRO_SEGUIMIENTO,
    ]);
  });

  it('por día', () => {
    expect(filtrar(TODOS, { tipo: null, dia: '2026-11-17' })).toEqual([SEGUIMIENTO, ENTREVISTA]);
  });

  it('por tipo y día a la vez', () => {
    expect(filtrar(TODOS, { tipo: 'Seguimiento', dia: '2026-11-17' })).toEqual([SEGUIMIENTO]);
    expect(filtrar(TODOS, { tipo: 'Entrevista', dia: '2026-11-18' })).toEqual([]);
  });

  it('el día es el de Madrid: las 23:30Z del 17 ya son del 18', () => {
    const deMadrugada = crearRecordatorioAgenda({ id: 9, fechaHora: '2026-11-17T23:30:00Z' });

    expect(filtrar([deMadrugada], { tipo: null, dia: '2026-11-18' })).toEqual([deMadrugada]);
    expect(filtrar([deMadrugada], { tipo: null, dia: '2026-11-17' })).toEqual([]);
  });
});

describe('semanaDe', () => {
  it('va de lunes a domingo, también si el día es lunes o domingo', () => {
    const semana = { desde: '2026-11-16', hasta: '2026-11-22' };
    expect(semanaDe('2026-11-18')).toEqual(semana);
    expect(semanaDe('2026-11-16')).toEqual(semana);
    expect(semanaDe('2026-11-22')).toEqual(semana);
  });
});

describe('filtrar por semana', () => {
  it('deja lo de esa semana, con los dos extremos incluidos, y se suma al tipo', () => {
    const semana = { desde: '2026-11-09', hasta: '2026-11-17' };
    expect(filtrar(TODOS, { tipo: null, dia: null, semana })).toEqual([
      POST,
      SEGUIMIENTO,
      ENTREVISTA,
    ]);
    expect(filtrar(TODOS, { tipo: 'Entrevista', dia: null, semana })).toEqual([ENTREVISTA]);
  });

  it('los chips cuentan solo lo de la semana', () => {
    const semana = semanaDe('2026-11-13');
    expect(chipsTipo(TODOS, null, semana).map((chip) => chip.total)).toEqual([1, 0, 0, 1]);
  });
});

describe('chipsTipo', () => {
  it('da «Todas» y solo los tipos que tienen algo, en su orden y con su número', () => {
    expect(chipsTipo(TODOS, null)).toEqual([
      { tipo: null, texto: 'Todas', total: 4 },
      { tipo: 'Seguimiento', texto: 'Seguimientos', total: 2 },
      { tipo: 'Entrevista', texto: 'Entrevistas', total: 1 },
      { tipo: 'PublicacionLinkedIn', texto: 'LinkedIn', total: 1 },
    ]);
  });

  it('sin nada pendiente solo queda «Todas»', () => {
    expect(chipsTipo([], null)).toEqual([{ tipo: null, texto: 'Todas', total: 0 }]);
  });

  it('mirando un día, los números son los de ese día y los chips no cambian', () => {
    expect(chipsTipo(TODOS, '2026-11-17')).toEqual([
      { tipo: null, texto: 'Todas', total: 2 },
      { tipo: 'Seguimiento', texto: 'Seguimientos', total: 1 },
      { tipo: 'Entrevista', texto: 'Entrevistas', total: 1 },
      { tipo: 'PublicacionLinkedIn', texto: 'LinkedIn', total: 0 },
    ]);
  });
});

describe('agruparPorDia', () => {
  it('junta los de un mismo día bajo su encabezado, sin cambiar el orden', () => {
    const grupos = agruparPorDia([SEGUIMIENTO, ENTREVISTA, OTRO_SEGUIMIENTO]);

    expect(grupos).toEqual([
      { dia: '2026-11-17', titulo: 'Martes, 17 nov', recordatorios: [SEGUIMIENTO, ENTREVISTA] },
      { dia: '2026-11-18', titulo: 'Miércoles, 18 nov', recordatorios: [OTRO_SEGUIMIENTO] },
    ]);
  });

  it('sin nada no da grupos', () => {
    expect(agruparPorDia([])).toEqual([]);
  });
});
