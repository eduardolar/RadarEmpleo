// Portales de empleo conocidos, por el dominio del enlace (sin «www.» ni subdominios del país)
const PORTALES: readonly { dominio: string; nombre: string }[] = [
  { dominio: 'linkedin.com', nombre: 'LinkedIn' },
  { dominio: 'infojobs.net', nombre: 'InfoJobs' },
  { dominio: 'tecnoempleo.com', nombre: 'Tecnoempleo' },
  { dominio: 'indeed.com', nombre: 'Indeed' },
  { dominio: 'glassdoor.es', nombre: 'Glassdoor' },
  { dominio: 'glassdoor.com', nombre: 'Glassdoor' },
  { dominio: 'jooble.org', nombre: 'Jooble' },
  { dominio: 'remotive.com', nombre: 'Remotive' },
  { dominio: 'getmanfred.com', nombre: 'Manfred' },
  { dominio: 'welcometothejungle.com', nombre: 'Welcome to the Jungle' },
  { dominio: 'computrabajo.com', nombre: 'Computrabajo' },
  { dominio: 'infoempleo.com', nombre: 'Infoempleo' },
  { dominio: 'jobfluent.com', nombre: 'JobFluent' },
];

/**
 * Nombre del portal de empleo de un enlace («https://es.linkedin.com/jobs/...» → «LinkedIn»), para
 * rellenar el campo Portal sin escribirlo. Si el enlace no es de un portal conocido, null: mejor
 * dejarlo vacío que adivinar.
 */
export function portalDesdeEnlace(enlace: string | null | undefined): string | null {
  let host: string;
  try {
    host = new URL(enlace?.trim() ?? '').hostname.toLowerCase();
  } catch {
    return null;
  }
  const portal = PORTALES.find((p) => host === p.dominio || host.endsWith(`.${p.dominio}`));
  return portal?.nombre ?? null;
}
