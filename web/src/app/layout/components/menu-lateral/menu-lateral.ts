import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SeccionActivaService } from '../../services/seccion-activa.service';
import { OPCIONES_MENU } from '../../utils/opciones-menu';
import { IconoMenu } from '../icono-menu/icono-menu';

/** Menú fijo a la izquierda en pantallas anchas: en el móvil se usa la barra de abajo */
@Component({
  imports: [RouterLink, IconoMenu],
  selector: 'app-menu-lateral',
  templateUrl: './menu-lateral.html',
})
export class MenuLateral {
  protected readonly opciones = OPCIONES_MENU;
  protected readonly seccionActiva = inject(SeccionActivaService).ruta;
}
