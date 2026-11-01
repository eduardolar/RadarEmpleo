/** Error de la API ya preparado para enseñarlo: mensaje general y mensajes por campo */
export interface ErrorApi {
  /** Código HTTP (0 si no hubo respuesta) */
  estado: number;
  mensaje: string;
  /** Campo del JSON («empresa», «puesto»...) → mensajes en español. Vacío si no es un 400 */
  errores: Record<string, string[]>;
}
