import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { ETIQUETAS_ESTADO, ETIQUETAS_MOTIVO_DESCARTE } from '../../../candidaturas/utils/etiquetas';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { EnlaceVolver } from '../../../shared/components/enlace-volver/enlace-volver';
import { FilaBarra, ListaBarras } from '../../components/lista-barras/lista-barras';
import { EstadisticasService } from '../../services/estadisticas.service';
import { etiquetaSemana, textoDias } from '../../utils/barras';

/** Cómo va la búsqueda: página de lectura, sin acción principal (por eso no lleva relleno lima) */
@Component({
  imports: [
    CabeceraPagina,
    EnlaceVolver,
    ListaBarras,
    MatButtonModule,
    MatProgressBarModule,
    RouterLink,
  ],
  selector: 'app-estadisticas-page',
  templateUrl: './estadisticas-page.component.html',
})
export default class EstadisticasPageComponent {
  private readonly estadisticasService = inject(EstadisticasService);

  protected readonly estadisticas = this.estadisticasService.estadisticas;
  protected readonly cargando = this.estadisticasService.cargando;
  protected readonly error = this.estadisticasService.error;

  constructor() {
    this.estadisticasService.cargar();
  }

  protected readonly porEstado = computed<FilaBarra[]>(() =>
    (this.estadisticas()?.porEstado ?? []).map((e) => ({
      etiqueta: ETIQUETAS_ESTADO[e.estado],
      valor: e.total,
      estado: e.estado,
    })),
  );

  protected readonly porSemana = computed<FilaBarra[]>(() =>
    (this.estadisticas()?.enviadasPorSemana ?? []).map((s) => ({
      etiqueta: etiquetaSemana(s.lunes),
      valor: s.total,
    })),
  );

  protected readonly enviadasEnOchoSemanas = computed(() =>
    this.porSemana().reduce((suma, s) => suma + s.valor, 0),
  );

  protected readonly motivos = computed<FilaBarra[]>(() =>
    (this.estadisticas()?.motivosDescarte ?? []).map((m) => ({
      etiqueta: ETIQUETAS_MOTIVO_DESCARTE[m.motivo],
      valor: m.total,
    })),
  );

  /** «1 de 4 (25 %) han llegado a entrevista u oferta.»; null si no hay enviadas */
  protected readonly textoRespuesta = computed(() => {
    const r = this.estadisticas()?.respuesta;
    if (!r || r.enviadas === 0 || r.porcentaje === null) {
      return null;
    }
    const verbo = r.conRespuesta === 1 ? 'ha llegado' : 'han llegado';
    return `${r.conRespuesta} de ${r.enviadas} (${r.porcentaje} %) ${verbo} a entrevista u oferta.`;
  });

  /** «De media tardan 9 días en responder (calculado con 3).»; null si no hay casos */
  protected readonly textoDiasMedios = computed(() => {
    const r = this.estadisticas()?.respuesta;
    if (!r || r.diasMedios === null || r.casosDias === 0) {
      return null;
    }
    const calculado = `(calculado con ${r.casosDias})`;
    return r.diasMedios === 0
      ? `De media responden el mismo día ${calculado}.`
      : `De media tardan ${textoDias(r.diasMedios)} en responder ${calculado}.`;
  });

  protected reintentar(): void {
    this.estadisticasService.cargar();
  }
}
