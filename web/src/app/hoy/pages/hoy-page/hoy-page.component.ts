import { Component } from '@angular/core';
import { EstadoApi } from '../../../core/components/estado-api/estado-api';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';

@Component({
  imports: [CabeceraPagina, EstadoApi],
  selector: 'app-hoy-page',
  templateUrl: './hoy-page.component.html',
})
export default class HoyPageComponent {}
