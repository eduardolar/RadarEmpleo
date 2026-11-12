import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';

@Component({
  imports: [CabeceraPagina, RouterLink],
  selector: 'app-mas-page',
  templateUrl: './mas-page.component.html',
})
export default class MasPageComponent {}
