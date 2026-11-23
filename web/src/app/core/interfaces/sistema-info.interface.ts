/** Respuesta de GET /api/sistema/info */
export interface SistemaInfo {
  modo: ModoEjecucion;
  agentesHabilitados: boolean;
  version: string;
  /**
   * Si se pueden lanzar agentes desde aquí: por el endpoint «Local», con los agentes habilitados y en
   * modo Local. Desde el móvil es false y no se enseñan los botones (ni se pide /api/agentes/estado)
   */
  agentesDisponibles: boolean;
}

/** Local: el PC de Eduardo. Demo: datos inventados y agentes desactivados */
export type ModoEjecucion = 'Local' | 'Demo';

export type EstadoConexionApi = 'comprobando' | 'conectada' | 'sin-conexion';
