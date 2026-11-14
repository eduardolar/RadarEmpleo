import { TipoRecordatorio } from '../../candidaturas/interfaces/candidatura.interface';

/** Lo que hace falta de la candidatura para ponerle título a su recordatorio */
export interface CandidaturaDelTitulo {
  empresa: string;
  puesto: string;
}

/**
 * Título con el que empieza un recordatorio nuevo según su tipo, para no tener que escribirlo. Con
 * «Otro» va vacío: ahí no hay nada que adivinar. El de seguimiento es el mismo que pone la API al
 * programarlo sola («Seguimiento: puesto en empresa»), para que en la ficha se lean todos igual.
 */
export function tituloPorDefecto(
  tipo: TipoRecordatorio,
  candidatura?: CandidaturaDelTitulo | null,
): string {
  const empresa = candidatura?.empresa.trim();
  const puesto = candidatura?.puesto.trim();
  switch (tipo) {
    case 'PublicacionLinkedIn':
      return 'Publicar en LinkedIn';
    case 'Entrevista':
      return empresa ? `Entrevista en ${empresa}` : 'Entrevista';
    case 'Seguimiento':
      if (!puesto) {
        return empresa ? `Seguimiento de ${empresa}` : 'Seguimiento';
      }
      return empresa ? `Seguimiento: ${puesto} en ${empresa}` : `Seguimiento: ${puesto}`;
    case 'Plazo':
      return empresa ? `Plazo de ${empresa}` : 'Plazo';
    case 'Otro':
      return '';
  }
}
