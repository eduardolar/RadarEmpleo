import { Component } from '@angular/core';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { TarjetaEnlace } from '../../components/tarjeta-enlace/tarjeta-enlace';

@Component({
  imports: [CabeceraPagina, TarjetaEnlace],
  selector: 'app-mas-page',
  templateUrl: './mas-page.component.html',
})
export default class MasPageComponent {}
