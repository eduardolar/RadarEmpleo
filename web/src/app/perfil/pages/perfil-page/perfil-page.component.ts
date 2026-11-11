import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { EnlaceVolver } from '../../../shared/components/enlace-volver/enlace-volver';
import { SeccionPlegable } from '../../../shared/components/seccion-plegable/seccion-plegable';
import { TextoMarkdown } from '../../../shared/components/texto-markdown/texto-markdown';
import { FechaMadridPipe } from '../../../shared/pipes/fecha-madrid.pipe';
import { PerfilService } from '../../services/perfil.service';

@Component({
  imports: [
    CabeceraPagina,
    EnlaceVolver,
    FechaMadridPipe,
    MatButtonModule,
    MatProgressBarModule,
    SeccionPlegable,
    TextoMarkdown,
  ],
  selector: 'app-perfil-page',
  templateUrl: './perfil-page.component.html',
})
export default class PerfilPageComponent {
  private readonly perfilService = inject(PerfilService);

  protected readonly perfil = this.perfilService.perfil;
  protected readonly cargando = this.perfilService.cargando;
  protected readonly error = this.perfilService.error;

  constructor() {
    this.perfilService.cargar();
  }

  /**
   * Los filtros son lo que se viene a mirar: van abiertos y el resto plegado. Vale «Filtros de
   * búsqueda», «3. Filtros» o «Mis filtros»; si ninguna sección habla de filtros, se abre la primera
   * para que la página no salga toda cerrada.
   */
  protected readonly abiertas = computed(() => {
    const secciones = this.perfil()?.secciones ?? [];
    const filtros = secciones.flatMap((s, i) => (/filtros?/i.test(s.titulo) ? [i] : []));
    return new Set(filtros.length > 0 ? filtros : [0]);
  });

  protected reintentar(): void {
    this.perfilService.cargar();
  }
}
