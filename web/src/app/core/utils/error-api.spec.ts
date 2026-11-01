import { HttpErrorResponse } from '@angular/common/http';
import { convertirErrorApi } from './error-api';

function respuesta(status: number, error: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status, error });
}

describe('convertirErrorApi', () => {
  it('usa el detail del ProblemDetails y los errores por campo de un 400', () => {
    const error = convertirErrorApi(
      respuesta(400, {
        title: 'Hay datos que no son válidos.',
        detail: 'La empresa es obligatoria. El puesto es obligatorio.',
        errors: { empresa: ['La empresa es obligatoria.'], puesto: ['El puesto es obligatorio.'] },
      }),
    );

    expect(error).toEqual({
      estado: 400,
      mensaje: 'La empresa es obligatoria. El puesto es obligatorio.',
      errores: { empresa: ['La empresa es obligatoria.'], puesto: ['El puesto es obligatorio.'] },
    });
  });

  it('usa el detail de un 409', () => {
    const error = convertirErrorApi(
      respuesta(409, {
        title: 'No se puede hacer esa operación',
        detail: 'No se puede volver de «Enviada» a «Detectada»: los estados solo avanzan.',
      }),
    );

    expect(error.mensaje).toBe(
      'No se puede volver de «Enviada» a «Detectada»: los estados solo avanzan.',
    );
    expect(error.errores).toEqual({});
  });

  it('usa el title si no hay detail', () => {
    expect(convertirErrorApi(respuesta(404, { title: 'No se ha encontrado.' })).mensaje).toBe(
      'No se ha encontrado.',
    );
  });

  it('da un mensaje genérico si no hay ProblemDetails', () => {
    expect(convertirErrorApi(respuesta(404, null)).mensaje).toBe('No se ha encontrado.');
    expect(convertirErrorApi(respuesta(500, 'Error')).mensaje).toBe(
      'Ha ocurrido un error inesperado (código 500). Vuelve a probar en un momento.',
    );
  });

  it('en un 500 con título añade qué hacer', () => {
    const error = convertirErrorApi(respuesta(500, { title: 'Ha ocurrido un error inesperado.' }));

    expect(error.mensaje).toBe(
      'Ha ocurrido un error inesperado (código 500). Vuelve a probar en un momento.',
    );
  });

  it('trata un 502-504 sin ProblemDetails (proxy sin API detrás) como falta de conexión', () => {
    expect(convertirErrorApi(respuesta(502, 'Bad Gateway')).mensaje).toContain(
      'No se puede conectar con RadarEmpleo',
    );
    expect(convertirErrorApi(respuesta(504, null)).mensaje).toContain(
      'No se puede conectar con RadarEmpleo',
    );
  });

  it('avisa de que no hay conexión si no hubo respuesta', () => {
    const error = convertirErrorApi(respuesta(0, new ProgressEvent('error')));

    expect(error.estado).toBe(0);
    expect(error.mensaje).toContain('No se puede conectar con RadarEmpleo');
  });

  it('ignora errores por campo que no son listas de textos', () => {
    const error = convertirErrorApi(respuesta(400, { errors: { a: 'x', b: [1, 'Mal.'] } }));

    expect(error.errores).toEqual({ b: ['Mal.'] });
  });
});
