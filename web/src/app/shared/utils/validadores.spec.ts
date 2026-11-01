import { FormControl, FormGroup, Validators } from '@angular/forms';
import { aplicarErroresServidor, mensajeError, obligatorio, textoOpcional } from './validadores';

const EMPRESA = { nombre: 'La empresa', obligatorio: 'La empresa es obligatoria.' };

describe('obligatorio', () => {
  it('trata como vacío un texto con solo espacios', () => {
    expect(obligatorio(new FormControl('   '))).toEqual({ obligatorio: true });
    expect(obligatorio(new FormControl(null))).toEqual({ obligatorio: true });
    expect(obligatorio(new FormControl(' Acme '))).toBeNull();
  });
});

describe('mensajeError', () => {
  it('da los mismos mensajes que la API', () => {
    expect(mensajeError({ obligatorio: true }, EMPRESA)).toBe('La empresa es obligatoria.');
    expect(mensajeError({ maxlength: { requiredLength: 200, actualLength: 201 } }, EMPRESA)).toBe(
      'La empresa no puede tener más de 200 caracteres.',
    );
    expect(mensajeError({ required: true }, { nombre: 'El puesto' })).toBe(
      'El puesto es obligatorio.',
    );
  });

  it('usa el mensaje propio del campo para un valor no válido, si lo tiene', () => {
    expect(mensajeError({ cualquiera: true }, { nombre: 'El dato' })).toBe('El dato no es válido.');
    expect(mensajeError({ cualquiera: true }, { nombre: 'El dato', invalido: 'Así no.' })).toBe(
      'Así no.',
    );
  });

  it('da prioridad al error que devolvió el servidor', () => {
    expect(mensajeError({ servidor: 'Ya existe.', obligatorio: true }, EMPRESA)).toBe('Ya existe.');
  });

  it('devuelve una cadena vacía si no hay errores', () => {
    expect(mensajeError(null, EMPRESA)).toBe('');
  });
});

describe('aplicarErroresServidor', () => {
  it('pone el error en su campo, lo marca como tocado y devuelve los que no tienen campo', () => {
    const formulario = new FormGroup({
      empresa: new FormControl('Acme', { nonNullable: true }),
      puesto: new FormControl('', { nonNullable: true }),
    });

    const sueltos = aplicarErroresServidor(formulario, {
      empresa: ['La empresa no puede tener más de 200 caracteres.'],
      cuerpo: ['Falta el cuerpo de la petición o no es un JSON válido.'],
    });

    const empresa = formulario.controls.empresa;
    expect(empresa.errors).toEqual({
      servidor: 'La empresa no puede tener más de 200 caracteres.',
    });
    expect(empresa.touched).toBe(true);
    expect(sueltos).toEqual(['Falta el cuerpo de la petición o no es un JSON válido.']);
  });

  it('el error del servidor desaparece en cuanto se cambia el valor', () => {
    const control = new FormControl('x', { validators: [Validators.maxLength(10)] });
    const formulario = new FormGroup({ empresa: control });
    aplicarErroresServidor(formulario, { empresa: ['Mal.'] });

    control.setValue('y');

    expect(control.errors).toBeNull();
  });
});

describe('textoOpcional', () => {
  it('recorta y convierte el texto vacío en null', () => {
    expect(textoOpcional('  Madrid ')).toBe('Madrid');
    expect(textoOpcional('   ')).toBeNull();
    expect(textoOpcional(null)).toBeNull();
  });
});
