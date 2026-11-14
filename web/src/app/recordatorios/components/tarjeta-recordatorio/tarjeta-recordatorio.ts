import { Component, computed, ElementRef, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { RouterLink } from '@angular/router';
import { Recordatorio } from '../../../candidaturas/interfaces/candidatura.interface';
import {
  ETIQUETAS_ESTADO,
  ETIQUETAS_TIPO_RECORDATORIO,
} from '../../../candidaturas/utils/etiquetas';
import { CandidaturaBreve, RecordatorioAgenda } from '../../../hoy/interfaces/panel-hoy.interface';
import { diaCorto, diaMadrid, horaMadrid } from '../../../shared/utils/fechas-madrid';
import { opcionesPosponer } from '../../utils/opciones-posponer';
import { tituloAccesible, tituloVisible } from '../../utils/titulo-visible';
import { InsigniaTipo } from '../insignia-tipo/insignia-tipo';

// El título cuando lleva a algún sitio (enlace a la ficha o botón que abre el detalle). Sin subrayado:
// tinta en reposo y el color de los enlaces al pasar el ratón o con el foco. El ::after le da 48 px de
// alto para acertar con el dedo sin que la tarjeta crezca
const CLASES_TITULO =
  "relative mt-1.5 block w-fit max-w-full rounded-sm text-base leading-snug font-semibold transition-colors after:absolute after:inset-x-0 after:-inset-y-3 after:content-[''] hover:text-enlace focus-visible:text-enlace focus-visible:ring-[3px] focus-visible:ring-primary focus-visible:ring-offset-2 focus-visible:ring-offset-surface-container-lowest focus-visible:outline-none motion-reduce:transition-none";

/**
 * Un recordatorio como tarjeta, igual en Hoy y en la ficha: qué es, cuándo toca y sus acciones.
 * «Hecho» va en un toque; «Posponer» y «⋮» abren un menú. No guarda nada: avisa a quien la usa.
 */
@Component({
  imports: [RouterLink, MatButtonModule, MatMenuModule, InsigniaTipo],
  selector: 'app-tarjeta-recordatorio',
  templateUrl: './tarjeta-recordatorio.html',
  host: { class: 'block min-w-0' },
})
export class TarjetaRecordatorio {
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly recordatorio = input.required<Recordatorio | RecordatorioAgenda>();
  /** El día de Madrid de hoy, como «2026-11-13»: decide qué está vencido y a qué día se pospone */
  readonly hoy = input.required<string>();
  /**
   * «Hecho» lleva el contorno con acento (del color de los enlaces): lo que toca hoy. Nunca va
   * relleno: el único relleno de Hoy es «Nuevo recordatorio»
   */
  readonly destacada = input(false);
  readonly conPosponer = input(false);
  /**
   * Enseña de qué candidatura es, con el título como enlace a su ficha y el enlace a la oferta. El
   * título que se pone solo se queda en el puesto: la empresa ya va en la línea de debajo
   */
  readonly conCandidatura = input(false);
  /** Va bajo un encabezado que ya dice el día: enseña solo la hora */
  readonly soloHora = input(false);
  /**
   * Hay un panel al lado para su detalle: el título pasa a ser un botón que la elige (en vez del enlace
   * a la ficha) y un clic en cualquier parte de la tarjeta que no sea un botón hace lo mismo
   */
  readonly seleccionable = input(false);
  /** Es la que se ve en el panel de detalle */
  readonly seleccionada = input(false);

  readonly hecho = output<void>();
  readonly pendiente = output<void>();
  /** El día al que se pospone, como «2026-11-14» */
  readonly posponer = output<string>();
  readonly editar = output<void>();
  readonly borrar = output<void>();
  /** Se ha pulsado para ver su detalle (o para dejar de verlo, si ya era la elegida) */
  readonly seleccionar = output<void>();

  protected readonly clasesTitulo = CLASES_TITULO;

  protected readonly etiquetaTipo = computed(
    () => ETIQUETAS_TIPO_RECORDATORIO[this.recordatorio().tipo],
  );
  protected readonly etiquetasEstado = ETIQUETAS_ESTADO;

  /**
   * Un seguimiento hecho solo al mover la candidatura (a Entrevista u Oferta): el estado al que pasó,
   * para decirlo en la tarjeta en vez de que parezca hecho a mano
   */
  protected readonly cerradoAlPasarA = computed(() => {
    const r = this.recordatorio();
    return r.hecho && 'cerradoAlPasarA' in r ? (r.cerradoAlPasarA ?? null) : null;
  });

  protected readonly candidatura = computed<CandidaturaBreve | null>(() => {
    const r = this.recordatorio();
    return this.conCandidatura() && 'candidatura' in r ? r.candidatura : null;
  });

  /** Lo que se lee en la tarjeta: el título guardado o, si es el que se pone solo, el puesto */
  protected readonly titulo = computed(() =>
    tituloVisible(this.recordatorio(), this.candidatura()),
  );
  /** El título entero, para los aria-label: el lector de pantalla no pierde el contexto */
  protected readonly tituloCompleto = computed(() =>
    tituloAccesible(this.recordatorio(), this.candidatura()),
  );

  private readonly dia = computed(() => diaMadrid(this.recordatorio().fechaHora));
  protected readonly hora = computed(() => horaMadrid(this.recordatorio().fechaHora));

  /** Pendiente y de un día anterior a hoy */
  protected readonly vencido = computed(
    () => !this.recordatorio().hecho && this.dia() < this.hoy(),
  );

  /** «Hoy», «vie 14 nov» o, si es de otro año, «vie 14 nov 2027» */
  protected readonly textoDia = computed(() => {
    const dia = this.dia();
    if (dia === this.hoy()) {
      return 'Hoy';
    }
    const anio = dia.slice(0, 4);
    return anio === this.hoy().slice(0, 4) ? diaCorto(dia) : `${diaCorto(dia)} ${anio}`;
  });

  protected readonly opcionesPosponer = computed(() => opcionesPosponer(this.hoy(), this.dia()));

  /** Lleva el foco a su acción principal («Hecho» o «Pendiente otra vez»). Devuelve false si no la hay */
  enfocarAccion(): boolean {
    const boton = this.elemento.nativeElement.querySelector<HTMLElement>('[data-accion]');
    boton?.focus();
    return boton !== null;
  }

  /** Lleva el foco a su título, si es un enlace o un botón. Devuelve false si no ha podido */
  enfocarTitulo(): boolean {
    const titulo = this.elemento.nativeElement.querySelector<HTMLElement>('[data-titulo]');
    titulo?.focus();
    return titulo !== null && document.activeElement === titulo;
  }

  protected alPulsarTarjeta(evento: MouseEvent): void {
    if (!this.seleccionable()) {
      return;
    }
    // Los botones y los enlaces de dentro hacen lo suyo, y quien arrastra para copiar un texto no
    // está eligiendo la tarjeta
    const destino = evento.target;
    if (destino instanceof Element && destino.closest('a, button')) {
      return;
    }
    if (window.getSelection()?.toString()) {
      return;
    }
    this.seleccionar.emit();
  }
}
