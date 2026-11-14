import { Recordatorio } from '../../candidaturas/interfaces/candidatura.interface';
import { PanelHoy, RecordatorioAgenda, ResumenHoy } from '../../hoy/interfaces/panel-hoy.interface';

// Solo para los tests: datos mínimos que cada prueba completa con lo que le importa

export function crearRecordatorio(cambios: Partial<Recordatorio> = {}): Recordatorio {
  return {
    id: 1,
    candidaturaId: null,
    tipo: 'PublicacionLinkedIn',
    titulo: 'Publicar en LinkedIn',
    // Viernes 13/11/2026 a las 09:00 de Madrid
    fechaHora: '2026-11-13T08:00:00Z',
    hecho: false,
    notas: null,
    fechaHecho: null,
    ...cambios,
  };
}

export function crearRecordatorioAgenda(
  cambios: Partial<RecordatorioAgenda> = {},
): RecordatorioAgenda {
  return { ...crearRecordatorio(), candidatura: null, ...cambios };
}

/** La semana del lunes 9 al domingo 15 de noviembre de 2026, sin nada */
export function crearResumenHoy(cambios: Partial<ResumenHoy> = {}): ResumenHoy {
  return {
    candidaturasActivas: 0,
    entrevistasSemana: 0,
    respuesta: { enviadas: 0, conRespuesta: 0, porcentaje: null },
    semana: [9, 10, 11, 12, 13, 14, 15].map((dia) => ({
      dia: `2026-11-${String(dia).padStart(2, '0')}`,
      recordatorios: 0,
    })),
    siguienteDia: null,
    ...cambios,
  };
}

export function crearPanel(cambios: Partial<PanelHoy> = {}): PanelHoy {
  return {
    hoy: '2026-11-13',
    paraHoy: [],
    proximos: [],
    masAdelante: [],
    hechosHoy: [],
    sinRespuesta: [],
    sinRespuestaAntiguas: 0,
    resumen: crearResumenHoy(),
    ...cambios,
  };
}
