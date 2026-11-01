import { Component, ElementRef, inject, viewChild } from '@angular/core';
import { SistemaService } from '../../services/sistema.service';

/** Indicador de si la API responde y en qué modo está (Local o Demo) */
@Component({
  selector: 'app-estado-api',
  templateUrl: './estado-api.html',
  host: { class: 'block' },
})
export class EstadoApi {
  protected readonly sistemaService = inject(SistemaService);

  private readonly indicador = viewChild.required<ElementRef<HTMLElement>>('indicador');

  constructor() {
    this.sistemaService.cargarInfoSiFalta();
  }

  protected reintentar(): void {
    this.sistemaService.cargarInfo();
    this.indicador().nativeElement.focus();
  }
}
