import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  afterRenderEffect,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  ElementRef,
  inject,
  Injector,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { EstadoEjecucion } from '../../../agentes/components/estado-ejecucion/estado-ejecucion';
import { EjecucionAgente } from '../../../agentes/interfaces/ejecucion.interface';
import { AgentesService, esTerminal } from '../../../agentes/services/agentes.service';
import { AVISO_GASTO } from '../../../agentes/utils/textos-ejecucion';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import { SistemaService } from '../../../core/services/sistema.service';
import { diaMadrid } from '../../../shared/utils/fechas-madrid';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { EnlaceVolver } from '../../../shared/components/enlace-volver/enlace-volver';
import { SeccionPlegable } from '../../../shared/components/seccion-plegable/seccion-plegable';
import { AvisosService } from '../../../shared/services/avisos.service';
import { ListaImportacion } from '../../components/lista-importacion/lista-importacion';
import { ResultadoAplicacion } from '../../interfaces/importacion.interface';
import { AlNoValerExtraccion, ImportacionService } from '../../services/importacion.service';
import {
  AVISO_PUEDE_TARDAR,
  AVISO_RADAR,
  AVISO_SOLO_LEE,
  EXPLICACION_DUDAS,
  EXPLICACION_FUERA,
  EXPLICACION_PARA_REVISAR,
  EXPLICACION_SIN_LEER,
  EXPLICACION_YA_EXISTEN,
  filasAplicables,
  filasConResultado,
  lineasResumen,
  textoBotonAplicar,
  textoEnviosEnTexto,
  textoExtraccionLeida,
  textoFilaFuera,
  textoFilaSinLeer,
  textoNadaQueAnadir,
  textoResultado,
  textoVistaCombinada,
  tituloNadaQueAplicar,
} from '../../utils/resumen-importacion';

@Component({
  imports: [
    CabeceraPagina,
    EnlaceVolver,
    EstadoEjecucion,
    ListaImportacion,
    MatButtonModule,
    MatProgressBarModule,
    NgTemplateOutlet,
    RouterLink,
    SeccionPlegable,
  ],
  selector: 'app-importacion-page',
  templateUrl: './importacion-page.component.html',
})
export default class ImportacionPageComponent {
  private readonly importacion = inject(ImportacionService);
  private readonly avisos = inject(AvisosService);
  private readonly agentes = inject(AgentesService);
  private readonly sistema = inject(SistemaService);
  private readonly documento = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly inicio = viewChild.required<ElementRef<HTMLElement>>('inicio');
  private readonly tarjetaExtraccion = viewChild(EstadoEjecucion);
  private readonly botonExtraer = viewChild<ElementRef<HTMLElement>>('botonExtraer');
  private readonly lineaCombinada = viewChild<ElementRef<HTMLElement>>('lineaCombinada');

  protected readonly vista = this.importacion.vistaPrevia;
  protected readonly cargando = this.importacion.cargando;
  protected readonly error = this.importacion.error;
  protected readonly aplicando = this.importacion.aplicando;

  /** Solo de esta visita a la página: al volver a entrar ya no viene al caso */
  protected readonly resultado = signal<ResultadoAplicacion | null>(null);
  protected readonly textoResultado = textoResultado;
  protected readonly avisoRadar = AVISO_RADAR;
  protected readonly explicacionSinLeer = EXPLICACION_SIN_LEER;
  protected readonly explicacionFuera = EXPLICACION_FUERA;
  protected readonly explicacionDudas = EXPLICACION_DUDAS;
  protected readonly textoFilaFuera = textoFilaFuera;
  protected readonly textoFilaSinLeer = textoFilaSinLeer;
  protected readonly avisoExtraer = `${AVISO_SOLO_LEE} ${AVISO_GASTO}`;
  protected readonly avisoPuedeTardar = AVISO_PUEDE_TARDAR;
  /** La petición que ha fallado era la de la vista combinada: también se puede ir a la de la tabla */
  protected readonly fallaLaCombinada = computed(
    () => this.error() !== null && this.importacion.extraccionId() !== null,
  );
  /** Con más filas que estas, el botón se repite al final de la lista */
  protected readonly filasParaRepetirBoton = 3;

  /** Si ha entrado o cambiado algo, lo siguiente es ir a verlo */
  protected readonly hayCambiosGuardados = computed(() => {
    const hecho = this.resultado();
    return hecho !== null && hecho.creadas + hecho.avanzadas > 0;
  });

  private readonly filas = computed(() => this.vista()?.filas ?? []);
  protected readonly porAplicar = computed(() => filasAplicables(this.filas()));
  protected readonly nuevas = computed(() => filasConResultado(this.filas(), 'Nueva'));
  protected readonly avanzan = computed(() => filasConResultado(this.filas(), 'AvanzaEstado'));
  protected readonly grupos = computed(() => [
    {
      titulo: 'Ya las tienes',
      explicacion: EXPLICACION_YA_EXISTEN,
      filas: filasConResultado(this.filas(), 'YaExiste'),
    },
    {
      titulo: 'Para revisar',
      explicacion: EXPLICACION_PARA_REVISAR,
      filas: filasConResultado(this.filas(), 'Conflicto'),
    },
  ]);

  protected readonly todoAlDia = computed(
    () => this.vista() !== null && this.porAplicar().length === 0,
  );

  protected readonly lineasResumen = computed(() => {
    const vista = this.vista();
    return vista ? lineasResumen(vista.resumen) : [];
  });
  protected readonly textoBoton = computed(() => {
    const vista = this.vista();
    return vista ? textoBotonAplicar(vista.resumen) : '';
  });
  protected readonly tituloAlDia = computed(() => {
    const vista = this.vista();
    return vista ? tituloNadaQueAplicar(vista.resumen, this.resultado() !== null) : '';
  });
  protected readonly textoNadaQueAnadir = computed(() => {
    const vista = this.vista();
    return vista ? textoNadaQueAnadir(vista.resumen) : '';
  });
  protected readonly avisoEnvios = computed(() => {
    const vista = this.vista();
    return vista
      ? textoEnviosEnTexto(vista.resumen.enviosEnTexto, this.agentes.puedeLanzar())
      : null;
  });

  /** «Tabla + N leídas del texto libre con Claude», con las partes perdidas si las hay; null con la vista de la tabla */
  protected readonly lineaVistaCombinada = computed(() => {
    const vista = this.vista();
    return vista?.origen === 'RegistroConClaude'
      ? textoVistaCombinada(vista.resumen.delTextoLibre, vista.partesPerdidas)
      : null;
  });

  /** La extracción que se enseña: la lanzada aquí o la que seguía en marcha al entrar */
  protected readonly extraccion = signal<EjecucionAgente | null>(null);
  private readonly perdidaId = signal<number | null>(null);
  protected readonly lanzandoExtraccion = signal(false);
  /** Con la versión del seguimiento; null si se ha perdido (la API ya no la encuentra) */
  private readonly extraccionActual = computed(() => {
    const e = this.extraccion();
    return e && this.perdidaId() !== e.id ? (this.agentes.ejecuciones().get(e.id) ?? e) : null;
  });
  protected readonly enMarcha = computed(() => {
    const e = this.extraccionActual();
    return e !== null && !esTerminal(e);
  });
  /**
   * La que ya ha terminado bien y se puede volver a ver (al entrar, si sigue valiendo, o tras «Ver solo la
   * tabla»): se ofrece en vez de extraer otra vez
   */
  protected readonly leida = signal<EjecucionAgente | null>(null);
  /** Cuándo se ha empezado a ofrecer: «hace 12 min» se cuenta hasta ahí */
  private readonly ofrecidaEn = signal(new Date());
  protected readonly textoLeida = computed(() => {
    const e = this.leida();
    return e ? textoExtraccionLeida(e.fin ?? e.fechaAlta, this.ofrecidaEn()) : null;
  });
  protected readonly verLeida = computed(
    () => this.leida() !== null && this.vista()?.origen === 'Tabla' && !this.aplicando(),
  );
  protected readonly verExtraerOtraVez = computed(
    () => this.verLeida() && this.agentes.puedeLanzar(),
  );
  protected readonly verBotonExtraer = computed(() => {
    const e = this.extraccionActual();
    // En Error o Cancelada la tarjeta ya ofrece «Reintentar»
    const libre = e === null || (esTerminal(e) && !e.puedeReintentar);
    return (
      this.agentes.puedeLanzar() && this.avisoEnvios() !== null && libre && this.leida() === null
    );
  });
  // Aparte del effect: volver a guardar la misma info no repite la consulta de la última
  private readonly segunInfo = computed(() => this.sistema.info()?.agentesDisponibles === true);
  /** Con un 403 de otra visita se vuelve a preguntar una vez al entrar */
  private comprobadoAlEntrar = false;
  // El botón de extraer desaparece al salir la tarjeta: el foco pasa a ella y no cae en <body>
  private enfocarTarjeta = false;
  // Mientras se lee la vista combinada la tarjeta se va y el foco cae en <body>: al salir, va a su línea
  private enfocarCombinada = false;
  // La combinada ya no vale y se vuelve a la tabla: el foco no se queda en <body>
  private enfocarTrasVolver = false;
  private botonVisto: HTMLElement | undefined;
  /** Si la extracción ya no vale, se vuelve a la tabla diciendo por qué (salvo que el 409 de aplicar ya lo diga) */
  private readonly alNoValer: AlNoValerExtraccion = (mensaje, yaAvisado) => {
    this.leida.set(null);
    this.enfocarCombinada = false;
    if (this.destroyRef.destroyed) {
      return;
    }
    this.enfocarTrasVolver = true;
    if (!yaAvisado) {
      this.avisos.error(mensaje);
    }
  };

  constructor() {
    this.importacion.previsualizar();
    // Su aviso retendría la página cerrada; si está guardando, la lectura del final aún la necesita
    this.destroyRef.onDestroy(() => {
      if (!this.aplicando()) {
        this.importacion.olvidarExtraccion();
      }
    });

    // Al entrar (también directo o con F5) se retoma la extracción que siga en marcha; una ya
    // terminada no se enseña
    this.sistema.cargarInfoSiFalta();
    effect(() => {
      const segunInfo = this.segunInfo();
      const disponibles = this.agentes.disponibles();
      untracked(() => {
        if (disponibles) {
          this.comprobadoAlEntrar = true;
          this.retomarExtraccion();
        } else if (segunInfo && !this.comprobadoAlEntrar) {
          this.comprobadoAlEntrar = true;
          this.agentes.cargarEstado();
        }
      });
    });

    afterRenderEffect(() => {
      const tarjeta = this.tarjetaExtraccion();
      const boton = this.botonExtraer()?.nativeElement;
      const linea = this.lineaCombinada()?.nativeElement;
      const hayVista = this.vista() !== null;
      const hayError = this.error() !== null;
      untracked(() => {
        // Si la tabla tampoco se ha podido leer, el aviso de error ya está a la vista
        if (hayError) {
          this.enfocarTrasVolver = false;
        }
        if (this.enfocarTrasVolver && hayVista) {
          this.enfocarTrasVolver = false;
          // Del DOM ya pintado: la consulta del botón que sale con la vista aún no se ha puesto al día
          const inicio = this.inicio().nativeElement;
          const destino = inicio.querySelector<HTMLElement>('#boton-extraer') ?? inicio;
          if (this.focoSinSitio()) {
            destino.focus({ preventScroll: true });
          }
        }
        if (this.enfocarTarjeta && tarjeta) {
          this.enfocarTarjeta = false;
          tarjeta.enfocar();
        }
        if (this.enfocarCombinada && linea) {
          this.enfocarCombinada = false;
          // Si entretanto se ha ido a otro sitio, no se le mueve
          if (this.focoSinSitio()) {
            linea.focus({ preventScroll: true });
          }
        }
        // Vuelve el botón (la tarea se ha perdido): si el foco estaba en la tarjeta, pasa a él
        const nuevo = boton !== this.botonVisto;
        this.botonVisto = boton;
        const enTarjeta = this.documento.activeElement?.closest('app-estado-ejecucion') != null;
        if (tarjeta && boton && nuevo && enTarjeta) {
          boton.focus({ preventScroll: true });
        }
      });
    });
  }

  protected aplicar(): void {
    // El botón sigue recibiendo clics mientras guarda (disabledInteractive, para no perder el foco)
    if (this.aplicando()) {
      return;
    }
    this.resultado.set(null);
    // Sin cancelar al destruirse: salir de la página no debe perder lo que ya se está guardando
    this.importacion.aplicar().subscribe({
      next: (resultado) => this.terminarAplicar(resultado),
      error: (error: ErrorApi) => this.avisos.error(error.mensaje),
    });
  }

  protected reintentar(): void {
    // Mientras guarda no: borraría la vista previa del lote que se está guardando (disabledInteractive deja pasar el clic)
    if (this.aplicando()) {
      return;
    }
    // El resultado era de la lectura anterior: el título tiene que hablar de la nueva
    this.resultado.set(null);
    this.importacion.volverALeer();
    this.enfocarInicio();
  }

  protected verSoloLaTabla(): void {
    if (this.aplicando()) {
      return;
    }
    this.resultado.set(null);
    this.enfocarCombinada = false;
    this.importacion.previsualizar();
    this.enfocarInicio();
  }

  protected extraer(): void {
    if (this.lanzandoExtraccion()) {
      return;
    }
    this.lanzandoExtraccion.set(true);
    this.agentes
      .lanzarExtraccion()
      .pipe(
        finalize(() => this.lanzandoExtraccion.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (e) => {
          this.enfocarTarjeta = true;
          // Lo leído antes deja de ofrecerse: la nueva lo sustituye
          this.leida.set(null);
          this.extraccion.set(e);
        },
        error: (error: ErrorApi) => this.avisos.error(error.mensaje),
      });
  }

  /** También para `reintentada`: la tarjeta pasa a la nueva y la página la guarda como la suya */
  protected guardarExtraccion(e: EjecucionAgente): void {
    this.extraccion.set(e);
  }

  protected alTerminarExtraccion(e: EjecucionAgente): void {
    if (e.estado !== 'Ok') {
      this.extraccion.set(e);
      return;
    }
    // La vista combinada ocupa su sitio; tras «Ver solo la tabla» se ofrece verla otra vez
    this.extraccion.set(null);
    this.ofrecer(e);
    this.enfocarCombinada = true;
    if (this.aplicando()) {
      // Pedirla ahora borraría la vista que se está guardando: la pide la lectura del final del aplicar
      this.importacion.apuntarExtraccion(e.id, this.alNoValer);
      return;
    }
    this.resultado.set(null);
    this.importacion.previsualizarExtraccion(e.id, this.alNoValer);
  }

  protected verLoLeido(): void {
    const e = this.leida();
    if (!e || this.aplicando()) {
      return;
    }
    this.resultado.set(null);
    this.enfocarCombinada = true;
    this.importacion.previsualizarExtraccion(e.id, this.alNoValer);
  }

  protected alPerderExtraccion(id: number): void {
    this.perdidaId.set(id);
  }

  private retomarExtraccion(): void {
    if (this.agentes.estado() === null && !this.agentes.cargandoEstado()) {
      this.agentes.cargarEstado();
    }
    this.agentes
      .ultimaDe('ExtraerRegistro')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (e) => {
          if (!e || this.extraccion() !== null || this.leida() !== null) {
            return;
          }
          if (!esTerminal(e)) {
            this.extraccion.set(e);
          } else if (e.estado === 'Ok' && this.deHoy(e)) {
            this.ofrecerSiVale(e);
          }
        },
        // Sin la última se puede lanzar igual: la API devuelve la que esté en marcha
        error: () => undefined,
      });
  }

  private terminarAplicar(resultado: ResultadoAplicacion): void {
    if (this.destroyRef.destroyed) {
      const radar = resultado.radarActualizado ? '' : ` ${AVISO_RADAR}`;
      this.avisos.exito(`${textoResultado(resultado)}${radar}`);
      return;
    }
    this.resultado.set(resultado);
    // El botón desaparece y el foco caería en <body>; si ya se ha ido a otro sitio, no se toca
    afterNextRender(
      () => {
        if (this.focoSinSitio()) {
          this.enfocarInicio();
        }
      },
      { injector: this.injector },
    );
  }

  private ofrecer(e: EjecucionAgente): void {
    this.ofrecidaEn.set(new Date());
    this.leida.set(e);
  }

  private deHoy(e: EjecucionAgente): boolean {
    return diaMadrid(e.fin ?? e.fechaAlta) === diaMadrid(new Date());
  }

  /** Solo si su vista combinada sigue valiendo; si no, queda el botón de extraer, sin aviso */
  private ofrecerSiVale(e: EjecucionAgente): void {
    this.importacion
      .extraccionVale(e.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((vale) => {
        if (vale && this.extraccion() === null && this.leida() === null) {
          this.ofrecer(e);
        }
      });
  }

  private focoSinSitio(): boolean {
    const activo = this.documento.activeElement;
    return activo === null || activo === this.documento.body;
  }

  private enfocarInicio(): void {
    this.inicio().nativeElement.focus({ preventScroll: true });
  }
}
