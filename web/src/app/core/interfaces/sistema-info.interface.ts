/** Respuesta de GET /api/sistema/info */
export interface SistemaInfo {
  modo: ModoEjecucion;
  agentesHabilitados: boolean;
  version: string;
}

/** Local: el PC de Eduardo. Demo: datos inventados y agentes desactivados */
export type ModoEjecucion = 'Local' | 'Demo';

export type EstadoConexionApi = 'comprobando' | 'conectada' | 'sin-conexion';
