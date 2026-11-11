import { Component, computed, input } from '@angular/core';
import { markdownAHtml } from '../../utils/markdown';

/**
 * Pinta un texto Markdown de Curriculum (un informe de rastreo, una sección del perfil). Va por
 * [innerHTML], así que Angular lo sanea; los enlaces se abren en otra pestaña.
 */
@Component({
  selector: 'app-texto-markdown',
  templateUrl: './texto-markdown.html',
  host: { class: 'block' },
})
export class TextoMarkdown {
  readonly markdown = input.required<string | null>();

  protected readonly html = computed(() => markdownAHtml(this.markdown()));
}
