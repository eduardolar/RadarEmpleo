import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatTabsModule } from '@angular/material/tabs';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { CandidaturaDetalle } from '../../../candidaturas/interfaces/candidatura.interface';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import { AvisosService } from '../../../shared/services/avisos.service';
import { senalEscritorio } from '../../../shared/utils/pantalla';
import { abrirDialogoSugeridos } from '../dialogo-sugeridos/dialogo-sugeridos';
import { Documento, TipoDocumento } from '../../interfaces/documento.interface';
import { DocumentosService } from '../../services/documentos.service';
import {
  documentoVigente,
  otrosDocumentos,
  pestanasConDocumento,
  TIPOS_PESTANA,
  TipoPestana,
} from '../../utils/documento-vigente';
import {
  carpetaLegible,
  ETIQUETAS_TIPO_DOCUMENTO,
  nombreLegible,
  TIPO_DOCUMENTO_CON_ARTICULO,
} from '../../utils/nombre-legible';
import { urlArchivo, urlVisorSegura } from '../../utils/url-archivo';

/** Lo que pinta el panel de cada documento, ya preparado */
interface VistaDocumento {
  documento: Documento;
  nombre: string;
  carpeta: string;
  etiquetaTipo: string;
  urlArchivo: string;
  /** Solo en escritorio y con un PDF que está en su sitio: si es null, el iframe ni se crea */
  urlVisor: SafeResourceUrl | null;
  /** Se puede abrir en otra pestaña (lo mismo que el visor: escritorio y PDF en su sitio) */
  sePuedeAbrir: boolean;
  /** Es un Word que está en su sitio: se ofrece descargarlo */
  sePuedeDescargar: boolean;
  tituloVisor: string;
}

/**
 * Panel «CV y carta» de la ficha: una pestaña por tipo (CV, Carta) con el documento vigente, que en
 * escritorio se ve dentro de la ficha; los demás documentos en una lista corta; y el botón para buscar
 * uno en la carpeta Curriculum. Quitar un documento nunca borra el archivo.
 */
@Component({
  imports: [NgTemplateOutlet, MatButtonModule, MatTabsModule],
  selector: 'app-panel-documentos',
  templateUrl: './panel-documentos.html',
})
export class PanelDocumentos {
  private readonly documentosService = inject(DocumentosService);
  private readonly dialog = inject(MatDialog);
  private readonly avisos = inject(AvisosService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly titulo = viewChild.required<ElementRef<HTMLElement>>('titulo');

  readonly candidatura = input.required<CandidaturaDetalle>();

  protected readonly esEscritorio = senalEscritorio();

  private readonly documentos = computed(() => this.candidatura().documentos);
  protected readonly pestanas = computed(() => pestanasConDocumento(this.documentos()));

  // La pestaña que se ha elegido; si deja de existir (se quita su documento), vale la primera
  private readonly pestanaElegida = signal<TipoPestana>('CV');
  protected readonly indicePestana = computed(() =>
    Math.max(0, this.pestanas().indexOf(this.pestanaElegida())),
  );

  protected readonly vistasPestana = computed(() =>
    this.pestanas().map((tipo) => this.vista(documentoVigente(this.documentos(), tipo)!)),
  );
  protected readonly otros = computed(() =>
    otrosDocumentos(this.documentos()).map((d) => this.vista(d)),
  );

  protected readonly hayDocumentos = computed(() => this.documentos().length > 0);

  /** Sin la carpeta no se encuentra ninguno: se dice una vez arriba y no en cada documento */
  protected readonly hayCarpeta = computed(() => this.candidatura().hayCarpetaCurriculum);

  protected readonly quitando = signal<number | null>(null);

  // Una SafeResourceUrl nueva en cada recarga de la ficha haría que el iframe volviera a pedir el PDF
  private readonly urlsVisor = new Map<number, SafeResourceUrl>();

  protected elegirPestana(indice: number): void {
    const tipo = this.pestanas()[indice];
    if (tipo) {
      this.pestanaElegida.set(tipo);
    }
  }

  protected buscar(): void {
    const c = this.candidatura();
    // El título del diálogo repite el botón que se ha pulsado
    const titulo = this.hayDocumentos() ? 'Buscar otro documento' : 'Buscar el CV';
    abrirDialogoSugeridos(this.dialog, {
      candidaturaId: c.id,
      empresa: c.empresa,
      titulo,
    }).subscribe((documento) => {
      if (!documento) {
        return;
      }
      // c.documentos es como estaba la ficha antes de añadirlo: sirve para saber si sustituye a otro
      this.avisos.exito(textoAnadido(documento, c.documentos));
      this.irAPestana(documento);
      // En la ficha vacía, «Buscar el CV» desaparece al recargarla y el foco caería en <body>
      if (c.documentos.length === 0) {
        this.enfocarTitulo();
      }
    });
  }

  protected quitar(documento: Documento): void {
    if (this.quitando() !== null) {
      return;
    }
    this.quitando.set(documento.id);
    this.documentosService.quitar(documento).subscribe({
      next: () => {
        this.quitando.set(null);
        const mensaje = `He quitado ${sujetoQuitado(documento)} de la ficha. El archivo sigue en su carpeta.`;
        // Deshacer es volver a añadirlo, y eso lo deja como vigente: solo cuadra si lo era. Si el
        // archivo ya no estaba, tampoco se podría
        if (documento.vigente && documento.encontrado) {
          this.avisos.exito(mensaje, {
            texto: 'Deshacer',
            alPulsar: () => this.deshacerQuitar(documento),
          });
        } else {
          this.avisos.exito(mensaje);
        }
        this.enfocarTitulo();
      },
      error: (error: ErrorApi) => {
        this.quitando.set(null);
        this.avisos.error(error.mensaje);
      },
    });
  }

  /** Lo vuelve a añadir con su mismo tipo */
  private deshacerQuitar(documento: Documento): void {
    this.documentosService
      .vincular(documento.candidaturaId, documento.rutaRelativa, documento.tipo)
      .subscribe({
        next: () => {
          this.avisos.exito(`${mayuscula(sujetoQuitado(documento))} vuelve a estar en la ficha.`);
          this.irAPestana(documento);
          // El botón «Deshacer» del aviso desaparece con él
          this.enfocarTitulo();
        },
        error: (error: ErrorApi) => this.avisos.error(error.mensaje),
      });
  }

  private irAPestana(documento: Documento): void {
    if (esTipoPestana(documento.tipo)) {
      this.pestanaElegida.set(documento.tipo);
    }
  }

  private vista(documento: Documento): VistaDocumento {
    const etiquetaTipo = ETIQUETAS_TIPO_DOCUMENTO[documento.tipo];
    const conVisor = this.esEscritorio() && documento.encontrado && documento.extension === 'pdf';
    return {
      documento,
      nombre: nombreLegible(documento.nombreArchivo),
      carpeta: carpetaLegible(documento.carpeta),
      etiquetaTipo,
      urlArchivo: urlArchivo(documento.id),
      urlVisor: conVisor ? this.urlVisor(documento.id) : null,
      sePuedeAbrir: conVisor,
      sePuedeDescargar: documento.encontrado && documento.extension !== 'pdf',
      tituloVisor: `${etiquetaTipo} para ${this.candidatura().empresa}, PDF`,
    };
  }

  private urlVisor(id: number): SafeResourceUrl {
    let url = this.urlsVisor.get(id);
    if (!url) {
      url = urlVisorSegura(this.sanitizer, id);
      this.urlsVisor.set(id, url);
    }
    return url;
  }

  /** El botón pulsado desaparece y el foco caería en <body>: va al título del panel */
  private enfocarTitulo(): void {
    if (this.destroyRef.destroyed) {
      return;
    }
    afterNextRender(() => this.titulo().nativeElement.focus({ preventScroll: true }), {
      injector: this.injector,
    });
  }
}

function esTipoPestana(tipo: TipoDocumento): tipo is TipoPestana {
  return (TIPOS_PESTANA as readonly TipoDocumento[]).includes(tipo);
}

function mayuscula(texto: string): string {
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}

/** «el CV», o «el CV anterior» si no era el vigente */
function sujetoQuitado(documento: Documento): string {
  const sujeto = TIPO_DOCUMENTO_CON_ARTICULO[documento.tipo];
  return documento.vigente ? sujeto : `${sujeto} anterior`;
}

/**
 * Aviso al añadir un documento: dónde queda y, si sustituye al CV o la carta que había, a dónde va el
 * de antes. Por ejemplo: «El CV ya está en la ficha. El anterior pasa a «Otros documentos».»
 */
export function textoAnadido(documento: Documento, antes: readonly Documento[]): string {
  const sujeto = mayuscula(TIPO_DOCUMENTO_CON_ARTICULO[documento.tipo]);
  if (!esTipoPestana(documento.tipo)) {
    return `${sujeto} ya está en la ficha, en «Otros documentos».`;
  }
  if (documentoVigente(antes, documento.tipo) === null) {
    return `${sujeto} ya está en la ficha.`;
  }
  const anterior = documento.tipo === 'Carta' ? 'La anterior' : 'El anterior';
  return `${sujeto} ya está en la ficha. ${anterior} pasa a «Otros documentos».`;
}
