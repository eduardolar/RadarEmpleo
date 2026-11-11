import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
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
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { EnlaceVolver } from '../../../shared/components/enlace-volver/enlace-volver';
import { SeccionPlegable } from '../../../shared/components/seccion-plegable/seccion-plegable';
import { AvisosService } from '../../../shared/services/avisos.service';
import { ListaImportacion } from '../../components/lista-importacion/lista-importacion';
import { ResultadoAplicacion } from '../../interfaces/importacion.interface';
import { ImportacionService } from '../../services/importacion.service';
import {
  AVISO_RADAR,
  EXPLICACION_PARA_REVISAR,
  EXPLICACION_SIN_LEER,
  EXPLICACION_YA_EXISTEN,
  filasAplicables,
  filasConResultado,
  lineasResumen,
  textoBotonAplicar,
  textoEnviosEnTexto,
  textoNadaQueAnadir,
  textoResultado,
  tituloNadaQueAplicar,
} from '../../utils/resumen-importacion';

@Component({
  imports: [
    CabeceraPagina,
    EnlaceVolver,
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
  private readonly documento = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly inicio = viewChild.required<ElementRef<HTMLElement>>('inicio');

  protected readonly vista = this.importacion.vistaPrevia;
  protected readonly cargando = this.importacion.cargando;
  protected readonly error = this.importacion.error;
  protected readonly aplicando = this.importacion.aplicando;

  /** Solo de esta visita a la página: al volver a entrar ya no viene al caso */
  protected readonly resultado = signal<ResultadoAplicacion | null>(null);
  protected readonly textoResultado = textoResultado;
  protected readonly avisoRadar = AVISO_RADAR;
  protected readonly explicacionSinLeer = EXPLICACION_SIN_LEER;
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
    return vista ? textoEnviosEnTexto(vista.resumen.enviosEnTexto) : null;
  });

  constructor() {
    this.importacion.previsualizar();
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
    this.importacion.previsualizar();
    this.enfocarInicio();
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
        if (this.documento.activeElement === this.documento.body) {
          this.enfocarInicio();
        }
      },
      { injector: this.injector },
    );
  }

  private enfocarInicio(): void {
    this.inicio().nativeElement.focus({ preventScroll: true });
  }
}
