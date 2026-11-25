import { SistemaInfo } from '../../core/interfaces/sistema-info.interface';
import {
  EjecucionAgente,
  EstadoEjecucion,
  GastoHoy,
  TareaAgente,
} from '../interfaces/ejecucion.interface';

// Claude Code va con la suscripción de Eduardo: en pantalla se habla de «uso estimado» como porcentaje
// del tope de hoy y nunca de dólares, aunque la API dé los importes en el equivalente en USD.

/** Entre una cifra y su unidad («3 %», «5 min»): así no se parten al final de una línea */
export const NBSP = String.fromCharCode(0xa0);

/** La insignia de cada estado (en femenino, como «la tarea») */
export const ETIQUETA_ESTADO: Record<EstadoEjecucion, string> = {
  EnCola: 'En cola',
  EnCurso: 'En curso',
  Ok: 'Hecha',
  Error: 'No ha salido',
  Cancelada: 'Cancelada',
};

/** La API ya no la encuentra (404 en el seguimiento): sin esto se quedaría «En curso» para siempre */
export const ETIQUETA_PERDIDA = 'No se encuentra';
export const TEXTO_PERDIDA =
  'Ya no se encuentra esta tarea de Claude. Si hace falta, vuelve a lanzarla.';
/** En la lista de /agentes, donde no se lanza nada */
export const TEXTO_PERDIDA_CORTO = 'Ya no se encuentra esta tarea.';

/** El nombre de la tarea en listas («Análisis del encaje · Acme») */
export const NOMBRE_TAREA: Record<TareaAgente, string> = {
  Rastreo: 'Rastreo de ofertas',
  AnalizarEncaje: 'Análisis del encaje',
  ExtraerRegistro: 'Lectura del texto libre',
  Redactar: 'Redacción',
  Revisar: 'Revisión',
  RellenarEnvio: 'Preparar el envío',
  ConfirmarEnvio: 'Confirmación del envío',
};

/** Debajo de cada botón que lanza una tarea de Claude */
export const AVISO_GASTO = 'Gasta un poco de tu tope diario de Claude.';

/** Lo que está haciendo mientras va en curso */
const TRABAJANDO: Record<TareaAgente, string> = {
  Rastreo: 'Rastreando ofertas',
  AnalizarEncaje: 'Analizando el encaje',
  ExtraerRegistro: 'Leyendo el texto libre',
  Redactar: 'Redactando',
  Revisar: 'Revisando',
  RellenarEnvio: 'Preparando el envío',
  ConfirmarEnvio: 'Confirmando el envío',
};

/** Los archivos que vigila el aviso rojo, dichos como los conoce Eduardo y no por su nombre */
export type ArchivoVigilado = 'registro' | 'perfil';

const NOMBRE_ARCHIVO: Record<ArchivoVigilado, string> = {
  registro: 'tu registro de candidaturas',
  perfil: 'tu perfil de búsqueda',
};

type DatosUso = Pick<
  EjecucionAgente,
  'estado' | 'inicio' | 'costeUsd' | 'costeEstimado' | 'presupuestoUsd'
>;

/**
 * «59 s», «1 min 20 s», «5 min», «1 h 2 min», «2 h», con espacio de no separación entre la cifra y su
 * unidad (no se parten al final de una línea); null si no es un número (una fecha inválida)
 */
export function duracion(segundos: number): string | null {
  if (!Number.isFinite(segundos)) {
    return null;
  }
  const total = Math.max(0, Math.floor(segundos));
  if (total < 60) {
    return `${total}${NBSP}s`;
  }
  if (total < 3600) {
    const minutos = Math.floor(total / 60);
    const resto = total % 60;
    return resto === 0 ? `${minutos}${NBSP}min` : `${minutos}${NBSP}min ${resto}${NBSP}s`;
  }
  const horas = Math.floor(total / 3600);
  const minutos = Math.floor((total % 3600) / 60);
  return minutos === 0 ? `${horas}${NBSP}h` : `${horas}${NBSP}h ${minutos}${NBSP}min`;
}

/**
 * El porcentaje del tope que supone `usd`: redondeado, o hacia arriba si es una cota (`comoMucho`).
 * Sin redondear a 100 lo que no llega: 100 solo si es el tope entero. null sin tope
 */
function porcentajeDelTope(usd: number, topeUsd: number | null, comoMucho: boolean): number | null {
  if (topeUsd === null || !(topeUsd > 0) || !Number.isFinite(usd)) {
    return null;
  }
  // Sin los restos de coma flotante (0,45 / 15 da 3,0000000000000004 y se iría a 4)
  const porcentaje = Number(((Math.max(0, usd) / topeUsd) * 100).toFixed(6));
  if (comoMucho) {
    return Math.ceil(porcentaje);
  }
  const redondeado = Math.round(porcentaje);
  return porcentaje < 100 ? Math.min(redondeado, 99) : redondeado;
}

/** «3 %», o «menos del 1 %» si ha gastado algo pero no llega al 1 % (con espacio de no separación) */
function parteConocida(usd: number, porcentaje: number): string {
  return porcentaje === 0 && usd > 0 ? `menos del 1${NBSP}%` : `${porcentaje}${NBSP}%`;
}

/**
 * El uso de una ejecución respecto al tope de hoy:
 * - con su coste, «uso estimado: 1 % del tope de hoy» (con 0, «no ha gastado nada de tu tope de hoy»);
 * - en curso, «uso estimado: como mucho el 3 % del tope de hoy», con su presupuesto;
 * - terminada sin coste, «uso estimado: no se sabe, como mucho el 3 % del tope de hoy».
 * De otro día (`deHoy` a false), «del tope diario». null sin tope o si no llegó a empezar (no ha
 * gastado nada)
 */
export function usoEstimado(
  ejecucion: DatosUso,
  topeUsd: number | null,
  deHoy = true,
): string | null {
  const { costeUsd, costeEstimado, estado, inicio, presupuestoUsd } = ejecucion;
  // El porcentaje sale del tope de ahora: de otro día no se sabe si era el mismo, así que «diario»
  const tope = deHoy ? 'tope de hoy' : 'tope diario';
  if (costeUsd !== null) {
    const porcentaje = porcentajeDelTope(costeUsd, topeUsd, false);
    if (porcentaje === null) {
      return null;
    }
    return costeUsd <= 0
      ? `no ha gastado nada de tu ${tope}`
      : `uso estimado: ${parteConocida(costeUsd, porcentaje)} del ${tope}`;
  }
  const enCurso = estado === 'EnCurso' && inicio !== null;
  if (!costeEstimado && !enCurso) {
    return null;
  }
  const cota = porcentajeDelTope(presupuestoUsd, topeUsd, true);
  if (cota === null) {
    return null;
  }
  return costeEstimado
    ? `uso estimado: no se sabe, como mucho el ${cota}${NBSP}% del ${tope}`
    : `uso estimado: como mucho el ${cota}${NBSP}% del ${tope}`;
}

/**
 * «Uso estimado de tu suscripción hoy: 3 % del tope» o «Hoy aún no has usado Claude» (si hay algo
 * reservado, «Hoy aún no has gastado nada; Claude tiene trabajo en marcha»); null sin tope
 */
export function usoDelDia(
  gasto: Pick<GastoHoy, 'gastadoUsd' | 'reservadoUsd' | 'topeUsd'>,
): string | null {
  const porcentaje = porcentajeDelTope(gasto.gastadoUsd, gasto.topeUsd, false);
  if (porcentaje === null) {
    return null;
  }
  if (gasto.gastadoUsd <= 0) {
    // Con algo en cola o en curso ya se está usando Claude: «aún no has usado» sería falso
    return gasto.reservadoUsd > 0
      ? 'Hoy aún no has gastado nada; Claude tiene trabajo en marcha'
      : 'Hoy aún no has usado Claude';
  }
  return `Uso estimado de tu suscripción hoy: ${parteConocida(gasto.gastadoUsd, porcentaje)} del tope`;
}

/**
 * «Reservado para lo que está en marcha: hasta el 4 % del tope de hoy» (el presupuesto de lo que va en
 * cola o en curso, como cota); null si no hay nada reservado o sin tope
 */
export function reservaDelDia(gasto: Pick<GastoHoy, 'reservadoUsd' | 'topeUsd'>): string | null {
  if (!(gasto.reservadoUsd > 0)) {
    return null;
  }
  const cota = porcentajeDelTope(gasto.reservadoUsd, gasto.topeUsd, true);
  return cota === null
    ? null
    : `Reservado para lo que está en marcha: hasta el ${cota}${NBSP}% del tope de hoy`;
}

export const TEXTO_RECHAZADOS =
  'Ahora mismo no se puede usar Claude desde aquí. Vuelve a comprobarlo en un momento.';

/** Lo que dice /agentes cuando «Volver a comprobar» vuelve a dar 403 */
export const TEXTO_SIGUE_RECHAZADO =
  'Sigue sin poder usarse Claude desde aquí. Prueba otra vez dentro de un momento.';

/** Por qué no se pueden usar los agentes desde aquí, para /agentes (sin /api/agentes/estado, que daría 403) */
export function motivoSinAgentes(
  info: Pick<SistemaInfo, 'modo' | 'agentesHabilitados' | 'desdeElPc'>,
): string {
  if (info.modo === 'Demo') {
    return 'En la demo no se usa Claude: los agentes están apagados.';
  }
  // Por el móvil `agentesHabilitados` siempre llega a false: no quiere decir que estén apagados
  if (!info.desdeElPc) {
    return 'Los agentes de Claude solo se pueden usar desde el PC donde está instalada la app.';
  }
  if (!info.agentesHabilitados) {
    return 'Los agentes de Claude están apagados en la configuración de la app. Se encienden allí, no desde esta pantalla; en Ajustes puedes ver si están encendidos.';
  }
  // En el PC y encendidos: solo llega aquí tras un 403 de /api/agentes
  return TEXTO_RECHAZADOS;
}

/** El estado corto de Ajustes sin agentes disponibles: la etiqueta y una explicación de una línea */
export function estadoSinAgentes(
  info: Pick<SistemaInfo, 'modo' | 'agentesHabilitados' | 'desdeElPc'>,
): { etiqueta: string; explicacion: string } {
  if (info.modo === 'Demo') {
    return { etiqueta: 'Apagados', explicacion: 'En la demo no se usa Claude.' };
  }
  if (!info.desdeElPc) {
    return {
      etiqueta: 'Solo desde el PC',
      explicacion: 'Se usan desde el PC donde está instalada la app.',
    };
  }
  if (!info.agentesHabilitados) {
    return { etiqueta: 'Apagados', explicacion: 'Se encienden en la configuración de la app.' };
  }
  return { etiqueta: 'No disponibles ahora', explicacion: TEXTO_RECHAZADOS };
}

/** Con punto final si no acaba ya en «.», «?», «!» o «…» (los mensajes de la API suelen llevarlo) */
export function conPunto(texto: string): string {
  const limpio = texto.trim();
  return /[.?!…]$/.test(limpio) ? limpio : `${limpio}.`;
}

/**
 * La frase del estado para la tarjeta. `ahora` cuenta el tiempo de lo que va en curso (null: sin el
 * tiempo, ver `relojEnCurso`); `topeUsd` es el de hoy (de `EstadoAgentes.gasto`), o null si no se
 * sabe y no se dice el uso. En la lista de /agentes (`compacta`), un Error solo da el motivo (la
 * insignia ya dice «No ha salido») y lo de otro día (`deHoy` a false) habla del «tope diario»
 */
export function textoEstado(
  ejecucion: EjecucionAgente,
  ahora: Date | null,
  topeUsd: number | null,
  { compacta = false, deHoy = true }: { compacta?: boolean; deHoy?: boolean } = {},
): string {
  switch (ejecucion.estado) {
    case 'EnCola':
      return textoEnCola(ejecucion);
    case 'EnCurso':
      return textoEnCurso(ejecucion, ahora);
    case 'Ok':
      return textoHecha(ejecucion, topeUsd, deHoy);
    case 'Error': {
      const motivo = ejecucion.error?.trim() ? conPunto(ejecucion.error) : null;
      if (compacta) {
        return motivo ?? 'Sin más detalle.';
      }
      return motivo ? `No ha salido. ${motivo}` : 'No ha salido.';
    }
    case 'Cancelada':
      // También la cancela la API al arrancar si estaba en cola: no se dice quién
      return textoCancelada(ejecucion, topeUsd, deHoy);
  }
}

/**
 * «1 min 20 s (máximo 5 min)» de lo que va en curso, para pintarlo aparte de la frase (no se anuncia
 * cada segundo). null si no va en curso, si ya se está cancelando o sin `inicio` válido
 */
export function relojEnCurso(
  ejecucion: Pick<EjecucionAgente, 'estado' | 'inicio' | 'puedeCancelar' | 'tiempoMaximoSegundos'>,
  ahora: Date,
): string | null {
  if (ejecucion.estado !== 'EnCurso' || !ejecucion.puedeCancelar || ejecucion.inicio === null) {
    return null;
  }
  const transcurrido = duracion((ahora.getTime() - Date.parse(ejecucion.inicio)) / 1000);
  if (transcurrido === null) {
    return null;
  }
  const maximo = duracion(ejecucion.tiempoMaximoSegundos);
  return maximo === null ? transcurrido : `${transcurrido} (máximo ${maximo})`;
}

/** Los avisos rojos: el registro o el perfil han cambiado mientras trabajaba Claude */
export function avisosCambio(
  ejecucion: Pick<EjecucionAgente, 'registroCambiado' | 'perfilCambiado'>,
  corto = false,
): string[] {
  const aviso = corto ? avisoCambioCorto : avisoCambio;
  const avisos: string[] = [];
  if (ejecucion.registroCambiado) {
    avisos.push(aviso('registro'));
  }
  if (ejecucion.perfilCambiado) {
    avisos.push(aviso('perfil'));
  }
  return avisos;
}

export function avisoCambio(archivo: ArchivoVigilado): string {
  return `Ojo: ${NOMBRE_ARCHIVO[archivo]} ha cambiado mientras trabajaba Claude. Si no lo has tocado tú, revísalo.`;
}

/** En una línea, para la lista de /agentes */
export function avisoCambioCorto(archivo: ArchivoVigilado): string {
  return `Ojo: cambió ${NOMBRE_ARCHIVO[archivo]} mientras trabajaba Claude.`;
}

function textoEnCola(ejecucion: EjecucionAgente): string {
  switch (ejecucion.espera) {
    case 'Turno':
      return 'En cola: hay otra tarea de Claude en marcha.';
    case 'BloqueoOcupado':
      return ejecucion.quienBloquea
        ? `Esperando: ${ejecucion.quienBloquea} está usando Claude. Empieza sola cuando termine.`
        : 'Esperando: Claude está ocupado con otra cosa. Empieza sola cuando termine.';
    default:
      return 'En cola: empieza en unos segundos.';
  }
}

function textoEnCurso(ejecucion: EjecucionAgente, ahora: Date | null): string {
  // En curso y sin poder cancelar es que ya se ha pedido: la API corta el proceso en unos segundos
  if (!ejecucion.puedeCancelar) {
    return 'Cancelando… se parará en unos segundos.';
  }
  const trabajando = `${TRABAJANDO[ejecucion.tarea]}…`;
  const reloj = ahora === null ? null : relojEnCurso(ejecucion, ahora);
  return reloj === null ? trabajando : `${trabajando} · ${reloj}`;
}

function textoCancelada(
  ejecucion: EjecucionAgente,
  topeUsd: number | null,
  deHoy: boolean,
): string {
  if (ejecucion.inicio === null) {
    return 'Cancelada antes de empezar. No ha gastado nada.';
  }
  const uso = usoEstimado(ejecucion, topeUsd, deHoy);
  return uso === null ? 'Cancelada antes de terminar.' : `Cancelada antes de terminar · ${uso}`;
}

function textoHecha(ejecucion: EjecucionAgente, topeUsd: number | null, deHoy: boolean): string {
  const { inicio, fin } = ejecucion;
  const tardado =
    inicio !== null && fin !== null
      ? duracion((Date.parse(fin) - Date.parse(inicio)) / 1000)
      : null;
  const hecha = tardado === null ? 'Hecha' : `Hecha en ${tardado}`;
  const uso = usoEstimado(ejecucion, topeUsd, deHoy);
  return uso === null ? `${hecha}.` : `${hecha} · ${uso}`;
}
