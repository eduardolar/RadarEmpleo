import { etiquetaRastreo, textoNuevas } from './etiqueta-rastreo';
import { crearRastreo } from './rastreo-de-prueba';

describe('etiquetaRastreo', () => {
  it('día, mes y día de la semana', () => {
    const rastreo = crearRastreo({ fecha: '2026-11-06' });

    expect(etiquetaRastreo(rastreo, [rastreo], 2026)).toBe('Rastreo del 06/11 (viernes)');
  });

  it('el día de la semana no depende de la zona del navegador', () => {
    const lunes = crearRastreo({ fecha: '2026-11-09' });
    const domingo = crearRastreo({ fecha: '2026-11-08' });

    expect(etiquetaRastreo(lunes, [lunes], 2026)).toBe('Rastreo del 09/11 (lunes)');
    expect(etiquetaRastreo(domingo, [domingo], 2026)).toBe('Rastreo del 08/11 (domingo)');
  });

  it('con tildes en miércoles y sábado', () => {
    const miercoles = crearRastreo({ fecha: '2026-10-07' });
    const sabado = crearRastreo({ fecha: '2026-10-24' });

    expect(etiquetaRastreo(miercoles, [], 2026)).toBe('Rastreo del 07/10 (miércoles)');
    expect(etiquetaRastreo(sabado, [], 2026)).toBe('Rastreo del 24/10 (sábado)');
  });

  it('de otro año, con el año', () => {
    const rastreo = crearRastreo({ fecha: '2026-12-28' });

    expect(etiquetaRastreo(rastreo, [rastreo], 2027)).toBe('Rastreo del 28/12/2026 (lunes)');
  });

  it('dos el mismo día se numeran por orden de lectura', () => {
    const manana = crearRastreo({ id: 4, fecha: '2026-10-30' });
    const tarde = crearRastreo({ id: 5, fecha: '2026-10-30' });
    const otro = crearRastreo({ id: 6, fecha: '2026-11-03' });
    // La lista llega del más reciente al más antiguo
    const todos = [otro, tarde, manana];

    expect(etiquetaRastreo(manana, todos, 2026)).toBe('Rastreo del 30/10 (viernes) · 1 de 2');
    expect(etiquetaRastreo(tarde, todos, 2026)).toBe('Rastreo del 30/10 (viernes) · 2 de 2');
    expect(etiquetaRastreo(otro, todos, 2026)).toBe('Rastreo del 03/11 (martes)');
  });
});

describe('textoNuevas', () => {
  it('singular y plural', () => {
    expect(textoNuevas(1)).toBe('1 nueva');
    expect(textoNuevas(4)).toBe('4 nuevas');
  });
});
