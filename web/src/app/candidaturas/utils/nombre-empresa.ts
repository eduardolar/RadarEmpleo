// Copia en el front de NombresEmpresa.Normalizar (RadarEmpleo.Domain/NombresEmpresa.cs): la forma de
// un nombre de empresa para COMPARAR, no para enseñar. Si se cambia allí, hay que cambiarla aquí.

// Centinela de uso privado con el que se protege la «ñ» antes de quitar las tildes
const CENTINELA_ENIE = '';

// Forma jurídica al final del nombre, ya en minúsculas y sin tildes. Exige espacio, coma o paréntesis
// delante para no comerse el final de «Nasa» o «Visa». «S.A.L.» y «S.p.A.» solo con puntos.
const FORMAS =
  String.raw`s\.?\s?l\.?(\s?[ulp]\.?|\s?n\.?\s?e\.?)?` +
  String.raw`|s\.?\s?a\.?(\s?u\.?)?` +
  String.raw`|s\.\s?a\.\s?l\.?` +
  String.raw`|s\.?\s?a\.?\s?s\.?` +
  String.raw`|s\.?\s?coop\.?(\s?and\.?)?` +
  String.raw`|s\.\s?p\.\s?a\.?` +
  String.raw`|ltd\.?|inc\.?|llc\.?|gmbh|b\.?\s?v\.?` +
  String.raw`|sociedad\s+(limitada|anonima)(\s+(unipersonal|laboral|profesional))?`;

const FORMA_JURIDICA = new RegExp(
  String.raw`(?:[\s,]+(?:${FORMAS})|[\s,]*\(\s*(?:${FORMAS})\s*\))[\s.,]*$`,
);

/** Minúsculas, sin tildes (la «ñ» se queda), un solo espacio y sin «S.L.», «S.A.»... al final */
export function normalizarEmpresa(nombre: string | null | undefined): string {
  if (!nombre?.trim()) {
    return '';
  }
  let texto = nombre
    .trim()
    .normalize('NFC')
    .toLowerCase()
    .replaceAll('ñ', CENTINELA_ENIE)
    .normalize('NFD')
    .replace(/\p{Mn}/gu, '')
    .normalize('NFC')
    .replaceAll(CENTINELA_ENIE, 'ñ')
    .replace(/\s+/g, ' ');

  let anterior: string;
  do {
    anterior = texto;
    texto = texto.replace(FORMA_JURIDICA, '');
  } while (texto !== anterior);

  return texto
    .trim()
    .replace(/[.,;-]+$/, '')
    .trim();
}
