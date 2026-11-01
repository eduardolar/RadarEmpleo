import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Empresa } from '../interfaces/empresa.interface';

@Injectable({ providedIn: 'root' })
export class EmpresasService {
  private readonly http = inject(HttpClient);

  /** Empresas cuyo nombre contiene el texto (la API ignora mayúsculas, tildes y S.L./S.A.) */
  buscar(texto: string): Observable<Empresa[]> {
    return this.http.get<Empresa[]>(`${environment.apiUrl}/api/empresas`, {
      params: new HttpParams().set('texto', texto.trim()),
    });
  }
}
