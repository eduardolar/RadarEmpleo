import { AbstractControl, FormGroup, ValidationErrors, ValidatorFn } from '@angular/forms';

/** Cómo se nombra un campo en los mensajes: «La empresa», «El puesto»... */
export interface CampoFormulario {
  nombre: string;
  /** Mensaje si falta (si no, «<nombre> es obligatorio.») */
  obligatorio?: string;
  /** Mensaje si el valor no vale (si no, «<nombre> no es válido.») */
  invalido?: string;
}

/** Como Validators.required, pero un texto con solo espacios también cuenta como vacío (igual que la API) */
export const obligatorio: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const valor: unknown = control.value;
  const vacio =
    valor === null || valor === undefined || (typeof valor === 'string' && valor.trim() === '');
  return vacio ? { obligatorio: true } : null;
};

/**
 * Mensaje para el mat-error de un campo, con el mismo texto que daría la API. Primero el error
 * que haya devuelto el servidor; luego los de cliente.
 */
export function mensajeError(errores: ValidationErrors | null, campo: CampoFormulario): string {
  if (!errores) {
    return '';
  }
  if (typeof errores['servidor'] === 'string') {
    return errores['servidor'];
  }
  if (errores['obligatorio'] || errores['required']) {
    return campo.obligatorio ?? `${campo.nombre} es obligatorio.`;
  }
  if (errores['maxlength']) {
    const maximo = (errores['maxlength'] as { requiredLength: number }).requiredLength;
    return `${campo.nombre} no puede tener más de ${maximo} caracteres.`;
  }
  return campo.invalido ?? `${campo.nombre} no es válido.`;
}

/**
 * Pone en cada control el error que devolvió la API para su campo (las claves de `errors` son los
 * nombres del JSON, que coinciden con los de los controles) y lo marca como tocado para que se vea.
 * El error se quita solo en cuanto el usuario cambia el valor. Devuelve los mensajes de los campos
 * que no están en el formulario, para enseñarlos aparte.
 */
export function aplicarErroresServidor(
  formulario: FormGroup,
  errores: Record<string, string[]>,
): string[] {
  const sueltos: string[] = [];
  for (const [campo, mensajes] of Object.entries(errores)) {
    const control = formulario.get(campo);
    if (control && mensajes.length > 0) {
      control.setErrors({ ...control.errors, servidor: mensajes.join(' ') });
      control.markAsTouched();
    } else {
      sueltos.push(...mensajes);
    }
  }
  return sueltos;
}

/** Quita los espacios de los extremos y convierte un texto vacío en null (como hace la API) */
export function textoOpcional(valor: string | null | undefined): string | null {
  const limpio = valor?.trim() ?? '';
  return limpio === '' ? null : limpio;
}
