import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { EstadoApi } from '../../../core/components/estado-api/estado-api';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';

@Component({
  imports: [CabeceraPagina, EstadoApi, RouterLink, MatButtonModule],
  selector: 'app-hoy-page',
  templateUrl: './hoy-page.component.html',
})
export default class HoyPageComponent {}
