import {
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialog,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Observable } from 'rxjs';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import { Documento, DocumentoSugerido } from '../../interfaces/documento.interface';
import { DocumentosService } from '../../services/documentos.service';
import { carpetaLegible, nombreLegible } from '../../utils/nombre-legible';

export interface DatosSugeridos {
  candidaturaId: number;
  empresa: string;
  /** El texto del botón que lo abre («Buscar el CV», «Buscar otro documento»): es el título */
  titulo: string;
}

/**
 * Propone los CV y cartas de Curriculum que encajan con la candidatura y vincula el elegido con «Usar
 * este». Se cierra con el documento vinculado, o sin nada si se cierra antes.
 */
@Component({
  imports: [MatDialogModule, MatButtonModule, MatProgressBarModule],
  selector: 'app-dialogo-sugeridos',
  templateUrl: './dialogo-sugeridos.html',
})
export class DialogoSugeridos {
  protected readonly datos = inject<DatosSugeridos>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<DialogoSugeridos, Documento>>(MatDialogRef);
  private readonly documentos = inject(DocumentosService);

  protected readonly cargando = this.documentos.cargandoSugeridos;
  protected readonly error = this.documentos.errorSugeridos;
  protected readonly vinculando = this.documentos.vinculando;
  protected readonly errorVincular = signal<string | null>(null);

  protected readonly filas = computed(() =>
    this.documentos.sugeridos().map((sugerido) => ({
      sugerido,
      nombre: nombreLegible(sugerido.nombreArchivo),
      carpeta: carpetaLegible(sugerido.carpeta),
    })),
  );

  /** La API, si ninguno lleva el nombre de la empresa, devuelve los de la semana de la candidatura */
  protected readonly sinCoincidencias = computed(() =>
    this.documentos.sugeridos().every((s) => s.coincidencia === 'MismaSemana'),
  );

  /**
   * La fila cuyo «Usar este» va destacado (y recibe el foco): la primera que no está ya en la ficha y
   * puede ser el CV enviado (la API las ordena de mejor a peor). Si solo hay «los de su semana», ninguna:
   * con Intro se vincularía el CV de otra empresa. -1 si no hay ninguna así.
   */
  protected readonly indiceDestacado = computed(() =>
    this.sinCoincidencias()
      ? -1
      : this.documentos.sugeridos().findIndex((s) => !s.yaVinculado && !s.posteriorAlEnvio),
  );

  // Al llegar la lista, el foco va al «Usar este» destacado: en el PC basta con pulsar Intro
  private readonly botonDestacado = viewChild('destacado', { read: ElementRef });
  private enfocarDestacado = true;

  constructor() {
    this.buscar();
    // Si se cierra mientras busca, la petición ya no le sirve a nadie
    inject(DestroyRef).onDestroy(() => this.documentos.cancelarSugeridos());
    effect(() => {
      const boton = this.botonDestacado()?.nativeElement as HTMLButtonElement | undefined;
      if (boton && this.enfocarDestacado) {
        this.enfocarDestacado = false;
        boton.focus();
      }
    });
  }

  protected buscar(): void {
    this.errorVincular.set(null);
    this.documentos.cargarSugeridos(this.datos.candidaturaId);
  }

  /** Sin cancelar al cerrar: si ya se ha pedido, el vínculo se guarda igual */
  protected usar(sugerido: DocumentoSugerido): void {
    this.errorVincular.set(null);
    this.documentos.vincular(this.datos.candidaturaId, sugerido.rutaRelativa).subscribe({
      next: (documento) => this.dialogRef.close(documento),
      error: (error: ErrorApi) => {
        this.errorVincular.set(error.mensaje);
        // El archivo se ha movido o ya estaba: la lista que se ve está desfasada
        if (error.estado === 409) {
          this.enfocarDestacado = true;
          this.documentos.cargarSugeridos(this.datos.candidaturaId);
        }
      },
    });
  }
}

/** Abre las sugerencias y emite el documento vinculado (o undefined si se cierra sin elegir) */
export function abrirDialogoSugeridos(
  dialog: MatDialog,
  datos: DatosSugeridos,
): Observable<Documento | undefined> {
  return dialog
    .open<DialogoSugeridos, DatosSugeridos, Documento>(DialogoSugeridos, {
      data: datos,
      width: '40rem',
      maxWidth: '95vw',
      // El foco empieza en el diálogo y no en «Cerrar» (con Intro se cerraría): cuando llega la lista,
      // pasa al «Usar este» destacado
      autoFocus: 'dialog',
      // En el móvil ocupa toda la pantalla (styles.css): la lista llega a 10 filas
      panelClass: 'dialogo-movil-completo',
    })
    .afterClosed();
}
