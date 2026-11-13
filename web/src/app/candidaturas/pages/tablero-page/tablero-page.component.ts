import { CdkDrag, CdkDragDrop, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { CdkScrollable } from '@angular/cdk/scrolling';
import {
  afterNextRender,
  afterRenderEffect,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { debounceTime, distinctUntilChanged, map } from 'rxjs';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { senalEscritorio } from '../../../shared/utils/pantalla';
import { abrirDialogoCandidatura } from '../../components/dialogo-candidatura/dialogo-candidatura';
import { TarjetaCandidatura } from '../../components/tarjeta-candidatura/tarjeta-candidatura';
import { CandidaturaResumen, EstadoCandidatura } from '../../interfaces/candidatura.interface';
import { AlDeshacer, CambioEstadoService } from '../../services/cambio-estado.service';
import { CandidaturasService } from '../../services/candidaturas.service';
import { FiltroMovil, VistaTableroService } from '../../services/vista-tablero.service';
import { agruparPorEstado, ColumnaTablero } from '../../utils/agrupar-por-estado';
import { ETIQUETAS_ESTADO } from '../../utils/etiquetas';
import { puedePasar } from '../../utils/transiciones-permitidas';

/** Columnas que cierran el proceso: en el PC salen plegadas en una tira para que quepan las demás */
const PLEGADAS_DE_SERIE: ReadonlySet<EstadoCandidatura> = new Set([
  'Oferta',
  'Descartada',
  'Cerrada',
]);

@Component({
  imports: [
    CabeceraPagina,
    TarjetaCandidatura,
    CdkDropListGroup,
    CdkDropList,
    CdkDrag,
    CdkScrollable,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  selector: 'app-tablero-page',
  templateUrl: './tablero-page.component.html',
})
export default class TableroPageComponent {
  private readonly candidaturasService = inject(CandidaturasService);
  private readonly cambioEstado = inject(CambioEstadoService);
  private readonly vista = inject(VistaTableroService);
  private readonly dialog = inject(MatDialog);
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly documento = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly inicio = viewChild.required<ElementRef<HTMLElement>>('inicio');

  protected readonly candidaturas = this.candidaturasService.candidaturas;
  protected readonly cargando = this.candidaturasService.cargando;
  protected readonly error = this.candidaturasService.error;
  protected readonly etiquetas = ETIQUETAS_ESTADO;

  // Se empieza con la búsqueda y el filtro que había al salir (p. ej. al volver de una ficha)
  protected readonly busqueda = new FormControl(this.vista.texto(), { nonNullable: true });
  protected readonly textoBuscado = this.vista.texto;

  protected readonly esEscritorio = senalEscritorio();

  protected readonly columnas = computed(() => agruparPorEstado(this.candidaturas()));

  /**
   * Columnas plegables que se han desplegado a mano (o al mover una tarjeta a ellas). Es solo
   * presentación: se pliegan las de PLEGADAS_DE_SERIE y las vacías, para que el tablero quepa sin
   * desplazamiento lateral en el PC
   */
  protected readonly expandidas = signal<ReadonlySet<EstadoCandidatura>>(new Set());

  /** Candidatura que se está arrastrando, para atenuar las columnas donde no se puede soltar */
  protected readonly arrastrando = signal<CandidaturaResumen | null>(null);

  protected readonly filtroMovil = this.vista.filtroMovil;
  protected readonly filtrosMovil = computed(() => [
    { valor: 'Todas' as FiltroMovil, etiqueta: 'Todas', total: this.candidaturas().length },
    ...this.columnas().map((columna) => ({
      valor: columna.estado as FiltroMovil,
      etiqueta: ETIQUETAS_ESTADO[columna.estado],
      total: columna.candidaturas.length,
    })),
  ]);
  protected readonly listaMovil = computed(() => {
    const filtro = this.filtroMovil();
    return filtro === 'Todas'
      ? this.candidaturas()
      : this.candidaturas().filter((c) => c.estado === filtro);
  });

  /** Tarjeta movida con «Mover a…»: al cambiar de columna se vuelve a crear y hay que devolverle el foco */
  private readonly enfocarTrasMover = signal<{
    id: number;
    destino: EstadoCandidatura;
  } | null>(null);

  protected readonly sinCandidaturas = computed(
    () => !this.cargando() && !this.error() && this.candidaturas().length === 0,
  );

  /** Sin ninguna candidatura (y sin búsqueda): el estado vacío lleva su propio «Nueva candidatura» */
  protected readonly tableroVacio = computed(() => this.sinCandidaturas() && !this.textoBuscado());

  constructor() {
    this.candidaturasService.cargar({ texto: this.vista.texto() });

    this.busqueda.valueChanges
      .pipe(
        debounceTime(300),
        map((texto) => texto.trim()),
        distinctUntilChanged(),
        takeUntilDestroyed(),
      )
      .subscribe((texto) => {
        if (texto === this.vista.texto()) {
          return;
        }
        this.vista.buscar(texto);
        this.candidaturasService.cargar({ texto });
      });

    // Sin esto, el foco del teclado se queda en <body> porque el botón «Mover a…» desaparece
    afterRenderEffect(() => {
      const pendiente = this.enfocarTrasMover();
      const movida = this.candidaturas().find((c) => c.id === pendiente?.id);
      if (!pendiente || movida?.estado !== pendiente.destino) {
        return;
      }
      this.enfocarTrasMover.set(null);
      const raiz = this.elemento.nativeElement;
      // En el móvil con un estado filtrado la tarjeta sale de la lista: el foco va al filtro activo
      const destino =
        raiz.querySelector<HTMLElement>(`[data-candidatura="${pendiente.id}"] a`) ??
        raiz.querySelector<HTMLElement>('[aria-pressed="true"]');
      destino?.focus();
    });
  }

  /** Las terminales y las vacías salen plegadas, salvo que se hayan desplegado */
  protected plegable(columna: ColumnaTablero): boolean {
    return PLEGADAS_DE_SERIE.has(columna.estado) || columna.candidaturas.length === 0;
  }

  protected plegada(columna: ColumnaTablero): boolean {
    return this.plegable(columna) && !this.expandidas().has(columna.estado);
  }

  /** El botón pulsado desaparece (la tira se cambia por la cabecera o al revés): el foco va al otro */
  protected alternarColumna(estado: EstadoCandidatura): void {
    this.expandidas.update((actuales) => {
      const nuevas = new Set(actuales);
      if (!nuevas.delete(estado)) {
        nuevas.add(estado);
      }
      return nuevas;
    });
    afterNextRender(
      () =>
        this.elemento.nativeElement
          .querySelector<HTMLElement>(`section[data-estado="${estado}"] [data-plegar]`)
          ?.focus(),
      { injector: this.injector },
    );
  }

  /** Al mover una tarjeta a una columna plegada, se despliega para que se vea (y reciba el foco) */
  private desplegar(estado: EstadoCandidatura): void {
    if (!this.expandidas().has(estado)) {
      this.expandidas.update((actuales) => new Set(actuales).add(estado));
    }
  }

  /** Para cdkDropListEnterPredicate: solo deja entrar en las columnas permitidas */
  protected readonly sePuedeSoltar = (
    drag: CdkDrag<CandidaturaResumen>,
    drop: CdkDropList<EstadoCandidatura>,
  ): boolean => puedePasar(drag.data.estado, drop.data);

  protected puedeRecibir(estado: EstadoCandidatura): boolean {
    const candidatura = this.arrastrando();
    return (
      candidatura === null ||
      candidatura.estado === estado ||
      puedePasar(candidatura.estado, estado)
    );
  }

  protected soltar(
    evento: CdkDragDrop<EstadoCandidatura, EstadoCandidatura, CandidaturaResumen>,
  ): void {
    this.arrastrando.set(null);
    // Al soltar fuera, el CDK entrega la última columna por la que pasó: eso no es moverla
    if (!evento.isPointerOverContainer || evento.previousContainer === evento.container) {
      return;
    }
    // La tarjeta no se mueve aquí: cambia de columna cuando el servicio actualiza la lista
    this.desplegar(evento.container.data);
    this.cambioEstado.mover(
      evento.item.data,
      evento.container.data,
      undefined,
      this.alDeshacer(evento.item.data.id),
    );
  }

  protected mover(candidatura: CandidaturaResumen, destino: EstadoCandidatura): void {
    this.enfocarTrasMover.set({ id: candidatura.id, destino });
    this.desplegar(destino);
    this.cambioEstado.mover(
      candidatura,
      destino,
      (resultado) => {
        if (resultado !== 'hecho') {
          this.enfocarTrasMover.set(null);
        }
        if (resultado === 'rechazado') {
          this.recuperarFoco();
        }
      },
      this.alDeshacer(candidatura.id),
    );
  }

  /** Deshecho el cambio desde el aviso, la tarjeta vuelve a su columna y se lleva el foco (no a <body>) */
  private alDeshacer(id: number): AlDeshacer {
    return (estado) => {
      if (this.destroyRef.destroyed) {
        return;
      }
      this.desplegar(estado);
      this.enfocarTrasMover.set({ id, destino: estado });
    };
  }

  /**
   * La tarjeta rechazada vuelve a su columna y se crea de nuevo: si tenía el foco, caería en <body>.
   * Si la API ha tardado y mientras tanto se ha seguido trabajando en otro sitio, no se toca.
   */
  private recuperarFoco(): void {
    if (this.destroyRef.destroyed) {
      return;
    }
    afterNextRender(
      () => {
        if (this.documento.activeElement === this.documento.body) {
          this.enfocarInicio();
        }
      },
      { injector: this.injector },
    );
  }

  protected nuevaCandidatura(): void {
    abrirDialogoCandidatura(this.dialog).subscribe();
  }

  protected reintentar(): void {
    this.candidaturasService.cargar();
    this.enfocarInicio();
  }

  protected limpiarBusqueda(): void {
    this.busqueda.setValue('');
    this.enfocarInicio();
  }

  /** Esos botones desaparecen y el foco caería en <body>: se sigue desde el principio de la página */
  private enfocarInicio(): void {
    this.inicio().nativeElement.focus({ preventScroll: true });
  }

  protected filtrar(filtro: FiltroMovil): void {
    this.vista.filtrar(filtro);
  }

  /** Para el mensaje de lista vacía: «Detectada», «Enviada»... */
  protected etiquetaFiltro(filtro: FiltroMovil): string {
    return filtro === 'Todas' ? 'Todas' : ETIQUETAS_ESTADO[filtro];
  }
}
