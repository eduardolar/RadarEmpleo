import {
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
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
  protected readonly cargandoEstado = this.agentes.cargandoEstado;
  /** Mientras se reintenta, el aviso del uso sigue a la vista con su mensaje aunque el servicio lo borre */
  private readonly reintentandoUso = signal(false);
  private readonly ultimoErrorUso = signal<string | null>(null);
  protected readonly errorUso = computed(
    () =>
      this.agentes.errorEstado() ??
      (this.reintentandoUso() && this.cargandoEstado() ? this.ultimoErrorUso() : null),
  );
  private readonly tituloUso = viewChild<ElementRef<HTMLElement>>('tituloUso');
  protected readonly historial = this.agentes.historial;
  protected readonly cargando = this.agentes.cargandoHistorial;
  protected readonly errorHistorial = this.agentes.errorHistorial;
  protected readonly nombreTarea = NOMBRE_TAREA;

  /** /api/sistema/info dice que sí, pero /api/agentes ha dado 403: se puede volver a comprobar */
  private readonly segunInfo = computed(() => this.info()?.agentesDisponibles === true);
  protected readonly rechazados = computed(() => this.segunInfo() && !this.disponibles());

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

  private refrescoPendiente = false;
  private destruida = false;
  /** Con un 403 de otra visita se pregunta una vez al entrar; si contesta bien, solo falta la lista */
  private comprobadoAlEntrar = false;
  private estadoRecienPedido = false;

  constructor() {
    // Al acabar el reintento: si ha salido bien, el botón pulsado desaparece y el foco va al título
    // de «Uso de hoy» y no a <body>
    effect(() => {
      if (this.reintentandoUso() && !this.cargandoEstado()) {
        untracked(() => {
          this.reintentandoUso.set(false);
          if (this.agentes.errorEstado() === null) {
            this.tituloUso()?.nativeElement.focus();
          }
        });
      }
    });
    inject(DestroyRef).onDestroy(() => (this.destruida = true));
    this.sistema.cargarInfoSiFalta();
    // Al entrar, o en cuanto /api/sistema/info diga que hay agentes, se pide la lista y el uso. Con un
    // 403 de antes (otra visita), se vuelve a preguntar una vez: si no, haría falta recargar
    effect(() => {
      const disponibles = this.disponibles();
      const segunInfo = this.segunInfo();
      untracked(() => {
        if (disponibles) {
          // Ya se ha pedido al entrar: un 403 de ahora se queda con «Volver a comprobar»
          this.comprobadoAlEntrar = true;
          if (this.estadoRecienPedido) {
            this.estadoRecienPedido = false;
            this.agentes.cargarHistorial();
          } else {
            this.refrescar();
          }
        } else if (segunInfo && !this.comprobadoAlEntrar) {
          this.comprobadoAlEntrar = true;
          this.volverAComprobar();
        }
      });
    });
  }

  protected refrescar(): void {
    this.agentes.cargarEstado();
    this.agentes.cargarHistorial();
  }

  protected reintentarUso(): void {
    if (this.cargandoEstado()) {
      return;
    }
    this.ultimoErrorUso.set(this.agentes.errorEstado());
    this.reintentandoUso.set(true);
    this.refrescar();
  }

  /** Lo que avisan las tarjetas: si terminan varias en la misma vuelta del seguimiento, se pide una vez */
  protected pedirRefresco(): void {
    if (this.refrescoPendiente) {
      return;
    }
    this.refrescoPendiente = true;
    queueMicrotask(() => {
      this.refrescoPendiente = false;
      if (!this.destruida) {
        this.refrescar();
      }
    });
  }

  /** Tras un 403: si ahora contesta bien, el efecto pide la lista; si vuelve a dar 403, se queda así */
  protected volverAComprobar(): void {
    this.estadoRecienPedido = true;
    this.agentes.cargarEstado();
  }

  protected reintentarConexion(): void {
    this.sistema.cargarInfo();
  }
}
