import { Component, input } from '@angular/core';

/** Título y descripción con los que empieza cada página */
@Component({
  selector: 'app-cabecera-pagina',
  templateUrl: './cabecera-pagina.html',
  host: { class: 'block' },
})
export class CabeceraPagina {
  readonly titulo = input.required<string>();
  readonly descripcion = input('');
}
