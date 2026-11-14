import { TipoRecordatorio } from '../../candidaturas/interfaces/candidatura.interface';

// Tipos del contrato de /api/recordatorios (RadarEmpleo.Services/Contratos/Recordatorios.cs).
// El día y la hora son siempre de Madrid: la API los convierte a UTC.

/** Cuerpo para crear (suelto o de una candidatura) y para editar un recordatorio */
export interface GuardarRecordatorio {
  tipo: TipoRecordatorio;
  titulo: string;
  /** «2026-11-17» */
  dia: string;
  /** «15:00»; null son las 09:00 */
  hora: string | null;
  notas: string | null;
}
