import { Component, inject } from '@angular/core';
import { SistemaService } from '../../../core/services/sistema.service';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { EnlaceVolver } from '../../../shared/components/enlace-volver/enlace-volver';

/**
 * Ajustes: de momento solo enseña lo que ya es real (modo, versión y agentes). Sin botones: no se
 * promete nada que no exista todavía
 */
@Component({
  imports: [CabeceraPagina, EnlaceVolver],
  selector: 'app-ajustes-page',
  templateUrl: './ajustes-page.component.html',
})
export default class AjustesPageComponent {
  protected readonly sistemaService = inject(SistemaService);

  constructor() {
    this.sistemaService.cargarInfoSiFalta();
  }
}
