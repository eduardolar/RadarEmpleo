import {
  afterNextRender,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { confirmarSalirSinGuardar } from '../../../shared/components/dialogo-confirmacion/dialogo-confirmacion';
import { ConCambiosSinGuardar } from '../../../shared/guards/sin-cambios.guard';
import { AvisosService } from '../../../shared/services/avisos.service';
import { FormularioCandidatura } from '../../components/formulario-candidatura/formulario-candidatura';
import { GuardarCandidatura } from '../../interfaces/candidatura.interface';
import { CandidaturasService } from '../../services/candidaturas.service';

@Component({
  imports: [CabeceraPagina, FormularioCandidatura],
  selector: 'app-apuntar-page',
  templateUrl: './apuntar-page.component.html',
})
export default class ApuntarPageComponent implements ConCambiosSinGuardar {
  private readonly candidaturasService = inject(CandidaturasService);
  private readonly avisos = inject(AvisosService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formulario = viewChild.required(FormularioCandidatura);

  protected readonly guardando = signal(false);
  protected readonly erroresServidor = signal<Record<string, string[]>>({});

  constructor() {
    // La página solo sirve para escribir: se empieza en el enlace sin tener que tocarlo. Va después
    // de que el layout lleve el foco a <main> al cambiar de página
    const elemento = inject<ElementRef<HTMLElement>>(ElementRef);
    afterNextRender(() =>
      elemento.nativeElement.querySelector<HTMLInputElement>('input[formcontrolname]')?.focus(),
    );
  }

  /** Para el candado de la ruta. Mientras guarda no hay nada que perder: el alta sigue adelante */
  puedeSalir(): boolean | Observable<boolean> {
    return (
      this.guardando() || !this.formulario().tieneCambios() || confirmarSalirSinGuardar(this.dialog)
    );
  }

  /** Alta rápida: al guardar lleva a la ficha para completar el resto si hace falta */
  protected guardar(datos: GuardarCandidatura): void {
    this.guardando.set(true);
    this.erroresServidor.set({});

    // Sin cancelar al destruirse: salir de la página no debe perder el alta que ya se está guardando
    this.candidaturasService.crear(datos).subscribe({
      next: (detalle) => {
        this.avisos.exito('Candidatura apuntada en «Detectada».');
        // Si ya se ha ido a otra página, no se le saca de ella
        if (!this.destroyRef.destroyed) {
          this.router.navigate(['/candidaturas', detalle.id]);
        }
      },
      error: (error: ErrorApi) => {
        this.guardando.set(false);
        if (Object.keys(error.errores).length > 0 && !this.destroyRef.destroyed) {
          this.erroresServidor.set(error.errores);
        } else {
          this.avisos.error(error.mensaje);
        }
      },
    });
  }
}
