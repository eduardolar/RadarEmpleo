import {
  EjecucionAgente,
  EstadoEjecucion,
  GastoHoy,
  TareaAgente,
} from '../interfaces/ejecucion.interface';

// Claude Code va con la suscripción de Eduardo: en pantalla se habla de «uso estimado» como porcentaje
// del tope diario y nunca de dólares, aunque la API dé los importes en el equivalente en USD.

/** La insignia de cada estado */
export const ETIQUETA_ESTADO: Record<EstadoEjecucion, string> = {
  EnCola: 'En cola',
  EnCurso: 'En curso',
  Ok: 'Hecho',
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
  RellenarEnvio: 'Relleno del envío',
  ConfirmarEnvio: 'Confirmación del envío',
};

/** Lo que está haciendo mientras va en curso */
const TRABAJANDO: Record<TareaAgente, string> = {
  Rastreo: 'Rastreando ofertas',
  AnalizarEncaje: 'Analizando el encaje',
  ExtraerRegistro: 'Leyendo el registro',
  Redactar: 'Redactando',
  Revisar: 'Revisando',
  RellenarEnvio: 'Rellenando el envío',
  ConfirmarEnvio: 'Confirmando el envío',
};

type DatosUso = Pick<
  EjecucionAgente,
  'estado' | 'inicio' | 'costeUsd' | 'costeEstimado' | 'presupuestoUsd'
>;

/** «59 s», «1 min 20 s», «5 min», «1 h 2 min», «2 h» */
export function duracion(segundos: number): string {
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
 * Qué parte del tope del día supone `usd`: «3 %», «menos del 1 %» o, como cota (`hasta`), «hasta el 3 %»
 * redondeado hacia arriba. Sin redondear a 100 lo que no llega: «100 %» solo si es el tope entero.
 * null sin tope
 */
function parteDelTope(usd: number, topeUsd: number | null, hasta: boolean): string | null {
  if (topeUsd === null || !(topeUsd > 0)) {
    return null;
  }
  // Sin los restos de coma flotante (0,45 / 15 da 3,0000000000000004 y se iría a 4)
  const porcentaje = Number(((Math.max(0, usd) / topeUsd) * 100).toFixed(6));
  if (hasta) {
    return `hasta el ${Math.ceil(porcentaje)} %`;
  }
  if (porcentaje > 0 && porcentaje < 1) {
    return 'menos del 1 %';
  }
  const redondeado = Math.round(porcentaje);
  return `${porcentaje < 100 ? Math.min(redondeado, 99) : redondeado} %`;
}

/**
 * El uso de una ejecución: «uso estimado: 1 % del tope del día» o, si no se sabe lo que ha gastado
 * (en curso o terminada sin coste), «uso estimado: hasta el 3 % del tope del día» con su presupuesto.
 * null sin tope o si no llegó a empezar (no ha gastado nada)
 */
export function usoEstimado(ejecucion: DatosUso, topeUsd: number | null): string | null {
  const { costeUsd, costeEstimado, estado, inicio, presupuestoUsd } = ejecucion;
  let parte: string | null;
  if (costeUsd !== null) {
    parte = parteDelTope(costeUsd, topeUsd, false);
  } else if (costeEstimado || (estado === 'EnCurso' && inicio !== null)) {
    parte = parteDelTope(presupuestoUsd, topeUsd, true);
  } else {
    parte = null;
  }
  return parte === null ? null : `uso estimado: ${parte} del tope del día`;
}

/** «Uso estimado de tu suscripción hoy: 3 % del tope diario»; null sin tope */
export function usoDelDia(gasto: Pick<GastoHoy, 'gastadoUsd' | 'topeUsd'>): string | null {
  const parte = parteDelTope(gasto.gastadoUsd, gasto.topeUsd, false);
  return parte === null ? null : `Uso estimado de tu suscripción hoy: ${parte} del tope diario`;
}

/**
 * La frase del estado para la tarjeta. `ahora` cuenta el tiempo de lo que va en curso; `topeUsd` es
 * el del día (de `EstadoAgentes.gasto`), o null si no se sabe y no se dice el uso
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
      return textoHecho(ejecucion, topeUsd);
    case 'Error':
      return ejecucion.error ? `No ha salido: ${ejecucion.error}` : 'No ha salido.';
    case 'Cancelada':
      return 'Cancelada.';
  }
}

/** Los avisos rojos: el registro o el perfil han cambiado mientras trabajaba Claude */
export function avisosCambio(
  ejecucion: Pick<EjecucionAgente, 'registroCambiado' | 'perfilCambiado'>,
): string[] {
  const avisos: string[] = [];
  if (ejecucion.registroCambiado) {
    avisos.push(avisoCambio('ofertas-enviadas.md'));
  }
  if (ejecucion.perfilCambiado) {
    avisos.push(avisoCambio('perfil-busqueda.md'));
  }
  return avisos;
}

function avisoCambio(archivo: string): string {
  return `Ojo: ${archivo} ha cambiado mientras trabajaba Claude. Si no lo has tocado tú, revísalo.`;
}

function textoEnCola(ejecucion: EjecucionAgente): string {
  switch (ejecucion.espera) {
    case 'Turno':
      return 'En cola: hay otra tarea de Claude en marcha.';
    case 'BloqueoOcupado':
      return ejecucion.quienBloquea
        ? `Esperando: ${ejecucion.quienBloquea} está usando Claude. Empieza sola cuando termine.`
        : 'Esperando: Claude está ocupado en otra sesión. Empieza sola cuando termine.';
    default:
      return 'En cola: empieza en unos segundos.';
  }
}

function textoEnCurso(ejecucion: EjecucionAgente, ahora: Date): string {
  // En curso y sin poder cancelar es que ya se ha pedido: la API corta el proceso en unos segundos
  if (!ejecucion.puedeCancelar) {
    return 'Cancelando: la paro en unos segundos.';
  }
  const trabajando = `${TRABAJANDO[ejecucion.tarea]}…`;
  if (ejecucion.inicio === null) {
    return trabajando;
  }
  const transcurrido = (ahora.getTime() - Date.parse(ejecucion.inicio)) / 1000;
  return `${trabajando} · ${duracion(transcurrido)} de ${duracion(ejecucion.tiempoMaximoSegundos)}`;
}

function textoHecho(ejecucion: EjecucionAgente, topeUsd: number | null): string {
  const { inicio, fin } = ejecucion;
  const hecho =
    inicio !== null && fin !== null
      ? `Hecho en ${duracion((Date.parse(fin) - Date.parse(inicio)) / 1000)}`
      : 'Hecho';
  const uso = usoEstimado(ejecucion, topeUsd);
  if (uso !== null) {
    return `${hecho} · ${uso}`;
  }
  return `${hecho}.`;
}
