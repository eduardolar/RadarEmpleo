import {
  afterNextRender,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { catchError, concatMap, EMPTY, finalize, from } from 'rxjs';
import { ErrorApi } from '../../../core/interfaces/error-api.interface';
import { CabeceraPagina } from '../../../shared/components/cabecera-pagina/cabecera-pagina';
import { SeccionPlegable } from '../../../shared/components/seccion-plegable/seccion-plegable';
import { AvisosService } from '../../../shared/services/avisos.service';
import { FechaActualService } from '../../../shared/services/fecha-actual.service';
import { diaMadrid } from '../../../shared/utils/fechas-madrid';
import { InformeRastreo } from '../../components/informe-rastreo/informe-rastreo';
import { TarjetaOferta } from '../../components/tarjeta-oferta/tarjeta-oferta';
import { OfertaRastreo } from '../../interfaces/rastreo.interface';
import { RastreosService } from '../../services/rastreos.service';
import { agruparOfertas } from '../../utils/agrupar-ofertas';
import { etiquetaRastreo, textoNuevas } from '../../utils/etiqueta-rastreo';
import { CandidaturaParecida, posiblesRepetidas } from '../../utils/posibles-repetidas';
import { textoEscaneo, textoSinLeer } from '../../utils/resumen-escaneo';

const SIN_PARECIDAS: readonly CandidaturaParecida[] = [];

@Component({
  imports: [
    CabeceraPagina,
    InformeRastreo,
    MatButtonModule,
    MatFormFieldModule,
    MatProgressBarModule,
    MatSelectModule,
    SeccionPlegable,
    TarjetaOferta,
  ],
  selector: 'app-rastreos-page',
  templateUrl: './rastreos-page.component.html',
})
export default class RastreosPageComponent {
  private readonly rastreosService = inject(RastreosService);
  private readonly avisos = inject(AvisosService);
  private readonly router = inject(Router);
  private readonly fechaActual = inject(FechaActualService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly rastreos = this.rastreosService.rastreos;
  protected readonly detalle = this.rastreosService.detalle;
  protected readonly seleccionado = this.rastreosService.seleccionado;
  protected readonly escaneando = this.rastreosService.escaneando;
  protected readonly cargando = this.rastreosService.cargando;
  protected readonly error = this.rastreosService.error;
  protected readonly errorEscaneo = this.rastreosService.errorEscaneo;
  protected readonly resultado = this.rastreosService.resultado;
  protected readonly ocupadas = this.rastreosService.ocupadas;

  /** Pasadas al tablero en esta visita: se quedan en su sitio para que la lista no salte */
  private readonly pasadasAhora = signal<ReadonlySet<number>>(new Set());

  /** «No me interesan las que quedan» en marcha */
  protected readonly apartando = signal(false);

  protected readonly grupos = computed(() =>
    agruparOfertas(this.detalle()?.ofertas ?? [], this.pasadasAhora()),
  );

  /** Las que quedan por decidir, sin contar las que se acaban de pasar y siguen a la vista */
  protected readonly porDecidir = computed(() =>
    this.grupos().pendientes.filter((o) => o.estado === 'Pendiente'),
  );

  protected readonly parecidas = computed(() =>
    posiblesRepetidas(this.detalle()?.ofertas ?? [], this.rastreosService.delTablero()),
  );

  protected readonly opciones = computed(() => {
    const lista = this.rastreos();
    const anio = Number(diaMadrid(this.fechaActual.ahora()).slice(0, 4));
    return lista.map((r) => ({
      id: r.id,
      etiqueta: etiquetaRastreo(r, lista, anio),
      pendientes: r.pendientes,
    }));
  });

  /** Con este ya mirado, el rastreo más reciente que aún tiene ofertas por mirar (o null) */
  protected readonly otroPorMirar = computed(
    () => this.opciones().find((o) => o.id !== this.seleccionado() && o.pendientes > 0) ?? null,
  );

  protected readonly textoNuevas = textoNuevas;

  protected readonly textoResultado = computed(() => {
    const resultado = this.resultado();
    return resultado ? textoEscaneo(resultado) : null;
  });

  protected readonly avisoSinLeer = computed(() => {
    const resultado = this.resultado();
    return resultado ? textoSinLeer(resultado.lineasSinLeer) : null;
  });

  /** Ya se ha mirado y no hay ninguno: se explica en vez de dejar la página en blanco */
  protected readonly sinRastreos = computed(
    () => !this.escaneando() && !this.cargando() && !this.error() && this.rastreos().length === 0,
  );

  constructor() {
    // Decisión de Eduardo: al abrir la pantalla se buscan informes nuevos sin tener que pulsar nada
    this.rastreosService.escanear();
    this.rastreosService.cargarTablero();
  }

  protected parecidasDe(oferta: OfertaRastreo): readonly CandidaturaParecida[] {
    return this.parecidas().get(oferta.id) ?? SIN_PARECIDAS;
  }

  protected volverABuscar(): void {
    // disabledInteractive deja pasar el clic mientras busca
    if (!this.escaneando()) {
      this.rastreosService.escanear();
      this.rastreosService.cargarTablero();
    }
  }

  protected elegir(id: number): void {
    this.rastreosService.seleccionar(id);
  }

  protected detectar(oferta: OfertaRastreo): void {
    this.pasadasAhora.update((ids) => new Set(ids).add(oferta.id));
    // El botón pulsado desaparece: el foco se queda en la tarjeta, que sigue en su sitio
    this.enfocar(`[data-oferta="${oferta.id}"]`);
    // Sin cancelar al destruirse: salir de la página no debe perder la candidatura que se está creando
    this.rastreosService.detectar(oferta).subscribe({
      next: (resultado) =>
        this.avisos.exito(
          resultado.yaExistia ? 'Ya estaba en el tablero.' : 'Añadida al tablero.',
          {
            texto: 'Abrir ficha',
            alPulsar: () => void this.router.navigate(['/candidaturas', resultado.candidaturaId]),
          },
        ),
      error: (error: ErrorApi) => this.avisos.error(error.mensaje),
    });
  }

  protected ignorar(oferta: OfertaRastreo): void {
    // La tarjeta se va al final: el foco pasa a la siguiente (o a la anterior si era la última)
    const lista = this.grupos().pendientes;
    const posicion = lista.findIndex((o) => o.id === oferta.id);
    const vecina = lista[posicion + 1] ?? lista[posicion - 1];
    this.enfocar(vecina ? `[data-oferta="${vecina.id}"]` : '#titulo-ofertas');

    this.rastreosService.ignorar(oferta).subscribe({
      error: (error: ErrorApi) => this.avisos.error(error.mensaje),
    });
  }

  /**
   * «No me interesan las que quedan»: aparta de una vez las que quedan, una detrás de otra para no
   * saturar la API. Se pueden recuperar desde «No me interesan», así que no se pide confirmación.
   */
  protected ignorarLasDemas(): void {
    const demas = this.porDecidir().filter((o) => !this.ocupadas().has(o.id));
    if (this.apartando() || demas.length === 0) {
      return;
    }
    this.apartando.set(true);
    this.enfocar('#titulo-ofertas');
    let fallos = 0;

    from(demas)
      .pipe(
        concatMap((oferta) =>
          this.rastreosService.ignorar(oferta).pipe(
            catchError(() => {
              fallos++;
              return EMPTY;
            }),
          ),
        ),
        finalize(() => this.apartando.set(false)),
      )
      .subscribe({
        complete: () => {
          const apartadas = demas.length - fallos;
          if (fallos > 0) {
            this.avisos.error(
              `No he podido apartar ${fallos === 1 ? '1 oferta' : `${fallos} ofertas`}. Vuelve a probar.`,
            );
          } else {
            this.avisos.exito(
              `${apartadas === 1 ? '1 oferta movida' : `${apartadas} ofertas movidas`} a «No me interesan».`,
            );
          }
        },
      });
  }

  /** Tras el próximo pintado, pone el foco en ese elemento de la página (si sigue ahí) */
  private enfocar(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
