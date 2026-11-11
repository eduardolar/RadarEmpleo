import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { defer, EMPTY, finalize, Observable, Subscription, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CandidaturasService } from '../../candidaturas/services/candidaturas.service';
import { conErrorApi, convertirErrorApi } from '../../core/utils/error-api';
import {
  Documento,
  DocumentoSugerido,
  TipoDocumento,
  VincularDocumento,
} from '../interfaces/documento.interface';

const URL_DOCUMENTOS = `${environment.apiUrl}/api/documentos`;
const URL_CANDIDATURAS = `${environment.apiUrl}/api/candidaturas`;

/**
 * Documentos de la ficha: las sugerencias en signals, vincular y quitar. Al vincular o quitar bien se
 * vuelve a pedir la ficha, que es la que trae los documentos (con el vigente de cada tipo ya resuelto
 * por la API).
 */
@Injectable({ providedIn: 'root' })
export class DocumentosService {
  private readonly http = inject(HttpClient);
  private readonly candidaturas = inject(CandidaturasService);

  private readonly _sugeridos = signal<DocumentoSugerido[]>([]);
  readonly sugeridos = this._sugeridos.asReadonly();

  private readonly _cargandoSugeridos = signal(false);
  readonly cargandoSugeridos = this._cargandoSugeridos.asReadonly();

  private readonly _errorSugeridos = signal<string | null>(null);
  readonly errorSugeridos = this._errorSugeridos.asReadonly();

  private readonly _vinculando = signal(false);
  readonly vinculando = this._vinculando.asReadonly();

  private peticionSugeridos?: Subscription;

  /** Pide las sugerencias de esta candidatura. Borra antes las que hubiera (pueden ser de otra) */
  cargarSugeridos(candidaturaId: number): void {
    this.peticionSugeridos?.unsubscribe();
    this._sugeridos.set([]);
    this._errorSugeridos.set(null);
    this._cargandoSugeridos.set(true);

    const params = new HttpParams().set('candidaturaId', candidaturaId);
    this.peticionSugeridos = this.http
      .get<DocumentoSugerido[]>(`${URL_DOCUMENTOS}/sugeridos`, { params })
      .subscribe({
        next: (sugeridos) => {
          this._sugeridos.set(sugeridos);
          this._cargandoSugeridos.set(false);
        },
        error: (error: unknown) => {
          this._errorSugeridos.set(convertirErrorApi(error).mensaje);
          this._cargandoSugeridos.set(false);
        },
      });
  }

  /** Deja de esperar las sugerencias (al cerrar el diálogo): nadie las va a ver */
  cancelarSugeridos(): void {
    this.peticionSugeridos?.unsubscribe();
    this._cargandoSugeridos.set(false);
  }

  /** Vincula un archivo a la ficha. Mientras responde, un segundo intento no hace nada (doble clic) */
  vincular(
    candidaturaId: number,
    rutaRelativa: string,
    tipo?: TipoDocumento,
  ): Observable<Documento> {
    return defer(() => {
      if (this._vinculando()) {
        return EMPTY;
      }
      this._vinculando.set(true);
      const cuerpo: VincularDocumento = tipo ? { rutaRelativa, tipo } : { rutaRelativa };
      return this.http
        .post<Documento>(`${URL_CANDIDATURAS}/${candidaturaId}/documentos`, cuerpo)
        .pipe(
          tap((documento) => {
            this._sugeridos.update((lista) =>
              lista.map((s) =>
                s.rutaRelativa === documento.rutaRelativa ? { ...s, yaVinculado: true } : s,
              ),
            );
            this.refrescarFicha(candidaturaId);
          }),
          conErrorApi(),
          finalize(() => this._vinculando.set(false)),
        );
    });
  }

  /** Quita el documento de la ficha. El archivo no se borra: sigue en su carpeta */
  quitar(documento: Documento): Observable<void> {
    return this.http.delete<void>(`${URL_DOCUMENTOS}/${documento.id}`).pipe(
      tap(() => this.refrescarFicha(documento.candidaturaId)),
      conErrorApi(),
    );
  }

  /** Solo si esa ficha sigue abierta: no se cambia la que se está viendo por otra */
  private refrescarFicha(candidaturaId: number): void {
    if (this.candidaturas.detalle()?.id === candidaturaId) {
      this.candidaturas.cargarDetalle(candidaturaId);
    }
  }
}
