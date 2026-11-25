import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { AgentesService } from '../../../agentes/services/agentes.service';
import {
  estadoSinAgentes,
  reservaDelDia,
  usoDelDia,
} from '../../../agentes/utils/textos-ejecucion';
import { SistemaService } from '../../../core/services/sistema.service';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { EnlaceVolver } from '../../../shared/components/enlace-volver/enlace-volver';

/**
 * Ajustes: de momento solo enseña lo que ya es real (modo, versión y agentes). Sin botones (salvo
 * volver a pedir el estado de los agentes si falla): no se promete nada que no exista todavía
 */
@Component({
  imports: [CabeceraPagina, EnlaceVolver, MatButtonModule, RouterLink],
  selector: 'app-ajustes-page',
  templateUrl: './ajustes-page.component.html',
})
export default class AjustesPageComponent {
  protected readonly sistemaService = inject(SistemaService);
  private readonly agentes = inject(AgentesService);

  protected readonly disponibles = this.agentes.disponibles;
  protected readonly estadoAgentes = this.agentes.estado;
  protected readonly cargandoEstado = this.agentes.cargandoEstado;
  protected readonly motivoCola = this.agentes.motivoCola;

  /** /api/sistema/info dice que sí, pero /api/agentes ha dado 403: se puede volver a comprobar */
  protected readonly rechazados = computed(
    () => this.sistemaService.info()?.agentesDisponibles === true && !this.disponibles(),
  );

  /** Al reintentar, el aviso sigue a la vista (con «Reintentando…») aunque el servicio borre el error */
  private readonly reintentando = signal(false);
  protected readonly fallo = computed(
    () => this.agentes.errorEstado() !== null || (this.reintentando() && this.cargandoEstado()),
  );

  private readonly titulo = viewChild<ElementRef<HTMLElement>>('tituloAgentes');

  /** Sin agentes disponibles: la etiqueta del estado y su explicación corta */
  protected readonly sinAgentes = computed(() => {
    const info = this.sistemaService.info();
    return info ? estadoSinAgentes(info) : null;
  });

  protected readonly uso = computed(() => {
    const gasto = this.estadoAgentes()?.gasto;
    return gasto ? (usoDelDia(gasto) ?? 'No se puede calcular sin un tope diario') : null;
  });
  protected readonly reserva = computed(() => {
    const gasto = this.estadoAgentes()?.gasto;
    return gasto ? reservaDelDia(gasto) : null;
  });

  protected readonly avisoVersion = computed(() => {
    const estado = this.estadoAgentes();
    return estado?.versionClaude && estado.versionDistinta
      ? `Distinta de la probada (${estado.versionProbada}). Debería funcionar; si alguna tarea falla, prueba con la ${estado.versionProbada}.`
      : null;
  });

  private pedido = false;
  private readonly recuperando = signal(false);

  constructor() {
    this.sistemaService.cargarInfoSiFalta();
    // Una vez al entrar, o en cuanto /api/sistema/info diga que hay agentes. Con un 403 de otra visita
    // también se pregunta: si ahora contesta bien, vuelven a estar disponibles
    effect(() => {
      if (this.sistemaService.info()?.agentesDisponibles === true && !this.pedido) {
        this.pedido = true;
        untracked(() => this.agentes.cargarEstado());
      }
    });
    // Al acabar de volver a pedirlo: si ha salido bien, el botón pulsado desaparece y el foco va al
    // título del bloque y no a <body>. Lo mismo si «Reintentar» da 403: su botón se va con el aviso
    // («Volver a comprobar» sí sigue ahí tras otro 403, y conserva el foco)
    effect(() => {
      if (this.recuperando() && !this.cargandoEstado()) {
        untracked(() => {
          const eraReintento = this.reintentando();
          this.recuperando.set(false);
          this.reintentando.set(false);
          if ((this.disponibles() || eraReintento) && this.agentes.errorEstado() === null) {
            this.titulo()?.nativeElement.focus();
          }
        });
      }
    });
  }

  /** «Reintentar» tras un error y «Volver a comprobar» tras un 403 */
  protected volverAPedir(): void {
    if (this.cargandoEstado()) {
      return;
    }
    this.reintentando.set(!this.rechazados());
    this.recuperando.set(true);
    this.agentes.cargarEstado();
  }
}
