import { CandidaturaResumen } from '../../candidaturas/interfaces/candidatura.interface';
import { normalizarEmpresa } from '../../candidaturas/utils/nombre-empresa';
import { OfertaRastreo } from '../interfaces/rastreo.interface';

/** Lo que hace falta de una candidatura del tablero para avisar de que puede ser la misma oferta */
export type CandidaturaParecida = Pick<CandidaturaResumen, 'id' | 'empresa' | 'puesto' | 'estado'>;

/**
 * Para cada oferta sin pasar al tablero, las candidaturas de la misma empresa (nombre normalizado,
 * como la API). Sirve para las que vienen del registro: no tienen enlace, así que la API no puede
 * reconocerlas y la oferta saldría repetida. Las ofertas sin candidaturas parecidas no salen.
 */
export function posiblesRepetidas(
  ofertas: readonly OfertaRastreo[],
  candidaturas: readonly CandidaturaParecida[],
): ReadonlyMap<number, CandidaturaParecida[]> {
  const porEmpresa = new Map<string, CandidaturaParecida[]>();
  for (const candidatura of candidaturas) {
    const clave = normalizarEmpresa(candidatura.empresa);
    if (clave) {
      porEmpresa.set(clave, [...(porEmpresa.get(clave) ?? []), candidatura]);
    }
  }

  const resultado = new Map<number, CandidaturaParecida[]>();
  for (const oferta of ofertas) {
    if (oferta.estado === 'Convertida') {
      continue;
    }
    const parecidas = (porEmpresa.get(normalizarEmpresa(oferta.empresa)) ?? []).filter(
      (c) => c.id !== oferta.candidaturaId,
    );
    if (parecidas.length > 0) {
      resultado.set(oferta.id, parecidas);
    }
  }
  return resultado;
}
