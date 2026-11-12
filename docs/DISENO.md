# Guía de diseño de RadarEmpleo

> **RadarEmpleo.** _¿La de trabajar te la sabes?_

Esta guía dice cómo se ve RadarEmpleo y por qué. Está aplicada en el front (`web/`): los valores de
aquí son los mismos que hay en `web/src/styles.css`, `web/src/material-theme.scss` y
`web/src/_theme-colors.scss`. Si se cambia uno, se cambia el otro.

---

## 1. Dirección de arte

### Concepto: «Ventanilla brutalista»

Buscar trabajo es papeleo: rellenar el mismo formulario una y otra vez, esperar el sello y volver a
preguntar en la ventanilla. RadarEmpleo lo convierte en una herramienta de desarrollador con
**neobrutalismo limpio**: bloques planos de gris, tinta casi negra, un único color de acento (el
lima con el que se marca lo importante), rótulos en mayúsculas como los de un impreso oficial y el
sello de goma cuando por fin se envía la candidatura.

**Por qué este y no otro.** El neobrutalismo es honesto con la estructura: se ve qué es un botón,
qué es una tarjeta y qué es un dato, sin sombras difuminadas ni degradados que despisten. Eso casa
con una app de trabajo diario y con el humor del eslogan: la ventanilla de siempre, sin adornos.
Descartados: el estilo «Material de serie», correcto pero sin carácter, y el brutalismo «de
manual», recargado de marcos, que en una pantalla con muchas tarjetas cansa la vista.

**Reglas del brutalismo limpio:**

1. **Un solo marco por nivel.** El borde grueso (2 px) con sombra dura va solo en el elemento
   principal de cada nivel: la tarjeta, la cabecera de la ficha, el estado vacío principal y el
   botón principal. Lo que va dentro (columnas, paneles, cajas internas, etiquetas) no lleva marco:
   se separa con diferencias sutiles de fondo.
2. **Esquinas casi rectas.** 2-6 px. Los círculos de verdad (puntos de estado) siguen redondos.
3. **Bloques planos y alto contraste.** Nada de degradados, transparencias suaves ni sombras
   difuminadas.
4. **El lima, solo para lo importante.** El relleno lima sólido es para la acción principal (de
   noche, todos los botones principales; de día, «Marcar enviada»), la insignia de encaje alto y
   las marcas de «activo». Nunca para un fondo de sección ni para la sección activa del menú.
5. **El humor, en la letra pequeña.** Estados vacíos, microcopys secundarios y el eslogan. Botones,
   etiquetas y errores dicen lo que hacen. Nunca a costa de quien busca trabajo ni de las empresas.

### Paleta: grises + lima, el claro como espejo del oscuro

| Nombre           | Claro                 | Oscuro    | Rol                                                         | Token (Tailwind)                                    |
| ---------------- | --------------------- | --------- | ----------------------------------------------------------- | --------------------------------------------------- |
| Fondo            | `#f9fafb`             | `#111827` | Fondo de página                                             | `surface`                                           |
| Tarjeta          | `#ffffff`             | `#0b1120` | Tarjetas, paneles                                           | `surface-container-lowest`                          |
| Gris de sección  | `#f3f4f6`             | `#1f2937` | Columnas del tablero, barra de herramientas, cajas internas | `surface-container`                                 |
| Gris de insignia | `#e5e7eb`             | `#273449` | Insignias neutras, botón secundario gris                    | `surface-container-high`                            |
| **Tinta**        | `#111827`             | `#f3f4f6` | Texto principal                                             | `on-surface`                                        |
| Lápiz            | `#4b5563`             | `#d1d5db` | Texto secundario                                            | `on-surface-variant`                                |
| **Primario**     | `#111827`             | `#a3e635` | Botón principal, foco, enlaces                              | `primary` / `on-primary`                            |
| Enlaces de texto | `#4d7c0f`             | `#a3e635` | Botones de texto («Editar», «Ver la oferta»)                | `mat.button-overrides`                              |
| Trazo            | `#111827`             | `#d1d5db` | Bordes de 2 px (tarjetas, campos)                           | `trazo`                                             |
| Sombra dura      | `#111827`             | `#6b7280` | Sombra desplazada 3, 4 o 6 px                               | `shadow-dura-sm` / `shadow-dura` / `shadow-dura-lg` |
| Menú             | `#111827`             | `#0b1120` | Menú lateral y barra inferior                               | `tinta-noche`                                       |
| Activo del menú  | `#1f2937`             | `#1f2937` | Fondo de la sección activa                                  | `tinta`                                             |
| Texto del menú   | `#f3f4f6` / `#d1d5db` | igual     | Etiqueta / descripción                                      | `papel` / `tinta-clara`                             |
| **Lima**         | `#a3e635`             | `#a3e635` | Lo importante; siempre con `#111827` encima                 | `fosforito` / `on-fosforito`                        |
| Lima suave       | `#ecfccb`             | `#1f2b0e` | Avisos amables («Puede que ya la tengas»)                   | `fosforito-suave`                                   |
| **Humo**         | `#b4410f`             | `#fdba74` | Red flags: la pega, «Tiro largo», «No encaja»               | `humo`                                              |
| Humo suave       | `#fdece3`             | `#3b1f14` | Fondo del inconveniente y de «Tiro largo»                   | `humo-suave`                                        |
| Luz verde        | `#15803d`             | `#5fcf86` | «Conectado»                                                 | `ok`, `ok-suave`, `on-ok`                           |
| Error            | `#ba1a1a`             | `#ffb4ab` | Errores y borrar (rol `error` de Material)                  | `error`                                             |

**Colores por estado** (con `data-estado` en el contenedor y `bg-estado`, `bg-estado-suave` y
`text-on-estado` dentro):

| Estado     | Fuerte (claro / oscuro) |
| ---------- | ----------------------- |
| Detectada  | `#0e7490` / `#4fc3dc`   |
| Preparada  | `#7c3aed` / `#a78bfa`   |
| Enviada    | `#2563eb` / `#7aa7ff`   |
| Entrevista | `#b45309` / `#f0b450`   |
| Oferta     | `#15803d` / `#5fcf86`   |
| Descartada | `#9b6b74` / `#b48f97`   |
| Cerrada    | `#6b7280` / `#959ca6`   |

**Contraste comprobado (WCAG AA), sobre el fondo real donde aparece cada texto:**

- Texto principal: 17:1 de día y 16:1 de noche. Lima con `#111827` encima: 11:1.
- Texto secundario (`on-surface-variant`): 7,6:1 sobre la tarjeta y 7,2:1 sobre el fondo de día;
  13:1 y 12:1 de noche. Sin opacidades: ningún texto lleva `/60` ni `/70`.
- Humo (la pega): `#b4410f` da 5,7:1 sobre la tarjeta, 5,2:1 sobre el gris de sección y 4,9:1
  sobre humo suave. De noche `#fdba74`: 11:1 sobre la tarjeta, 10,4:1 sobre el fondo, 8,7:1 sobre
  el gris de sección y 8,9:1 sobre humo suave.
- Menú: 16:1 la etiqueta y 11,7:1 la descripción; en la sección activa (#1f2937), 9,7:1 el lima
  y 13:1 la descripción.
- Bordes de 2 px: 17:1 de día y 12:1 de noche. La sombra de noche (`#6b7280`) da 3,6:1 sobre el
  fondo, para que se siga viendo.

### Tipografía

| Uso              | Fuente                                                  | Pesos               | Dónde                                                                                                                           |
| ---------------- | ------------------------------------------------------- | ------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Titulares        | **Bricolage Grotesque**                                 | 700 y 800           | Títulos de página (2-2,5 rem en 800), secciones, marca, estados vacíos, sello y diálogos                                        |
| Texto y datos    | **Roboto**                                              | 400, 500, 600 y 700 | Formularios, tarjetas, botones                                                                                                  |
| Datos de máquina | Monoespaciada del sistema (Cascadia, Consolas, SF Mono) | La que haya         | Fechas, días en el estado, contadores, el eslogan y los microcopys «de consola»                                                 |
| Rótulos          | Roboto 700 en MAYÚSCULAS, 11 px, espaciado 0,06-0,08 em | —                   | Empresa en las tarjetas, cabeceras de columna, «Cambiar estado», etiquetas de los campos y de los datos (`etiqueta`), insignias |

Las fuentes van en `@fontsource` (nada de CDN). Bricolage solo en latin y dos pesos: unos 44 kB. La
monoespaciada no pesa nada.

---

## 2. Iconografía e imaginería

### Logotipo

- **Isotipo** (`web/public/logo-radarempleo.svg`): un bloque de tinta `#111827` con los anillos del
  radar en gris, el barrido en lima y **la oferta encontrada como un cuadrado de lima con su sombra
  dura**. Es el favicon, la cabecera del menú y la presentación de Hoy en el móvil.
- **Logotipo**: el isotipo + «Radar**Empleo**» en Bricolage 800. Sobre el menú, «Radar» en papel y
  «Empleo» en lima. Sobre fondo claro, «Empleo» sobre un bloque de lima con texto `#111827`.
- **Eslogan**: «¿La de trabajar te la sabes?», en monoespaciada y precedido de `//`, como un
  comentario de código.

### Pictogramas: trazo editorial

Rejilla de 24×24 (menú) o 20×20 (en línea), trazo de 1,7 a 2,2 px, `currentColor`, puntas cuadradas
en los iconos nuevos. Las ilustraciones de los estados vacíos son bloques de lima o de tinta con
borde de 3 px y sombra dura de 5 px, en el mismo idioma que las tarjetas.

| Pieza                 | Metáfora                                                    |
| --------------------- | ----------------------------------------------------------- |
| Hoy                   | La taza del café de la mañana                               |
| Tablero               | Un corcho con fichas en columnas                            |
| Apuntar               | Un lápiz                                                    |
| Rastreos              | El radar de la marca                                        |
| Ficha                 | El expediente con su sello de goma                          |
| Detector de repetidas | Dos fichas superpuestas con una lupa                        |
| Enviar                | Avión de papel en el botón y sello «ENVIADA» al confirmarse |
| Pega                  | La banderita: la red flag, literal                          |
| Mover a…              | «⋮» de tres cuadrados                                       |

---

## 3. Componentes y microdetalles

### Insignia: un solo patrón para todas las etiquetas

`insignia` (styles.css): bloque plano sin borde, 24 px de alto, 11 px en MAYÚSCULAS y negrita,
icono + texto. El color codifica el significado:

| Insignia     | Fondo / texto                                              | Icono                      |
| ------------ | ---------------------------------------------------------- | -------------------------- |
| Encaje alto  | Lima / `#111827`                                           | Estrella                   |
| Encaje medio | Gris de insignia / tinta                                   | Medio círculo              |
| Sin valorar  | Gris de insignia / lápiz                                   | Interrogación              |
| Tiro largo   | Humo suave / humo                                          | Flecha en parábola         |
| No encaja    | Humo / blanco (`#2a1208` de noche)                         | Banderita                  |
| Estado       | `estado-suave` / `on-estado`                               | Punto del color del estado |
| Contadores   | Gris de insignia o tarjeta / tinta, cifra en monoespaciada | —                          |

La pega no es insignia: es una línea en humo con la banderita y «Pega:» en negrita, la misma
banderita que la de «No encaja». Nada depende solo del color: todas llevan texto.

### Tarjeta del tablero

- Borde de 2 px y sombra dura de 3 px (4 px al pasar por encima, 8 px de lima al arrastrarla).
- Franja del color del estado a la izquierda.
- Arriba, la empresa como rótulo y el puesto en negrita, con el botón **«⋮» (Mover a…)** en su
  propia columna en la esquina: 48 px de zona de toque, `aria-label` «Mover a otro estado: puesto»
  y el menú de siempre. Es la alternativa con teclado y en el móvil al arrastrar.
- Debajo, lugar, pega e insignias (estado en el móvil, encaje y días) en una fila que baja de
  línea si no cabe: los textos largos nunca se solapan con nada.

### Tablero

- **Barra de herramientas** pegada a la cabecera: buscador (la ayuda va de marcador de posición) y
  el total de candidaturas en monoespaciada.
- **Columnas** sin borde, en gris de sección, con la franja del estado y el rótulo en mayúsculas.
  Se reparten el ancho (`flex-1` con un mínimo de 12,5 rem) y **Oferta, Descartada, Cerrada y las
  vacías salen plegadas** en una tira de 56 px con el total y el nombre en vertical; al pulsarla se
  despliega, y se despliega sola al mover o soltar una tarjeta en ella. Así cabe sin desplazamiento
  lateral en 1366, 1440 y 1600 px. Las tiras siguen siendo zonas donde soltar.
- La lista de cada columna deja 20 px abajo y 12 px a la derecha para la sombra de la última
  tarjeta.
- **Filtros del móvil**: el activo no copia el botón principal. Va en gris más oscuro, con una raya
  de tinta abajo, negrita y una marca de visto.

### Menú

- Menú lateral y barra inferior de tinta en los dos modos, sin transparencias.
- **Sección activa**: fondo `#1f2937`, barra de lima a la izquierda (abajo en la barra del móvil),
  e icono y etiqueta en lima. El relleno de lima queda para la acción principal.

### Formularios

- Etiqueta **siempre arriba** (`floatLabel="always"` en cada `mat-form-field`; el proveedor global
  `MAT_FORM_FIELD_DEFAULT_OPTIONS` metía unos 100 kB en la carga inicial), en mayúsculas
  pequeñas y negrita. Los marcadores de posición sirven de ejemplo.
- Campos con borde de 2 px (3 px con el foco), sin sombra.
- «Añadir más datos» es un desplegable secundario: botón gris con ▾, nunca lima.
- El botón de guardar va separado del último campo; en el móvil, de ancho completo.

### Maquetación de página

Todas las páginas comparten el mismo padding (20 px en el móvil y 24 px desde `sm`, a la derecha
del menú lateral en el PC). La regla es una sola: **el contenido empieza en el mismo borde que el
título**, salvo dos excepciones que van centradas.

| Tipo                   | Páginas                    | Alineación                                                                 | Ancho máximo                                           |
| ---------------------- | -------------------------- | -------------------------------------------------------------------------- | ------------------------------------------------------ |
| Tablero                | Tablero                    | Izquierda, a todo el ancho                                                 | — (las columnas se reparten el ancho)                  |
| Lista o rejilla        | Rastreos, Más              | Izquierda                                                                  | `max-w-6xl` (Rastreos), `max-w-4xl` (Más)              |
| Lectura                | Ficha, Perfil, Importación | Izquierda                                                                  | `max-w-5xl` (Ficha), `max-w-3xl` (Perfil, Importación) |
| Formulario suelto      | Apuntar                    | **Centrado**                                                               | `max-w-2xl`                                            |
| Estado vacío de página | Hoy (sin datos)            | Título a la izquierda; el estado vacío, **centrado** en el área de trabajo | `max-w-xl`                                             |

Así, al cambiar de sección con el menú, el título no salta de sitio. Lo que va centrado es lo que
se lee como una sola pieza: un formulario corto o un mensaje.

### Más

Rejilla de tarjetas-enlace (2 columnas desde `sm`, 1 en el móvil), una por pantalla que existe hoy:
«Perfil y filtros» e «Importar del registro». Cada tarjeta es entera el enlace, con icono en bloque
gris, título con flecha y una línea de descripción; borde de 2 px, sombra dura de 4 px que crece al
pasar por encima y desaparece al pulsarla (se hunde), foco de 3 px y al menos 96 px de alto.
Debajo, una línea en monoespaciada dice lo que llegará (agenda y recordatorios), sin botones: no se
pinta ninguna función que no exista.

### Botones

| Tipo                | Aspecto                                                                                                       |
| ------------------- | ------------------------------------------------------------------------------------------------------------- |
| Principal (relleno) | `#111827` de día, lima de noche; sombra dura de 3 px; al pulsarlo se hunde (desplaza 3 px y pierde la sombra) |
| Secundario (borde)  | Borde de 2 px, sin sombra                                                                                     |
| De texto            | Plano, en el color de los enlaces                                                                             |
| «Marcar enviada»    | El momento cumbre: lima también de día, borde de 3 px, sombra de 5 px y avión de papel                        |

### Estados vacíos

| Dónde                      | Ilustración                                                           | Texto principal                                    | Microcopy                                                                                     | Acciones                                                 |
| -------------------------- | --------------------------------------------------------------------- | -------------------------------------------------- | --------------------------------------------------------------------------------------------- | -------------------------------------------------------- |
| Hoy                        | Taza de café en un bloque de lima, centrada en la pantalla, sin marco | «Aquí se juntará lo del día.» (16 px el texto)     | «De momento, café tranquilo.»                                                                 | **Buscar nuevas ofertas** (→ Rastreos) y «Ir al tablero» |
| Tablero sin candidaturas   | Página de clasificados con el hueco del anuncio                       | «Todavía no hay candidaturas.»                     | —                                                                                             | «Nueva candidatura»                                      |
| Columna vacía              | Ficha discontinua con chincheta del color del estado                  | «Sin candidaturas»                                 | Una frase por estado; Entrevista: «Nadie ha respondido todavía. Los procesos van a su ritmo.» | —                                                        |
| Rastreos sin rastreos      | El radar en un bloque de tinta                                        | «Todavía no hay rastreos.»                         | «El radar está encendido. Solo falta que pase algo por delante.»                              | —                                                        |
| Bandeja de rastreos a cero | Bandeja en un bloque de lima                                          | «Ya has mirado todas las ofertas de este rastreo.» | «Bandeja a cero. Hasta el próximo rastreo.»                                                   | Ir a otro rastreo, si tiene ofertas                      |

### El momento cumbre: pasar a «Enviada»

1. El botón «Marcar enviada» (ver Botones). Se hunde al pulsarlo.
2. Cae el **sello «ENVIADA»** con la fecha sobre la cabecera de la ficha: doble borde de 4 px del
   color de Enviada, girado −6°, entra grande y rebota en 0,5 s. Al lado: «La pelota está en su
   tejado.».
3. El aviso de abajo, de tinta con borde y sombra de lima, dice que se ha programado el
   seguimiento a 7 días.

### Movimiento

Todo respeta `prefers-reduced-motion`: el sello aparece ya puesto, los botones y las tarjetas no se
desplazan (solo cambia la sombra) y no se gira la tarjeta al arrastrarla.

### Accesibilidad

Contraste AA (ver la paleta), zonas de toque de 48 px en el móvil y en pantallas táctiles, foco
visible (borde de 3 px en Material con `strong-focus-indicators` y anillo de 3 px en lo propio),
nada que dependa solo del color, iconos e ilustraciones con `aria-hidden`, y modo oscuro según el
sistema.

---

## 4. Tokens

### Dónde vive cada cosa

| Archivo                           | Qué tiene                                                                                                                                                |
| --------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `web/src/_theme-colors.scss`      | Paletas de Material generadas (`ng generate @angular/material:theme-color`)                                                                              |
| `web/src/material-theme.scss`     | `mat.theme()`, la paleta de grises y lima con `theme-overrides`, formas y bordes de Material con `*-overrides`, foco fuerte y modo según el sistema      |
| `web/src/styles.css`              | Fuentes, radios, colores de marca, estados, encajes, `insignia`, `etiqueta`, sombras, sello y Material en brutalista (sombras duras y paneles flotantes) |
| `web/public/logo-radarempleo.svg` | Isotipo y favicon                                                                                                                                        |

### Material

```scss
@include mat.theme-overrides(
    (
        primary: light-dark(#111827, #a3e635),
        on-primary: light-dark(#f3f4f6, #111827),
        surface: light-dark(#f9fafb, #111827),
        surface-container-lowest: light-dark(#ffffff, #0b1120),
        surface-container: light-dark(#f3f4f6, #1f2937),
        on-surface: light-dark(#111827, #f3f4f6),
        on-surface-variant: light-dark(#4b5563, #d1d5db),
        // … (ver material-theme.scss)
    )
);
@include mat.button-overrides(
    (
        text-label-text-color: light-dark(#4d7c0f, #a3e635),
        filled-container-shape: 4px,
        outlined-outline-width: 2px,
        …,
    )
);
@include mat.form-field-overrides(
    (
        outlined-container-shape: 4px,
        outlined-outline-width: 2px,
        outlined-focus-outline-width: 3px,
        …,
    )
);
@include mat.dialog-overrides(
    (
        container-shape: 6px,
    )
);
@include mat.menu-overrides(
    (
        container-shape: 4px,
    )
);
@include mat.strong-focus-indicators(
    (
        border-color: var(--mat-sys-primary),
        border-width: 3px,
    )
);
```

### Tailwind 4

```css
@theme {
    --radius-sm: 2px;
    --radius-md: 3px;
    --radius-lg: 4px;
    --radius-xl: 4px;
    --radius-2xl: 6px;
    --radius-3xl: 8px;
}
@theme inline {
    --color-fosforito: var(--re-fosforito); /* #a3e635 */
    --color-on-fosforito: var(--re-on-fosforito); /* #111827 */
    --color-humo: var(--re-humo); /* light-dark(#b4410f, #fdba74) */
    --color-trazo: var(--re-trazo); /* light-dark(#111827, #d1d5db) */
    --shadow-dura-sm: 3px 3px 0 0 var(--re-sombra);
    --shadow-dura: 4px 4px 0 0 var(--re-sombra);
    --shadow-dura-lg: 6px 6px 0 0 var(--re-sombra);
    --font-display: "Bricolage Grotesque", Roboto, system-ui, sans-serif;
    --font-mono:
        ui-monospace, "Cascadia Mono", Consolas, "SF Mono", Menlo, monospace;
    --animate-sello: sello-entra 520ms cubic-bezier(0.2, 0.9, 0.3, 1.25) both;
}
```

### Recetas

| Para                                         | Clases                                                                                                                                                          |
| -------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Tarjeta                                      | `rounded-md border-2 border-trazo bg-surface-container-lowest shadow-dura-sm`                                                                                   |
| Documento principal / estado vacío principal | `rounded-2xl border-2 border-trazo bg-surface-container-lowest shadow-dura-lg`                                                                                  |
| Panel                                        | `rounded-2xl bg-surface-container-lowest` (sin marco)                                                                                                           |
| Caja interna, columna, barra de herramientas | `rounded-md bg-surface-container` (sin marco)                                                                                                                   |
| Insignia                                     | `insignia` + `bg-encaje text-on-encaje` (con `data-encaje`), `bg-estado-suave text-on-estado` (con `data-estado`) o `bg-surface-container-high text-on-surface` |
| Rótulo                                       | `etiqueta text-on-surface-variant`                                                                                                                              |
| Pega                                         | `text-humo` + banderita + «Pega:» en negrita                                                                                                                    |
| Botón principal / secundario                 | `mat-flat-button` / `mat-stroked-button`                                                                                                                        |
| «Marcar enviada»                             | `mat-flat-button` + `boton-enviar`                                                                                                                              |
| Sello                                        | `-rotate-6 animate-sello motion-reduce:animate-none`                                                                                                            |
| Datos                                        | `font-mono` (fechas, días, contadores)                                                                                                                          |

**No**: marcos dentro de marcos; lima como fondo de secciones o como color de texto sobre claro;
transparencias en el texto; sombras difuminadas; colores sueltos de Tailwind (`slate-*`, `blue-*`):
siempre tokens, para que funcione el modo oscuro.
