import { Location, NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  signal,
  untracked,
  viewChild,
  viewChildren,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TipoRecordatorio } from '../../../candidaturas/interfaces/candidatura.interface';
import { EstadoApi } from '../../../core/components/estado-api/estado-api';
import { TarjetaRecordatorio } from '../../../recordatorios/components/tarjeta-recordatorio/tarjeta-recordatorio';
import { AccionesRecordatorioService } from '../../../recordatorios/services/acciones-recordatorio.service';
import { RecordatoriosService } from '../../../recordatorios/services/recordatorios.service';
import { tipoInicialSegunEstado } from '../../../recordatorios/utils/tipo-inicial';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { SeccionPlegable } from '../../../shared/components/seccion-plegable/seccion-plegable';
import { FechaActualService } from '../../../shared/services/fecha-actual.service';
import { diaCorto, diaLargo, diaMadrid } from '../../../shared/utils/fechas-madrid';
import { senalPantallaGrande } from '../../../shared/utils/pantalla';
import { esCampoDeTexto } from '../../../shared/utils/teclado';
import { DetalleRecordatorio } from '../../components/detalle-recordatorio/detalle-recordatorio';
import { ResumenHoy } from '../../components/resumen-hoy/resumen-hoy';
import { PanelHoy, RecordatorioAgenda } from '../../interfaces/panel-hoy.interface';
import {
  agruparPorDia,
  chipsTipo,
  filtrar,
  leerDia,
  leerTipo,
  semanaDe,
  textoTareas,
} from '../../utils/filtros-hoy';

/** Las listas de recordatorios del panel */
const SECCIONES = ['paraHoy', 'proximos', 'masAdelante', 'hechosHoy'] as const;
type Seccion = (typeof SECCIONES)[number];

/** Parámetro de la URL con el recordatorio cuyo detalle se ve: «/inicio?ver=12» */
const PARAMETRO_VER = 'ver';
/** Los filtros también van en la URL, para poder enlazarlos: «/inicio?tipo=Entrevista&dia=2026-11-18» */
const PARAMETRO_TIPO = 'tipo';
const PARAMETRO_DIA = 'dia';
/** «?semana=esta»: lo de esta semana, que es lo que cuenta la cifra de las entrevistas del resumen */
const PARAMETRO_SEMANA = 'semana';
const VALOR_SEMANA = 'esta';

/** Las listas del panel tal como se ven: con los filtros ya aplicados */
type Listas = Record<Seccion, RecordatorioAgenda[]>;

/** El recordatorio elegido para el panel de detalle, tal como está ahora en el panel */
interface Seleccion {
  recordatorio: RecordatorioAgenda;
  seccion: Seccion;
}

function leerId(texto: string | null): number | null {
  const id = Number(texto);
  return texto !== null && Number.isInteger(id) && id > 0 ? id : null;
}

/** A dónde va el foco cuando llegue el panel nuevo: la tarjeta que se ha tocado cambia de sitio o desaparece */
interface FocoPendiente {
  /** El panel que había al pedirlo: hasta que no llegue otro, no se mueve el foco */
  panel: PanelHoy;
  /** Tarjetas a las que ir, por orden de preferencia */
  candidatos: number[];
  /** Si no queda ninguna, el título de esta sección */
  seccion: Seccion;
}

/** El recordatorio que hay que volver a enseñar en el detalle cuando llegue el panel nuevo */
interface ReelegirPendiente {
  id: number;
  /** El panel que había al deshacer: en él aún sale como hecho */
  panel: PanelHoy;
}

/**
 * Hoy: lo que hay que hacer. Arriba lo vencido y lo de hoy (con «Hecho» y «Posponer»), debajo lo de
 * los próximos 7 días y, plegado, las enviadas sin respuesta, lo de más adelante y lo hecho hoy.
 *
 * En pantallas grandes hay un panel a la derecha: enseña el resumen (cifras y semana) o, al elegir
 * una tarjeta, el detalle de ese recordatorio. La elegida va en la URL («?ver=12») para que aguante
 * una recarga. En el móvil el resumen va arriba, plegado, y no hay panel de detalle.
 *
 * Las listas se pueden filtrar por tipo (los chips de arriba) y por día (el calendario del resumen)
 * o por esta semana (la cifra de las entrevistas). Los filtros se suman y van en la URL
 * («?tipo=Entrevista&dia=2026-11-18», «?tipo=Entrevista&semana=esta»).
 */
@Component({
  imports: [
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatProgressBarModule,
    CabeceraPagina,
    EstadoApi,
    SeccionPlegable,
    TarjetaRecordatorio,
    ResumenHoy,
    DetalleRecordatorio,
  ],
  selector: 'app-hoy-page',
  templateUrl: './hoy-page.component.html',
  host: { '(document:keydown.escape)': 'alPulsarEscape($event)' },
})
export default class HoyPageComponent {
  private readonly recordatoriosService = inject(RecordatoriosService);
  private readonly acciones = inject(AccionesRecordatorioService);
  private readonly fechaActual = inject(FechaActualService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly inicio = viewChild.required<ElementRef<HTMLElement>>('inicio');
  private readonly tarjetas = viewChildren(TarjetaRecordatorio);
  private readonly panelLateral = viewChild<ElementRef<HTMLElement>>('panelLateral');
  private readonly detalle = viewChild(DetalleRecordatorio);

  /** Donde caben dos columnas: el panel de la derecha y las tarjetas que se pueden elegir */
  protected readonly pantallaGrande = senalPantallaGrande();

  protected readonly panel = this.recordatoriosService.panel;
  protected readonly cargando = this.recordatoriosService.cargando;
  protected readonly error = this.recordatoriosService.error;

  /**
   * El panel guardado en el servicio de una visita anterior se enseña al entrar, pero puede estar
   * desfasado: lo que se corrige solo (un filtro sin nada, un elegido que ya no está) espera al que
   * se pide al entrar
   */
  private readonly panelAlEntrar = this.recordatoriosService.panel();
  private readonly panelFresco = computed(() => {
    const p = this.panel();
    return p !== null && p !== this.panelAlEntrar;
  });

  /** No hay nada que enseñar: sale el estado vacío de la taza */
  protected readonly vacio = computed(() => {
    const p = this.panel();
    return (
      p !== null &&
      p.paraHoy.length === 0 &&
      p.proximos.length === 0 &&
      p.masAdelante.length === 0 &&
      p.hechosHoy.length === 0 &&
      p.sinRespuesta.length === 0 &&
      p.sinRespuestaAntiguas === 0
    );
  });

  /**
   * Para cuando hoy no hay nada: el primer día de «Próximos 7 días» que tiene algo («lun 17 nov»),
   * cuántas tareas hay ese día y si la primera es un seguimiento
   */
  protected readonly proximoDia = computed(() => {
    const proximos = this.panel()?.proximos ?? [];
    if (proximos.length === 0) {
      return null;
    }
    const dia = diaMadrid(proximos[0].fechaHora);
    return {
      texto: diaCorto(dia),
      total: proximos.filter((r) => diaMadrid(r.fechaHora) === dia).length,
      seguimiento: proximos[0].tipo === 'Seguimiento',
    };
  });

  private readonly parametros = inject(ActivatedRoute).snapshot.queryParamMap;

  /** El recordatorio elegido; al entrar, el que diga la URL */
  private readonly idElegido = signal(leerId(this.parametros.get(PARAMETRO_VER)));

  /** El tipo que se ha pedido ver (un chip o la URL), haya o no algo de ese tipo */
  private readonly tipoPedido = signal(leerTipo(this.parametros.get(PARAMETRO_TIPO)));

  /** El día por el que se filtra («2026-11-18»): un día del calendario o el de la URL */
  protected readonly diaElegido = signal(leerDia(this.parametros.get(PARAMETRO_DIA)));

  /** Se ha pedido ver solo esta semana (la cifra de las entrevistas o la URL) */
  private readonly semanaPedida = signal(this.parametros.get(PARAMETRO_SEMANA) === VALOR_SEMANA);

  /** Esta semana, de lunes a domingo, si se filtra por ella. Sale del «hoy» del servidor */
  private readonly semana = computed(() => {
    const p = this.panel();
    return this.semanaPedida() && p !== null ? semanaDe(p.hoy) : null;
  });

  /**
   * Lo que dice el aviso de los filtros de fecha: «el mié 18 nov» para verlo y «el miércoles 18 de
   * noviembre» para leerlo en voz alta, o «esta semana»
   */
  protected readonly avisoFecha = computed(() => {
    const dia = this.diaElegido();
    if (dia !== null) {
      return { prefijo: 'el ', corto: diaCorto(dia), largo: diaLargo(dia) };
    }
    return this.semana() === null
      ? null
      : { prefijo: '', corto: 'esta semana', largo: 'esta semana' };
  });

  /** Lo que anuncia el lector de pantalla al cambiar el filtro de fecha; vacío si no hay */
  protected readonly anuncioFiltro = computed(() => {
    const aviso = this.avisoFecha();
    return aviso === null ? '' : `Mostrando ${aviso.prefijo}${aviso.largo}`;
  });

  private readonly pendientes = computed(() => {
    const p = this.panel();
    return p === null ? [] : [...p.paraHoy, ...p.proximos, ...p.masAdelante];
  });

  /** «Todas» y los tipos que tienen algo pendiente, cada uno con su número */
  protected readonly chips = computed(() =>
    chipsTipo(this.pendientes(), this.diaElegido(), this.semana()),
  );

  /** El tipo por el que se filtra: el pedido, mientras quede algo pendiente de ese tipo */
  protected readonly tipoElegido = computed(() => {
    const tipo = this.tipoPedido();
    return tipo !== null && this.chips().some((chip) => chip.tipo === tipo) ? tipo : null;
  });

  /**
   * Hay entrevistas pendientes esta semana: la cifra del resumen (que cuenta esas mismas) puede filtrar
   * por ellas
   */
  protected readonly hayEntrevistas = computed(() => {
    const p = this.panel();
    if (p === null) {
      return false;
    }
    const semana = semanaDe(p.hoy);
    return filtrar(this.pendientes(), { tipo: 'Entrevista', dia: null, semana }).length > 0;
  });

  /** La lista enseña lo mismo que cuenta la cifra de las entrevistas */
  protected readonly entrevistasFiltradas = computed(
    () => this.tipoElegido() === 'Entrevista' && this.semana() !== null,
  );

  protected readonly hayFiltro = computed(
    () => this.tipoElegido() !== null || this.diaElegido() !== null || this.semana() !== null,
  );

  /** Las cuatro listas de recordatorios, filtradas por el tipo y el día elegidos */
  protected readonly listas = computed<Listas | null>(() => {
    const p = this.panel();
    if (p === null) {
      return null;
    }
    const filtro = { tipo: this.tipoElegido(), dia: this.diaElegido(), semana: this.semana() };
    return {
      paraHoy: filtrar(p.paraHoy, filtro),
      proximos: filtrar(p.proximos, filtro),
      masAdelante: filtrar(p.masAdelante, filtro),
      hechosHoy: filtrar(p.hechosHoy, filtro),
    };
  });

  /** «Próximos 7 días», con un encabezado por día */
  protected readonly gruposProximos = computed(() => agruparPorDia(this.listas()?.proximos ?? []));

  /** Con el filtro puesto no queda nada que enseñar */
  protected readonly nadaFiltrado = computed(() => {
    const listas = this.listas();
    return this.hayFiltro() && listas !== null && SECCIONES.every((s) => listas[s].length === 0);
  });

  /** «entrevistas», para decir de qué no hay nada ese día */
  protected readonly textoTipoElegido = computed(() => {
    const tipo = this.tipoElegido();
    return this.chips().find((chip) => chip.tipo === tipo && tipo !== null)?.texto ?? null;
  });

  protected readonly textoTareas = textoTareas;

  /** Lo que enseña el panel de detalle; null si toca el resumen (o no hay panel: en el móvil) */
  protected readonly seleccion = computed<Seleccion | null>(() => {
    const id = this.idElegido();
    const p = this.listas();
    if (id === null || p === null || !this.pantallaGrande()) {
      return null;
    }
    for (const seccion of SECCIONES) {
      const recordatorio = p[seccion].find((r) => r.id === id);
      if (recordatorio) {
        return { recordatorio, seccion };
      }
    }
    return null;
  });

  /**
   * Las secciones plegadas que hay que enseñar abiertas: las que tienen dentro el recordatorio
   * elegido (al entrar con «?ver=12» o al recargar). Una vez abierta no se vuelve a plegar sola:
   * cerrar el detalle no debe esconder la tarjeta a la que vuelve el foco
   */
  protected readonly abiertas = signal<ReadonlySet<Seccion>>(new Set());

  private focoPendiente: FocoPendiente | null = null;
  private focoAlDetalle = false;

  /** «Deshacer» de un «Hecho» del recordatorio que estaba en el detalle: vuelve a él al refrescarse */
  private readonly reelegir = signal<ReelegirPendiente | null>(null);

  constructor() {
    // Al entrar y cada vez que cambia el día con la pestaña abierta: lo de mañana pasa a ser lo de hoy
    const dia = computed(() => diaMadrid(this.fechaActual.ahora()));
    effect(() => {
      dia();
      untracked(() => this.recordatoriosService.cargarPanel());
    });

    // Tras «Hecho», «Posponer» o borrar, la tarjeta se va y el foco caería en <body>: pasa a la
    // siguiente o, si no queda ninguna, al título de su sección
    afterRenderEffect(() => {
      const panel = this.panel();
      const tarjetas = this.tarjetas();
      const pendiente = this.focoPendiente;
      if (!pendiente || panel === null || panel === pendiente.panel) {
        return;
      }
      this.focoPendiente = null;
      const destino = pendiente.candidatos
        .map((id) => tarjetas.find((t) => t.recordatorio().id === id))
        .find((tarjeta) => tarjeta !== undefined);
      if (!destino?.enfocarAccion()) {
        this.enfocarTitulo(pendiente.seccion);
      }
    });

    // Si el panel nuevo no llega, el foco se queda donde está: no salta más tarde con otro cambio
    effect(() => {
      this.recordatoriosService.refrescosFallidos();
      this.focoPendiente = null;
      untracked(() => this.reelegir.set(null));
    });

    // Tras deshacer el «Hecho» del que estaba en el detalle, vuelve a enseñarse en cuanto el panel
    // nuevo lo trae otra vez a su sección (en el de antes aún sale como hecho)
    effect(() => {
      const pendiente = this.reelegir();
      if (!pendiente || this.panel() === pendiente.panel) {
        return;
      }
      untracked(() => {
        this.reelegir.set(null);
        // Al pulsar «Deshacer» el aviso se cierra y el foco cae en <body>; con Ctrl+Z no se mueve
        this.focoAlDetalle = document.activeElement === document.body;
        this.elegir(pendiente.id);
      });
    });

    // Se ha pedido un tipo del que no hay nada (una URL vieja, o se acaba de hacer el último): el
    // filtro se quita solo, en vez de dejar la pantalla vacía sin un chip que lo explique. Con el panel
    // de esta visita: el de la anterior puede no tener lo que se acaba de apuntar
    effect(() => {
      if (this.panelFresco() && this.tipoPedido() !== null && this.tipoElegido() === null) {
        untracked(() => this.filtrarPorTipo(null));
      }
    });

    // El elegido ya no está a la vista (borrado, cambiado desde otra pestaña o fuera del filtro):
    // vuelve el resumen. También espera al panel de esta visita
    effect(() => {
      const id = this.idElegido();
      const p = this.listas();
      if (
        id === null ||
        p === null ||
        !this.panelFresco() ||
        SECCIONES.some((s) => p[s].some((r) => r.id === id))
      ) {
        return;
      }
      untracked(() => {
        // Si el foco estaba en el detalle, que desaparece, caería en <body>
        const focoDentro = this.panelLateral()?.nativeElement.contains(document.activeElement);
        this.elegir(null);
        if (focoDentro) {
          this.inicio().nativeElement.focus({ preventScroll: true });
        }
      });
    });

    // El elegido está en una sección plegada: se abre, para que se vea de qué tarjeta es el detalle
    effect(() => {
      const seccion = this.seleccion()?.seccion;
      if (seccion && !untracked(this.abiertas).has(seccion)) {
        this.abiertas.update((abiertas) => new Set(abiertas).add(seccion));
      }
    });

    // Al abrir un detalle, el foco va a su encabezado: es lo que acaba de aparecer
    afterRenderEffect(() => {
      const detalle = this.detalle();
      this.seleccion();
      if (this.focoAlDetalle && detalle) {
        this.focoAlDetalle = false;
        detalle.enfocarTitulo();
      }
    });
  }

  /** Elige la tarjeta para ver su detalle; si ya era la elegida, vuelve el resumen */
  protected seleccionar(recordatorio: RecordatorioAgenda): void {
    if (this.seleccion()?.recordatorio.id === recordatorio.id) {
      this.elegir(null);
      return;
    }
    this.focoAlDetalle = true;
    this.elegir(recordatorio.id);
  }

  /** «Volver al resumen» (o Esc): el foco vuelve a la tarjeta de la que se vino */
  protected cerrarDetalle(): void {
    const id = this.seleccion()?.recordatorio.id;
    if (id === undefined) {
      return;
    }
    this.elegir(null);
    const tarjeta = this.tarjetas().find((t) => t.recordatorio().id === id);
    // Si se ha plegado a mano la sección en la que está, la tarjeta no admite el foco
    if (!tarjeta?.enfocarTitulo()) {
      this.inicio().nativeElement.focus({ preventScroll: true });
    }
  }

  protected alPulsarEscape(evento: Event): void {
    if (this.seleccion() === null || evento.defaultPrevented || esCampoDeTexto(evento.target)) {
      return;
    }
    // Con un menú o un diálogo abiertos, Esc es para cerrarlos
    const destino = evento.target;
    const enCapa = destino instanceof Element && destino.closest('.cdk-overlay-pane') !== null;
    if (enCapa || document.querySelector('.cdk-overlay-backdrop') !== null) {
      return;
    }
    this.cerrarDetalle();
  }

  /** Un chip: filtra por ese tipo; «Todas» (null) o el que ya estaba puesto quitan el filtro */
  protected filtrarPorTipo(tipo: TipoRecordatorio | null): void {
    const nuevo = tipo === this.tipoElegido() ? null : tipo;
    this.tipoPedido.set(nuevo);
    this.ponerEnUrl(PARAMETRO_TIPO, nuevo);
  }

  /** Un día del calendario: filtra por él (en vez de por la semana); otro clic en el mismo lo quita */
  protected filtrarPorDia(dia: string): void {
    const nuevo = dia === this.diaElegido() ? null : dia;
    this.diaElegido.set(nuevo);
    this.ponerEnUrl(PARAMETRO_DIA, nuevo);
    this.filtrarPorSemana(false);
  }

  /**
   * La cifra de las entrevistas: la lista pasa a enseñar lo mismo que cuenta (las entrevistas
   * pendientes de esta semana). Si ya lo enseñaba, quita los dos filtros
   */
  protected filtrarEntrevistas(): void {
    const quitar = this.entrevistasFiltradas();
    this.tipoPedido.set(quitar ? null : 'Entrevista');
    this.ponerEnUrl(PARAMETRO_TIPO, quitar ? null : 'Entrevista');
    this.diaElegido.set(null);
    this.ponerEnUrl(PARAMETRO_DIA, null);
    this.filtrarPorSemana(!quitar);
  }

  /** «Quitar filtro» del aviso del día (o de la semana): el botón desaparece, así que el foco pasa a los chips */
  protected quitarDia(): void {
    this.diaElegido.set(null);
    this.ponerEnUrl(PARAMETRO_DIA, null);
    this.filtrarPorSemana(false);
    const chip = this.elemento.nativeElement.querySelector<HTMLElement>(
      '[data-filtros] [aria-pressed="true"]',
    );
    (chip ?? this.inicio().nativeElement).focus({ preventScroll: true });
  }

  protected anadirDesdeDetalle(recordatorio: RecordatorioAgenda): void {
    const candidatura = recordatorio.candidatura;
    if (candidatura) {
      this.acciones.nuevo(tipoInicialSegunEstado(candidatura.estado), candidatura).subscribe();
    }
  }

  protected editarDesdeDetalle(recordatorio: RecordatorioAgenda): void {
    // Sin mover el foco a la tarjeta: al cerrarse el diálogo vuelve al botón del panel
    this.acciones.editar(recordatorio, recordatorio.candidatura).subscribe();
  }

  protected reintentar(): void {
    this.recordatoriosService.cargarPanel();
    // El botón desaparece y el foco caería en <body>
    this.inicio().nativeElement.focus({ preventScroll: true });
  }

  protected nuevo(): void {
    // Lo que se apunta suelto suele ser una publicación; los de una candidatura se apuntan en su ficha
    this.acciones.nuevo('PublicacionLinkedIn').subscribe();
  }

  protected hecho(recordatorio: RecordatorioAgenda, seccion: Seccion): void {
    // Si se marca desde su detalle, este se cierra; si se deshace, vuelve a abrirse
    const eraElegido = this.idElegido() === recordatorio.id;
    this.acciones.hecho(
      recordatorio,
      () => this.alQuitarDeSuSitio(recordatorio, seccion),
      eraElegido ? () => this.volverAElegir(recordatorio.id) : undefined,
    );
  }

  protected pendiente(recordatorio: RecordatorioAgenda, seccion: Seccion): void {
    this.acciones.pendiente(recordatorio, () => this.focoAlVecino(recordatorio, seccion));
  }

  protected posponer(recordatorio: RecordatorioAgenda, dia: string, seccion: Seccion): void {
    this.acciones.posponer(recordatorio, dia, () => this.alQuitarDeSuSitio(recordatorio, seccion));
  }

  protected editar(recordatorio: RecordatorioAgenda, seccion: Seccion): void {
    const panel = this.panel();
    this.acciones.editar(recordatorio, recordatorio.candidatura).subscribe((guardado) => {
      // Con otra fecha puede haber cambiado de sección: el foco la sigue
      if (guardado && panel) {
        this.focoPendiente = { panel, candidatos: [recordatorio.id], seccion };
      }
    });
  }

  protected borrar(recordatorio: RecordatorioAgenda, seccion: Seccion): void {
    this.acciones.borrar(recordatorio, () => this.focoAlVecino(recordatorio, seccion));
  }

  /**
   * Hecho o pospuesto: la tarjeta se va a otra sección, quizá plegada. Si era la elegida, vuelve el
   * resumen, y el foco pasa a la tarjeta vecina
   */
  private alQuitarDeSuSitio(recordatorio: RecordatorioAgenda, seccion: Seccion): void {
    if (this.idElegido() === recordatorio.id) {
      this.elegir(null);
    }
    this.focoAlVecino(recordatorio, seccion);
  }

  private volverAElegir(id: number): void {
    const panel = this.panel();
    if (panel !== null) {
      this.reelegir.set({ id, panel });
    }
  }

  private filtrarPorSemana(activa: boolean): void {
    this.semanaPedida.set(activa);
    this.ponerEnUrl(PARAMETRO_SEMANA, activa ? VALOR_SEMANA : null);
  }

  /** Cambia el elegido y lo apunta en la URL */
  private elegir(id: number | null): void {
    this.idElegido.set(id);
    this.ponerEnUrl(PARAMETRO_VER, id);
  }

  /**
   * Apunta en la URL el elegido o un filtro (null lo quita). Con `Location` y no con una navegación
   * del router: esta subiría la página arriba y llevaría el foco a <main>, como al cambiar de pantalla
   */
  private ponerEnUrl(parametro: string, valor: string | number | null): void {
    const url = this.router.parseUrl(this.location.path());
    const { [parametro]: anterior, ...resto } = url.queryParams;
    if ((anterior ?? null) === (valor === null ? null : String(valor))) {
      return;
    }
    url.queryParams = valor === null ? resto : { ...resto, [parametro]: valor };
    this.location.replaceState(this.router.serializeUrl(url));
  }

  /** Se llama al acabar el cambio, con el panel de antes todavía a la vista */
  private focoAlVecino(recordatorio: RecordatorioAgenda, seccion: Seccion): void {
    const panel = this.panel();
    const listas = this.listas();
    if (!panel || !listas) {
      return;
    }
    // La vecina es la que se ve al lado: con un filtro puesto, la siguiente de la lista filtrada
    const lista = listas[seccion];
    const indice = lista.findIndex((r) => r.id === recordatorio.id);
    const candidatos = [lista[indice + 1], lista[indice - 1]]
      .filter((vecino) => vecino !== undefined)
      .map((vecino) => vecino.id);
    this.focoPendiente = { panel, candidatos, seccion };
  }

  private enfocarTitulo(seccion: Seccion): void {
    // En las plegadas el título es su <summary>. No se pintan si se quedan vacías: entonces, al
    // principio de la página
    const titulo = this.elemento.nativeElement.querySelector<HTMLElement>(`#titulo-${seccion}`);
    (titulo ?? this.inicio().nativeElement).focus({ preventScroll: true });
  }
}
