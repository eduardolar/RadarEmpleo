import { Marked, Tokens } from 'marked';

/** Atributos de los enlaces: se abren en otra pestaña sin dar acceso a la app desde la página abierta */
const ATRIBUTOS_ENLACE = 'target="_blank" rel="noopener noreferrer"';

// Instancia propia: configurar el `marked` global cambiaría el de cualquier otro que lo use
const procesador = new Marked({
  gfm: true,
  breaks: false,
  renderer: {
    link({ href, title, tokens }: Tokens.Link): string {
      const texto = this.parser.parseInline(tokens);
      const titulo = title ? ` title="${escaparAtributo(title)}"` : '';
      return `<a href="${escaparAtributo(limpiarUrl(href))}"${titulo} ${ATRIBUTOS_ENLACE}>${texto}</a>`;
    },
  },
});

/**
 * Markdown de Curriculum (informes de rastreo, perfil) a HTML. Las URL sueltas también salen como
 * enlaces. El resultado se pinta con [innerHTML], que pasa por el saneado de Angular: lo que no sea
 * seguro (scripts, `javascript:`...) no llega a la página.
 */
export function markdownAHtml(markdown: string | null | undefined): string {
  if (!markdown?.trim()) {
    return '';
  }
  return procesador.parse(markdown, { async: false });
}

/** Como marked: las tildes y espacios se codifican, pero un «%C3%A9» ya codificado se queda igual */
function limpiarUrl(url: string): string {
  try {
    return encodeURI(url).replace(/%25/g, '%');
  } catch {
    return '';
  }
}

function escaparAtributo(texto: string): string {
  return texto
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}
