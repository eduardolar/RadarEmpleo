import { MediaMatcher } from '@angular/cdk/layout';
import { DestroyRef, Directive, ElementRef, inject, signal } from '@angular/core';
import { ajusteParaVer, destinoLateral, SentidoLateral } from '../utils/desplazamiento-lateral';

/**
 * Carril con desplazamiento horizontal: dice si queda contenido oculto a cada lado y avanza de columna
 * en columna (cada hijo directo es una columna). Las flechas y la barra las pone quien lo usa. El
 * margen de las flechas es el scroll-padding del carril
 */
@Directive({
  selector: '[appDesplazamientoLateral]',
  exportAs: 'desplazamientoLateral',
})
export class DesplazamientoLateral {
  private readonly carril = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly movimientoReducido = inject(MediaMatcher).matchMedia(
    '(prefers-reduced-motion: reduce)',
  );

  readonly hayAntes = signal(false);
  readonly hayDespues = signal(false);

  /** Adónde va el desplazamiento suave en curso: dos clics seguidos avanzan dos columnas */
  private destino: number | null = null;

  constructor() {
    const medir = () => this.medir();
    const terminar = () => {
      this.destino = null;
      this.medir();
    };
    let fotograma = 0;
    // Después del desplazamiento que haga el propio navegador al recibir el foco
    const alEnfocar = (evento: FocusEvent) => {
      cancelAnimationFrame(fotograma);
      fotograma = requestAnimationFrame(() => this.mostrarFoco(evento.target));
    };
    // Sin pasar por Angular: un listener de plantilla pediría una detección de cambios por cada
    // evento de scroll, y aquí solo hace falta cuando cambia una de las dos señales
    this.carril.addEventListener('scroll', medir, { passive: true });
    this.carril.addEventListener('scrollend', terminar, { passive: true });
    this.carril.addEventListener('focusin', alEnfocar);

    // También cambia lo que cabe al cambiar la ventana o una columna (al plegarla o desplegarla).
    // Las columnas que se quitan dejan de observarse: si no, el observador las retendría
    const tamanos = new ResizeObserver(medir);
    const columnas = new MutationObserver((cambios) => {
      for (const cambio of cambios) {
        cambio.removedNodes.forEach((nodo) => {
          if (nodo instanceof Element) {
            tamanos.unobserve(nodo);
          }
        });
        cambio.addedNodes.forEach((nodo) => {
          if (nodo instanceof Element) {
            tamanos.observe(nodo);
          }
        });
      }
    });
    tamanos.observe(this.carril);
    for (const columna of this.carril.children) {
      tamanos.observe(columna);
    }
    columnas.observe(this.carril, { childList: true });

    inject(DestroyRef).onDestroy(() => {
      this.carril.removeEventListener('scroll', medir);
      this.carril.removeEventListener('scrollend', terminar);
      this.carril.removeEventListener('focusin', alEnfocar);
      cancelAnimationFrame(fotograma);
      tamanos.disconnect();
      columnas.disconnect();
    });
  }

  avanzar(sentido: SentidoLateral): void {
    const carril = this.carril;
    const inicio = this.inicioVisible() - carril.scrollLeft;
    const bordes = [...carril.children].map(
      (columna) => columna.getBoundingClientRect().left - inicio,
    );
    const destino = destinoLateral(
      bordes,
      this.destino ?? carril.scrollLeft,
      carril.scrollWidth - carril.clientWidth,
      this.margen(),
      sentido,
    );
    if (destino !== null) {
      this.destino = destino;
      this.desplazar(destino - carril.scrollLeft);
    }
  }

  /**
   * Con el teclado, la columna del foco se trae entera a la vista. El navegador no desplaza nada si lo
   * enfocado asoma aunque sea un poco, y quedaría debajo de la flecha y del degradado. Con el ratón no:
   * moverlo al pulsar haría fallar el clic
   */
  private mostrarFoco(objetivo: EventTarget | null): void {
    if (!(objetivo instanceof Element) || !objetivo.matches(':focus-visible')) {
      return;
    }
    const columna = [...this.carril.children].find((hija) => hija.contains(objetivo));
    if (!columna) {
      return;
    }
    const inicio = this.inicioVisible();
    const caja = columna.getBoundingClientRect();
    const ajuste = ajusteParaVer(
      caja.left - inicio,
      caja.right - inicio,
      this.carril.clientWidth,
      this.margen(),
    );
    if (Math.abs(ajuste) >= 1) {
      this.destino = null;
      this.desplazar(ajuste);
    }
  }

  private desplazar(cuanto: number): void {
    this.carril.scrollBy({
      left: cuanto,
      behavior: this.movimientoReducido.matches ? 'instant' : 'smooth',
    });
  }

  /** Borde izquierdo de lo que se ve del carril, en coordenadas de la ventana */
  private inicioVisible(): number {
    return this.carril.getBoundingClientRect().left + this.carril.clientLeft;
  }

  private margen(): number {
    return parseFloat(getComputedStyle(this.carril).scrollPaddingLeft) || 0;
  }

  private medir(): void {
    const { scrollLeft, scrollWidth, clientWidth } = this.carril;
    // 1 px de tolerancia: con zoom el final se queda a medio píxel
    this.hayAntes.set(scrollLeft > 1);
    this.hayDespues.set(scrollLeft + clientWidth < scrollWidth - 1);
    if (this.destino !== null && Math.abs(scrollLeft - this.destino) < 2) {
      this.destino = null;
    }
  }
}
