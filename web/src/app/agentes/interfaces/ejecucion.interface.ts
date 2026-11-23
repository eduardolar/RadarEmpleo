import { CandidaturaBreve } from '../../hoy/interfaces/panel-hoy.interface';

// Tipos del contrato de /api/agentes (RadarEmpleo.Services/Contratos/Agentes.cs).
// Los enums viajan como texto, las fechas como cadenas ISO en UTC y los importes en el equivalente en USD
// que da Claude: por dentro se usan para el porcentaje del tope, pero en pantalla nunca sale una cifra en USD.

/** Las cuatro últimas son de la F8-F10: todavía no se pueden lanzar */
export type TareaAgente =
  | 'Rastreo'
  | 'AnalizarEncaje'
  | 'ExtraerRegistro'
  | 'Redactar'
  | 'Revisar'
  | 'RellenarEnvio'
  | 'ConfirmarEnvio';

/** Ok, Error y Cancelada son terminales: de ahí no se sale (reintentar crea otra ejecución) */
export type EstadoEjecucion = 'EnCola' | 'EnCurso' | 'Ok' | 'Error' | 'Cancelada';

/** Por qué sigue en cola: espera su turno o otra sesión de Claude tiene el bloqueo compartido */
export type MotivoEspera = 'Turno' | 'BloqueoOcupado';

/** Si la oferta seguía publicada, según la comprobación previa a AnalizarEncaje */
export type EstadoOferta = 'Abierta' | 'Cerrada' | 'NoSeSabe';

/** Respuesta de GET /api/agentes/ejecuciones/{id} y de los POST que lanzan, cancelan o reintentan */
export interface EjecucionAgente {
  id: number;
  tarea: TareaAgente;
  estado: EstadoEjecucion;
  espera: MotivoEspera | null;
  /** Quién tiene el bloqueo («la tarea de los lunes», «tu sesión de /empleo en Claude Code», «otro programa») */
  quienBloquea: string | null;
  fechaAlta: string;
  inicio: string | null;
  fin: string | null;
  presupuestoUsd: number;
  tiempoMaximoSegundos: number;
  /** null si no se sabe (con `costeEstimado`, cuenta el presupuesto entero) */
  costeUsd: number | null;
  /** Terminó sin coste conocido: en el uso del día cuenta su presupuesto */
  costeEstimado: boolean;
  resumen: string | null;
  /** Ya en español, para enseñarlo tal cual */
  error: string | null;
  /** Aviso rojo: ofertas-enviadas.md cambió mientras trabajaba */
  registroCambiado: boolean;
  /** Aviso rojo: perfil-busqueda.md cambió mientras trabajaba */
  perfilCambiado: boolean;
  candidaturaId: number | null;
  candidatura: CandidaturaBreve | null;
  rastreoId: number | null;
  /** Solo en AnalizarEncaje; null si no se llegó a comprobar */
  estadoOferta: EstadoOferta | null;
  puedeCancelar: boolean;
  puedeReintentar: boolean;
}

/** Lo usado hoy (día de Madrid) con los agentes */
export interface GastoHoy {
  /** «2026-11-23» */
  hoy: string;
  /** Lo de las terminadas (las que no saben su coste cuentan su presupuesto) */
  gastadoUsd: number;
  /** El presupuesto de lo que está en curso o en cola */
  reservadoUsd: number;
  topeUsd: number;
  /** Lo que queda hasta el tope (nunca negativo) */
  disponibleUsd: number;
  ejecuciones: number;
}

/** Respuesta de GET /api/agentes/estado */
export interface EstadoAgentes {
  disponibles: boolean;
  /** «Solo desde el PC» o «Apagados en la configuración»; null si están disponibles */
  motivoNoDisponibles: string | null;
  /** La de `claude --version`; null si no se sabe */
  versionClaude: string | null;
  versionProbada: string;
  /** La instalada no es la probada: se avisa, pero no se bloquea */
  versionDistinta: boolean;
  gasto: GastoHoy;
  enCurso: EjecucionAgente | null;
  enCola: number;
  /** Si esta copia de la API procesa la cola: si no, desde aquí no se puede lanzar nada */
  llevaLaCola: boolean;
  /** Por qué no la lleva, en español; null si la lleva */
  motivoCola: string | null;
}
