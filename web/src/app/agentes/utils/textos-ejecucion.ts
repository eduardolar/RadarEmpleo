import {
  EjecucionAgente,
  EstadoEjecucion,
  GastoHoy,
  TareaAgente,
} from '../interfaces/ejecucion.interface';

// Claude Code va con la suscripción de Eduardo: en pantalla se habla de «uso estimado» como porcentaje
// del tope de hoy y nunca de dólares, aunque la API dé los importes en el equivalente en USD.

/** La insignia de cada estado (en femenino, como «la tarea») */
export const ETIQUETA_ESTADO: Record<EstadoEjecucion, string> = {
  EnCola: 'En cola',
  EnCurso: 'En curso',
  Ok: 'Hecha',
  Error: 'No ha salido',
  Cancelada: 'Cancelada',
};

/** El nombre de la tarea en listas («Análisis del encaje · Acme») */
export const NOMBRE_TAREA: Record<TareaAgente, string> = {
  Rastreo: 'Rastreo de ofertas',
  AnalizarEncaje: 'Análisis del encaje',
  ExtraerRegistro: 'Lectura del registro',
  Redactar: 'Redacción',
  Revisar: 'Revisión',
  RellenarEnvio: 'Preparar el envío',
  ConfirmarEnvio: 'Confirmación del envío',
};

/** Lo que está haciendo mientras va en curso */
const TRABAJANDO: Record<TareaAgente, string> = {
  Rastreo: 'Rastreando ofertas',
  AnalizarEncaje: 'Analizando el encaje',
  ExtraerRegistro: 'Leyendo el registro',
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

/** «59 s», «1 min 20 s», «5 min», «1 h 2 min», «2 h»; null si no es un número (una fecha inválida) */
export function duracion(segundos: number): string | null {
  if (!Number.isFinite(segundos)) {
    return null;
  }
  const total = Math.max(0, Math.floor(segundos));
  if (total < 60) {
    return `${total} s`;
  }
  if (total < 3600) {
    const minutos = Math.floor(total / 60);
    const resto = total % 60;
    return resto === 0 ? `${minutos} min` : `${minutos} min ${resto} s`;
  }
  const horas = Math.floor(total / 3600);
  const minutos = Math.floor((total % 3600) / 60);
  return minutos === 0 ? `${horas} h` : `${horas} h ${minutos} min`;
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

/** «3 %», o «menos del 1 %» si ha gastado algo pero no llega al 1 % */
function parteConocida(usd: number, porcentaje: number): string {
  return porcentaje === 0 && usd > 0 ? 'menos del 1 %' : `${porcentaje} %`;
}

/**
 * El uso de una ejecución respecto al tope de hoy:
 * - con su coste, «uso estimado: 1 % del tope de hoy» (con 0, «no ha gastado nada de tu tope de hoy»);
 * - en curso, «uso estimado: como mucho el 3 % del tope de hoy», con su presupuesto;
 * - terminada sin coste, «uso estimado: no se sabe, como mucho el 3 % del tope de hoy».
 * null sin tope o si no llegó a empezar (no ha gastado nada)
 */
export function usoEstimado(ejecucion: DatosUso, topeUsd: number | null): string | null {
  const { costeUsd, costeEstimado, estado, inicio, presupuestoUsd } = ejecucion;
  if (costeUsd !== null) {
    const porcentaje = porcentajeDelTope(costeUsd, topeUsd, false);
    if (porcentaje === null) {
      return null;
    }
    return costeUsd <= 0
      ? 'no ha gastado nada de tu tope de hoy'
      : `uso estimado: ${parteConocida(costeUsd, porcentaje)} del tope de hoy`;
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
    ? `uso estimado: no se sabe, como mucho el ${cota} % del tope de hoy`
    : `uso estimado: como mucho el ${cota} % del tope de hoy`;
}

/** «Uso estimado de tu suscripción hoy: 3 % del tope» o «Hoy aún no has usado Claude»; null sin tope */
export function usoDelDia(gasto: Pick<GastoHoy, 'gastadoUsd' | 'topeUsd'>): string | null {
  const porcentaje = porcentajeDelTope(gasto.gastadoUsd, gasto.topeUsd, false);
  if (porcentaje === null) {
    return null;
  }
  if (gasto.gastadoUsd <= 0) {
    return 'Hoy aún no has usado Claude';
  }
  return `Uso estimado de tu suscripción hoy: ${parteConocida(gasto.gastadoUsd, porcentaje)} del tope`;
}

/**
 * La frase del estado para la tarjeta. `ahora` cuenta el tiempo de lo que va en curso; `topeUsd` es
 * el de hoy (de `EstadoAgentes.gasto`), o null si no se sabe y no se dice el uso
 */
export function textoEstado(
  ejecucion: EjecucionAgente,
  ahora: Date,
  topeUsd: number | null,
): string {
  switch (ejecucion.estado) {
    case 'EnCola':
      return textoEnCola(ejecucion);
    case 'EnCurso':
      return textoEnCurso(ejecucion, ahora);
    case 'Ok':
      return textoHecha(ejecucion, topeUsd);
    case 'Error':
      return ejecucion.error ? `No ha salido: ${ejecucion.error}` : 'No ha salido.';
    case 'Cancelada':
      // También la cancela la API al arrancar si estaba en cola: no se dice quién
      return 'Cancelada antes de terminar.';
  }
}

/** Los avisos rojos: el registro o el perfil han cambiado mientras trabajaba Claude */
export function avisosCambio(
  ejecucion: Pick<EjecucionAgente, 'registroCambiado' | 'perfilCambiado'>,
): string[] {
  const avisos: string[] = [];
  if (ejecucion.registroCambiado) {
    avisos.push(avisoCambio('registro'));
  }
  if (ejecucion.perfilCambiado) {
    avisos.push(avisoCambio('perfil'));
  }
  return avisos;
}

export function avisoCambio(archivo: ArchivoVigilado): string {
  return `Ojo: ${NOMBRE_ARCHIVO[archivo]} ha cambiado mientras trabajaba Claude. Si no lo has tocado tú, revísalo.`;
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

function textoEnCurso(ejecucion: EjecucionAgente, ahora: Date): string {
  // En curso y sin poder cancelar es que ya se ha pedido: la API corta el proceso en unos segundos
  if (!ejecucion.puedeCancelar) {
    return 'Cancelando… se parará en unos segundos.';
  }
  const trabajando = `${TRABAJANDO[ejecucion.tarea]}…`;
  const transcurrido =
    ejecucion.inicio === null
      ? null
      : duracion((ahora.getTime() - Date.parse(ejecucion.inicio)) / 1000);
  if (transcurrido === null) {
    return trabajando;
  }
  const maximo = duracion(ejecucion.tiempoMaximoSegundos);
  return maximo === null
    ? `${trabajando} · ${transcurrido}`
    : `${trabajando} · ${transcurrido} (máximo ${maximo})`;
}

function textoHecha(ejecucion: EjecucionAgente, topeUsd: number | null): string {
  const { inicio, fin } = ejecucion;
  const tardado =
    inicio !== null && fin !== null
      ? duracion((Date.parse(fin) - Date.parse(inicio)) / 1000)
      : null;
  const hecha = tardado === null ? 'Hecha' : `Hecha en ${tardado}`;
  const uso = usoEstimado(ejecucion, topeUsd);
  return uso === null ? `${hecha}.` : `${hecha} · ${uso}`;
}
