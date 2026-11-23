import { Component, computed, effect, inject, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { AgentesService } from '../../../agentes/services/agentes.service';
import {
  reservaDelDia,
  TEXTO_RECHAZADOS,
  usoDelDia,
} from '../../../agentes/utils/textos-ejecucion';
import { SistemaInfo } from '../../../core/interfaces/sistema-info.interface';
import { SistemaService } from '../../../core/services/sistema.service';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { EnlaceVolver } from '../../../shared/components/enlace-volver/enlace-volver';

/**
 * Ajustes: de momento solo enseña lo que ya es real (modo, versión y agentes). Sin botones (salvo
 * «Reintentar» si falla el estado de los agentes): no se promete nada que no exista todavía
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
  protected readonly errorEstado = this.agentes.errorEstado;
  protected readonly motivoCola = this.agentes.motivoCola;

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

  protected readonly versionClaude = computed(() => {
    const estado = this.estadoAgentes();
    if (!estado?.versionClaude) {
      return null;
    }
    return estado.versionDistinta
      ? `${estado.versionClaude} · probado con ${estado.versionProbada}`
      : estado.versionClaude;
  });

  private pedido = false;

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
  }

  protected reintentar(): void {
    this.agentes.cargarEstado();
  }
}

function estadoSinAgentes(info: Pick<SistemaInfo, 'modo' | 'agentesHabilitados' | 'desdeElPc'>): {
  etiqueta: string;
  explicacion: string;
} {
  if (info.modo === 'Demo') {
    return { etiqueta: 'Apagados', explicacion: 'En la demo no se usa Claude.' };
  }
  // Por el móvil `agentesHabilitados` siempre llega a false: no quiere decir que estén apagados
  if (!info.desdeElPc) {
    return {
      etiqueta: 'Solo desde el PC',
      explicacion: 'Se usan desde el PC donde está instalada la app.',
    };
  }
  if (!info.agentesHabilitados) {
    return { etiqueta: 'Apagados', explicacion: 'Se encienden en la configuración de la app.' };
  }
  // Encendidos y en el PC, pero /api/agentes ha dado 403
  return { etiqueta: 'Encendidos', explicacion: TEXTO_RECHAZADOS };
}
