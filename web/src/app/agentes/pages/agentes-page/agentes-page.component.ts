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
import { diaMadrid, horaMadrid } from '../../../shared/utils/fechas-madrid';
import { EstadoEjecucion } from '../../components/estado-ejecucion/estado-ejecucion';
import { AgentesService } from '../../services/agentes.service';
import {
  agruparPorDia,
  erroresResueltos,
  FILTROS_TAREAS,
  FiltroTareas,
  pasaElFiltro,
  recuentoDeHoy,
} from '../../utils/tareas-por-dia';
import {
  motivoSinAgentes,
  NOMBRE_TAREA,
  reservaDelDia,
  TEXTO_SIGUE_RECHAZADO,
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
  private readonly textoMotivo = viewChild<ElementRef<HTMLElement>>('textoMotivo');
  /** Adónde va el foco cuando el botón pulsado desaparece con su panel; se lleva al pintarse el destino */
  private readonly focoPendiente = signal<'uso' | 'motivo' | null>(null);
  /** «Volver a comprobar» pulsado y sin respuesta todavía */
  private readonly comprobando = signal(false);
  protected readonly esperandoComprobacion = computed(
    () => this.comprobando() && this.cargandoEstado(),
  );
  /** Lo que dio la última comprobación a mano, si no sirvió: sin esto, el botón no parece hacer nada */
  protected readonly resultadoComprobacion = signal<string | null>(null);
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
  protected readonly resueltos = computed(() => erroresResueltos(this.historial() ?? []));

  protected readonly filtros = FILTROS_TAREAS;
  protected readonly filtro = signal<FiltroTareas>('todas');
  /** El día de Madrid que da la API con el uso; mientras no llega, el de este reloj */
  private readonly hoy = computed(() => this.gasto()?.hoy ?? diaMadrid(new Date()));
  protected readonly recuento = computed(() => recuentoDeHoy(this.historial() ?? [], this.hoy()));
  protected readonly dias = computed(() => {
    const filtro = this.filtro();
    return agruparPorDia(
      (this.historial() ?? []).filter((e) => pasaElFiltro(e, filtro)),
      this.hoy(),
    );
  });
  protected readonly hora = horaMadrid;

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
            // Con un 403 desaparece el bloque entero: el foco va a la explicación que lo sustituye
            this.focoPendiente.set(this.disponibles() ? 'uso' : 'motivo');
          }
        });
      }
    });
    // Al contestar «Volver a comprobar»: si ha salido bien, el panel desaparece con su botón y el foco
    // va al título de «Uso de hoy»; si no, se dice debajo del botón, que sigue ahí
    effect(() => {
      if (this.comprobando() && !this.cargandoEstado()) {
        untracked(() => {
          this.comprobando.set(false);
          if (this.disponibles()) {
            this.focoPendiente.set('uso');
          } else {
            this.resultadoComprobacion.set(this.agentes.errorEstado() ?? TEXTO_SIGUE_RECHAZADO);
          }
        });
      }
    });
    // El destino no existe hasta que se pinta el bloque nuevo: se espera a que aparezca
    effect(() => {
      const destino = this.focoPendiente();
      const elemento = destino === 'uso' ? this.tituloUso() : this.textoMotivo();
      if (destino !== null && elemento) {
        elemento.nativeElement.focus();
        untracked(() => this.focoPendiente.set(null));
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
  private volverAComprobar(): void {
    this.estadoRecienPedido = true;
    this.agentes.cargarEstado();
  }

  /** El botón «Volver a comprobar»: como `volverAComprobar`, pero diciendo qué pasa mientras y después */
  protected comprobarAMano(): void {
    if (this.esperandoComprobacion()) {
      return;
    }
    this.resultadoComprobacion.set(null);
    this.comprobando.set(true);
    this.volverAComprobar();
  }

  protected filtrar(filtro: FiltroTareas): void {
    this.filtro.set(filtro);
  }

  protected reintentarConexion(): void {
    this.sistema.cargarInfo();
  }
}
