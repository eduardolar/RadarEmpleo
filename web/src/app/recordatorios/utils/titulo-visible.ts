import { Recordatorio } from '../../candidaturas/interfaces/candidatura.interface';
import { CandidaturaDelTitulo, tituloPorDefecto } from './titulo-por-defecto';

type ConTitulo = Pick<Recordatorio, 'tipo' | 'titulo'>;

/** Sin mayúsculas ni espacios de más: un título por defecto retocado así sigue siendo el de por defecto */
function normalizar(texto: string): string {
  return texto.trim().replace(/\s+/g, ' ').toLocaleLowerCase('es');
}

/**
 * Los títulos que se han puesto solos alguna vez para ese tipo y esa candidatura: el de ahora, con y
 * sin candidatura, y el seguimiento de antes («Preguntar cómo va…»). Los guardados no se tocan, así
 * que hay que seguir reconociéndolos
 */
function titulosPorDefecto(recordatorio: ConTitulo, candidatura: CandidaturaDelTitulo): string[] {
  const { tipo } = recordatorio;
  if (tipo !== 'Seguimiento' && tipo !== 'Entrevista' && tipo !== 'Plazo') {
    return [];
  }
  const titulos = [
    tituloPorDefecto(tipo, candidatura),
    tituloPorDefecto(tipo, { ...candidatura, empresa: '' }),
    tituloPorDefecto(tipo, { ...candidatura, puesto: '' }),
    tituloPorDefecto(tipo),
  ];
  if (tipo === 'Seguimiento') {
    titulos.push(`Preguntar cómo va ${candidatura.empresa}`, 'Preguntar cómo va');
  }
  return titulos;
}

/**
 * El título que se enseña en la tarjeta de Hoy. Si el guardado es el que se pone solo, repite lo que
 * ya dicen la insignia del tipo y la línea de la empresa: se enseña solo el puesto. Uno escrito a mano
 * («Plazo de la prueba técnica») sale tal cual. Lo guardado no cambia.
 */
export function tituloVisible(
  recordatorio: ConTitulo,
  candidatura: CandidaturaDelTitulo | null,
): string {
  const puesto = candidatura?.puesto.trim();
  if (!candidatura || !puesto) {
    return recordatorio.titulo;
  }
  const guardado = normalizar(recordatorio.titulo);
  return titulosPorDefecto(recordatorio, candidatura).some((t) => normalizar(t) === guardado)
    ? puesto
    : recordatorio.titulo;
}

/**
 * El título para los `aria-label`: el guardado, entero, que es el que da el contexto a quien no ve la
 * tarjeta. Si lo que se ve (el puesto) no está dentro, se añade: quien dicta «Desarrollador .NET» al
 * control por voz tiene que encontrarlo en el nombre
 */
export function tituloAccesible(
  recordatorio: ConTitulo,
  candidatura: CandidaturaDelTitulo | null,
): string {
  const visible = tituloVisible(recordatorio, candidatura);
  return normalizar(recordatorio.titulo).includes(normalizar(visible))
    ? recordatorio.titulo
    : `${recordatorio.titulo}, ${visible}`;
}
