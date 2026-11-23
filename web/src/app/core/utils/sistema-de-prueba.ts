import { SistemaInfo } from '../interfaces/sistema-info.interface';

// Solo para los tests: la respuesta de /api/sistema/info, por defecto sin agentes disponibles

export function crearSistemaInfo(cambios: Partial<SistemaInfo> = {}): SistemaInfo {
  return {
    modo: 'Local',
    agentesHabilitados: false,
    version: '0.1.0',
    agentesDisponibles: false,
    ...cambios,
  };
}
