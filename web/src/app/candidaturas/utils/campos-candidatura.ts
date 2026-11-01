import { CampoFormulario } from '../../shared/utils/validadores';

// Los mismos límites y textos que las anotaciones de GuardarCandidaturaRequest, CambiarEstadoRequest
// y CrearNotaRequest en la API: si cambian allí, hay que cambiarlos aquí.

export const MAXIMOS_CANDIDATURA = {
  empresa: 200,
  puesto: 300,
  enlace: 2000,
  portal: 100,
  modalidadDetalle: 300,
  ubicacion: 200,
  pega: 2000,
  salario: 200,
} as const;

export const MAXIMO_COMENTARIO = 1000;
export const MAXIMO_MOTIVO_TEXTO = 1000;
export const MAXIMO_NOTA = 5000;

export const CAMPOS_CANDIDATURA = {
  empresa: { nombre: 'La empresa', obligatorio: 'La empresa es obligatoria.' },
  puesto: { nombre: 'El puesto', obligatorio: 'El puesto es obligatorio.' },
  enlace: { nombre: 'El enlace', invalido: 'El enlace tiene que ser una dirección web.' },
  portal: { nombre: 'El portal' },
  modalidad: { nombre: 'La modalidad' },
  modalidadDetalle: { nombre: 'El detalle de la modalidad' },
  ubicacion: { nombre: 'La ubicación' },
  encaje: { nombre: 'El encaje' },
  pega: { nombre: 'La pega' },
  salario: { nombre: 'El salario' },
  analisis: { nombre: 'El análisis' },
} satisfies Record<string, CampoFormulario>;

export const CAMPO_COMENTARIO: CampoFormulario = { nombre: 'El comentario' };
export const CAMPO_MOTIVO: CampoFormulario = {
  nombre: 'El motivo',
  obligatorio: 'Para descartar una candidatura hay que indicar el motivo.',
};
export const CAMPO_MOTIVO_TEXTO: CampoFormulario = { nombre: 'El texto del motivo' };
export const CAMPO_NOTA: CampoFormulario = {
  nombre: 'La nota',
  obligatorio: 'El texto de la nota es obligatorio.',
};
