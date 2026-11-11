// Tipos del contrato de GET /api/perfil (RadarEmpleo.Services/Contratos/Perfil.cs).
// Las fechas viajan como cadenas ISO en UTC (terminan en «Z»).

/** Una sección «## » de perfil-busqueda.md, con su Markdown sin el título */
export interface SeccionPerfil {
  titulo: string;
  markdown: string;
}

/** El perfil de búsqueda partido en secciones. Solo se lee: lo cambia el equipo /empleo */
export interface Perfil {
  titulo: string;
  /** Última vez que se cambió el archivo */
  fechaModificacion: string;
  /** Lo que va entre el título y la primera sección, o null */
  introduccion: string | null;
  secciones: SeccionPerfil[];
}
