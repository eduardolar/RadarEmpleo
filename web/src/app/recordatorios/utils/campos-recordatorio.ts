import { CampoFormulario } from '../../shared/utils/validadores';

// Los mismos límites y textos que las anotaciones de GuardarRecordatorioRequest en la API: si cambian
// allí, hay que cambiarlos aquí.

export const MAXIMO_TITULO_RECORDATORIO = 200;
export const MAXIMO_NOTAS_RECORDATORIO = 2000;

/** La hora de un recordatorio si no se pone otra (la misma que usa la API) */
export const HORA_POR_DEFECTO = '09:00';

/** La noche de marzo en que el reloj salta de las 02:00 a las 03:00 (ReglasRecordatorio en la API) */
export const MENSAJE_HORA_INEXISTENTE =
  'Esa hora no existe ese día: el reloj se adelanta de 02:00 a 03:00.';

export const CAMPOS_RECORDATORIO = {
  tipo: { nombre: 'El tipo', obligatorio: 'Elige qué tipo de recordatorio es.' },
  titulo: { nombre: 'El título', obligatorio: 'El título es obligatorio.' },
  dia: { nombre: 'El día', obligatorio: 'El día es obligatorio.' },
  // Solo es obligatoria en una entrevista
  hora: {
    nombre: 'La hora',
    obligatorio: 'Pon la hora de la entrevista.',
    invalido: 'La hora tiene que ser como 09:30.',
  },
  notas: { nombre: 'Las notas' },
} satisfies Record<string, CampoFormulario>;
