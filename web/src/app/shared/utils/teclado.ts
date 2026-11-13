/** Tipos de <input> que no son para escribir: en ellos los atajos de teclado de la app sí valen */
const ENTRADAS_SIN_TEXTO = new Set([
  'button',
  'checkbox',
  'color',
  'file',
  'image',
  'radio',
  'range',
  'reset',
  'submit',
]);

/** Desplegables y cuadros que manejan su propio teclado (mat-select, autocompletados, listas) */
const CONTROLES_CON_TECLADO =
  'mat-select, [role="combobox"], [role="listbox"], [role="textbox"], [role="spinbutton"]';

/**
 * ¿Se está escribiendo o eligiendo ahí? Entonces Ctrl+Z y compañía son del navegador o del control, no
 * de la app
 */
export function esCampoDeTexto(elemento: EventTarget | null): boolean {
  if (!(elemento instanceof HTMLElement)) {
    return false;
  }
  if (elemento.isContentEditable || elemento instanceof HTMLTextAreaElement) {
    return true;
  }
  if (elemento instanceof HTMLSelectElement || elemento.closest(CONTROLES_CON_TECLADO)) {
    return true;
  }
  return elemento instanceof HTMLInputElement && !ENTRADAS_SIN_TEXTO.has(elemento.type);
}
