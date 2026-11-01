/** Empresa para el autocompletado (GET /api/empresas?texto) */
export interface Empresa {
  id: number;
  nombre: string;
  web: string | null;
  candidaturas: number;
}
