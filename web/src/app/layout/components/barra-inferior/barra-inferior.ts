import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SeccionActivaService } from '../../services/seccion-activa.service';
import { OPCIONES_MENU } from '../../utils/opciones-menu';
import { IconoMenu } from '../icono-menu/icono-menu';

/** Pestañas fijas abajo en el móvil: en pantallas anchas se usa el menú lateral */
@Component({
  imports: [RouterLink, IconoMenu],
  selector: 'app-barra-inferior',
  templateUrl: './barra-inferior.html',
})
export class BarraInferior {
  protected readonly opciones = OPCIONES_MENU;
  protected readonly seccionActiva = inject(SeccionActivaService).ruta;
}
