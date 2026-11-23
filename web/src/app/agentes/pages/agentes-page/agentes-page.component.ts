import { Component, computed, effect, inject, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { SistemaService } from '../../../core/services/sistema.service';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { EnlaceVolver } from '../../../shared/components/enlace-volver/enlace-volver';
import { FechaMadridPipe } from '../../../shared/pipes/fecha-madrid.pipe';
import { EstadoEjecucion } from '../../components/estado-ejecucion/estado-ejecucion';
import { AgentesService } from '../../services/agentes.service';
import {
  motivoSinAgentes,
  NOMBRE_TAREA,
  reservaDelDia,
  usoDelDia,
} from '../../utils/textos-ejecucion';
import { ultimasDeCadaTarea } from '../../utils/ultimas-ejecuciones';

/**
 * Lo que ha lanzado la app con Claude y el uso de hoy: página de lectura, sin acción principal (por
 * eso no lleva relleno lima). El seguimiento de lo que está en marcha lo hace `AgentesService`; aquí
 * solo se vuelve a pedir la lista y el uso cuando una tarjeta avisa de que algo ha cambiado
 */
@Component({
  imports: [
    CabeceraPagina,
    EnlaceVolver,
    EstadoEjecucion,
    FechaMadridPipe,
    MatButtonModule,
    MatProgressBarModule,
    RouterLink,
  ],
  selector: 'app-agentes-page',
  templateUrl: './agentes-page.component.html',
})
export default class AgentesPageComponent {
  private readonly sistema = inject(SistemaService);
  private readonly agentes = inject(AgentesService);

  protected readonly info = this.sistema.info;
  protected readonly sinConexion = computed(() => this.sistema.estado() === 'sin-conexion');
  protected readonly disponibles = this.agentes.disponibles;
  protected readonly errorEstado = this.agentes.errorEstado;
  protected readonly historial = this.agentes.historial;
  protected readonly cargando = this.agentes.cargandoHistorial;
  protected readonly errorHistorial = this.agentes.errorHistorial;
  protected readonly nombreTarea = NOMBRE_TAREA;

  protected readonly motivo = computed(() => {
    const info = this.info();
    return info ? motivoSinAgentes(info) : null;
  });

  private readonly gasto = computed(() => this.agentes.estado()?.gasto ?? null);
  protected readonly tope = computed(() => this.gasto()?.topeUsd ?? null);
  protected readonly uso = computed(() => {
    const gasto = this.gasto();
    return gasto ? usoDelDia(gasto) : null;
  });
  protected readonly reserva = computed(() => {
    const gasto = this.gasto();
    return gasto ? reservaDelDia(gasto) : null;
  });

  protected readonly ultimas = computed(() => ultimasDeCadaTarea(this.historial() ?? []));

  constructor() {
    this.sistema.cargarInfoSiFalta();
    // Al entrar, o en cuanto /api/sistema/info diga que hay agentes, se pide la lista y el uso
    effect(() => {
      if (this.disponibles()) {
        untracked(() => this.refrescar());
      }
    });
  }

  protected refrescar(): void {
    this.agentes.cargarEstado();
    this.agentes.cargarHistorial();
  }

  protected reintentarHistorial(): void {
    this.agentes.cargarHistorial();
  }

  protected reintentarConexion(): void {
    this.sistema.cargarInfo();
  }
}
