import { CanDeactivateFn } from '@angular/router';
import { Observable } from 'rxjs';

/** Página con un formulario que no debería perderse al salir sin querer */
export interface ConCambiosSinGuardar {
  /** Es la página quien pregunta: así el diálogo no entra en la descarga inicial con las rutas */
  puedeSalir(): boolean | Observable<boolean>;
}

export const sinCambiosGuard: CanDeactivateFn<ConCambiosSinGuardar> = (pagina) =>
  pagina.puedeSalir();
