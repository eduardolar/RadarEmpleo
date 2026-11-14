import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { diaCorto, diaLargo } from '../../../shared/utils/fechas-madrid';
import { ResumenHoy as DatosResumen } from '../../interfaces/panel-hoy.interface';
import { diasCalendario, siguienteFueraDeSemana } from '../../utils/dias-semana';

// Una cifra que se puede pulsar: toda la caja es la zona de toque (el ::after del enlace o del botón
// la cubre), con el fondo algo más claro al pasar el ratón y el anillo de foco en la caja
const CLASES_CAJA_PULSABLE =
  'group relative transition-colors hover:bg-surface-container-high has-focus-visible:ring-[3px] has-focus-visible:ring-primary motion-reduce:transition-none';
const CLASES_CIFRA_PULSABLE =
  "flex w-full cursor-pointer items-start justify-between gap-2 text-left outline-none after:absolute after:inset-0 after:content-['']";

/**
 * El resumen de Hoy: tres cifras (candidaturas activas, entrevistas pendientes de esta semana y tasa de
 * respuesta) y la semana de lunes a domingo con una marca en los días con algo pendiente. Las cifras
 * llevan a donde se ve el detalle y los días filtran la lista; el filtro lo guarda la página.
 */
@Component({
  imports: [NgTemplateOutlet, RouterLink, MatButtonModule],
  selector: 'app-resumen-hoy',
  templateUrl: './resumen-hoy.html',
  host: { class: 'block' },
})
export class ResumenHoy {
  readonly resumen = input.required<DatosResumen>();
  /** El día de Madrid de hoy, como «2026-11-13» */
  readonly hoy = input.required<string>();
  /** El día por el que se está filtrando la lista, si lo hay */
  readonly diaElegido = input<string | null>(null);
  /** Hay entrevistas pendientes esta semana: la cifra de las entrevistas se puede pulsar */
  readonly entrevistasFiltrables = input(false);
  /** La lista ya enseña solo las entrevistas de esta semana */
  readonly entrevistasFiltradas = input(false);

  /** Se ha pulsado un día («2026-11-18») del calendario o el de «Lo siguiente» */
  readonly elegirDia = output<string>();
  readonly filtrarEntrevistas = output<void>();

  protected readonly clasesCaja = CLASES_CAJA_PULSABLE;
  protected readonly clasesCifra = CLASES_CIFRA_PULSABLE;

  protected readonly dias = computed(() => diasCalendario(this.resumen().semana, this.hoy()));

  /** «Lo siguiente», cuando cae pasada esta semana: el día para pulsar, para ver y para leer */
  protected readonly siguiente = computed(() => {
    const { semana, siguienteDia } = this.resumen();
    const dia = siguienteFueraDeSemana(semana, this.hoy(), siguienteDia);
    return dia === null ? null : { dia, corto: diaCorto(dia), largo: diaLargo(dia) };
  });
}
