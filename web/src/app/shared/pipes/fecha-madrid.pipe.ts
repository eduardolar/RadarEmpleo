import { Pipe, PipeTransform } from '@angular/core';
import { FormatoFecha, formatearFecha } from '../utils/fechas-madrid';

/** {{ fecha | fechaMadrid }} → «30 oct 2026»; {{ fecha | fechaMadrid: 'dia-hora' }} → «30 oct 2026, 10:05» */
@Pipe({ name: 'fechaMadrid' })
export class FechaMadridPipe implements PipeTransform {
  transform(fecha: string | Date | null | undefined, formato: FormatoFecha = 'dia'): string {
    return formatearFecha(fecha, formato);
  }
}
