import { Component, computed, inject } from '@angular/core';
import { SistemaService } from '../../../core/services/sistema.service';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { TarjetaEnlace } from '../../components/tarjeta-enlace/tarjeta-enlace';

@Component({
  imports: [CabeceraPagina, TarjetaEnlace],
  selector: 'app-mas-page',
  templateUrl: './mas-page.component.html',
})
export default class MasPageComponent {
  private readonly sistema = inject(SistemaService);

  // Sin agentes (el móvil, la demo, apagados) la tarjeta no sale: llevaría a una página sin nada que ver
  protected readonly conAgentes = computed(() => this.sistema.info()?.agentesDisponibles === true);

  constructor() {
    this.sistema.cargarInfoSiFalta();
  }
}
