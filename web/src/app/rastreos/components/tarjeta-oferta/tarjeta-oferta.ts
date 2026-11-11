import { Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { ETIQUETAS_ENCAJE, ETIQUETAS_ESTADO } from '../../../candidaturas/utils/etiquetas';
import { OfertaRastreo } from '../../interfaces/rastreo.interface';
import { CandidaturaParecida } from '../../utils/posibles-repetidas';

/** Cuántas candidaturas parecidas se enseñan como mucho; del resto solo se dice cuántas hay */
const PARECIDAS_A_LA_VISTA = 3;

/**
 * Una oferta de un rastreo: encaje, empresa, puesto, pega y enlace. Pendiente o ignorada se puede pasar
 * al tablero; pendiente, también dejarla de lado. Si ya está en el tablero, lleva a su ficha.
 * Si hay candidaturas de la misma empresa, avisa antes de los botones de que puede estar repetida.
 */
@Component({
  imports: [MatButtonModule, RouterLink],
  selector: 'app-tarjeta-oferta',
  templateUrl: './tarjeta-oferta.html',
  host: { class: 'block' },
})
export class TarjetaOferta {
  readonly oferta = input.required<OfertaRastreo>();
  /** Hay una petición en curso para esta oferta */
  readonly ocupada = input(false);
  /** Candidaturas del tablero de la misma empresa (las del registro no tienen enlace con que comparar) */
  readonly parecidas = input<readonly CandidaturaParecida[]>([]);

  readonly detectar = output<OfertaRastreo>();
  readonly ignorar = output<OfertaRastreo>();

  protected readonly encaje = computed(() => {
    const encaje = this.oferta().encaje;
    return encaje === 'No' ? ETIQUETAS_ENCAJE.No : `Encaje: ${ETIQUETAS_ENCAJE[encaje]}`;
  });

  protected readonly etiquetasEstado = ETIQUETAS_ESTADO;

  protected readonly parecidasALaVista = computed(() =>
    this.parecidas().slice(0, PARECIDAS_A_LA_VISTA),
  );

  protected readonly parecidasDeMas = computed(() =>
    Math.max(0, this.parecidas().length - PARECIDAS_A_LA_VISTA),
  );

  /** «En el tablero hay una candidatura de Acme.» Sin doble punto si el nombre acaba en «S.L.» */
  protected readonly textoParecidas = computed(() => {
    const total = this.parecidas().length;
    const cuantas = total === 1 ? 'una candidatura' : `${total} candidaturas`;
    const empresa = this.oferta().empresa.trim();
    return `En el tablero hay ${cuantas} de ${empresa}${empresa.endsWith('.') ? '' : '.'}`;
  });

  protected pulsarDetectar(): void {
    // disabledInteractive deja pasar el clic mientras responde: aquí se para
    if (!this.ocupada()) {
      this.detectar.emit(this.oferta());
    }
  }

  protected pulsarIgnorar(): void {
    if (!this.ocupada()) {
      this.ignorar.emit(this.oferta());
    }
  }
}
