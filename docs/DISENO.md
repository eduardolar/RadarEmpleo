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
   se separa con diferencias sutiles de fondo. La excepción son los recordatorios (ver «Inicio y
   recordatorios»): son muchos y pequeños, así que van en una tarjeta limpia de borde fino, sin
   sombra dura.
2. **Esquinas casi rectas.** 2-6 px. Los círculos de verdad (puntos de estado) siguen redondos.
3. **Bloques planos y alto contraste.** Nada de degradados, transparencias suaves ni sombras
   difuminadas. Única excepción: el degradado del borde del tablero hacia el lienzo, que solo sale
   en el lado donde hay columnas ocultas y solo para decir que las hay (ver «Tablero»).
4. **El lima, solo para lo importante.** El relleno lima sólido es para la acción principal (de
   noche, todos los botones principales; de día, «Marcar enviada»), la insignia de encaje alto y
   las marcas de «activo». Nunca para un fondo de sección ni para la sección activa del menú.
5. **El humor, en la letra pequeña.** Estados vacíos, microcopys secundarios y el eslogan. Botones,
   etiquetas y errores dicen lo que hacen. Nunca a costa de quien busca trabajo ni de las empresas.

### Paleta: grises + lima, el claro como espejo del oscuro

| Nombre           | Claro                 | Oscuro    | Rol                                                         | Token (Tailwind)                                    |
| ---------------- | --------------------- | --------- | ----------------------------------------------------------- | --------------------------------------------------- |
| Fondo (lienzo)   | `#f9fafb`             | `#0b111e` | Fondo de página                                             | `surface`                                           |
| Tarjeta          | `#ffffff`             | `#111827` | Tarjetas, paneles: de noche, algo más clara que el lienzo   | `surface-container-lowest`                          |
| Gris de sección  | `#f3f4f6`             | `#1f2937` | Columnas del tablero, barra de herramientas, cajas internas | `surface-container`                                 |
| Gris de insignia | `#e5e7eb`             | `#273449` | Insignias neutras, botón secundario gris                    | `surface-container-high`                            |
| **Tinta**        | `#111827`             | `#f3f4f6` | Texto principal                                             | `on-surface`                                        |
| Lápiz            | `#4b5563`             | `#d1d5db` | Texto secundario                                            | `on-surface-variant`                                |
| **Primario**     | `#111827`             | `#a3e635` | Botón principal, foco, enlaces                              | `primary` / `on-primary`                            |
| Enlaces de texto | `#4d7c0f`             | `#a3e635` | Botones de texto («Editar», «Ver la oferta») y títulos-enlace al pasar el ratón o con el foco | `mat.button-overrides`, `enlace` |
| Trazo            | `#111827`             | `#d1d5db` | Bordes de 2 px (tarjetas, campos)                           | `trazo`                                             |
| Sombra dura      | `#111827`             | `#6b7280` | Sombra desplazada 3, 4 o 6 px                               | `shadow-dura-sm` / `shadow-dura` / `shadow-dura-lg` |
| Borde fino       | `#111827` al 10 %     | blanco al 8 % | Borde de 1 px de la tarjeta de recordatorio             | `borde-fino`                                        |
| Borde fino activo | `#4d7c0f` al 80 %    | lima al 55 % | El mismo borde al pasar el ratón o con el foco dentro    | `borde-fino-activo`                                 |
| Despejado        | `#4d7c0f` al 6 %      | lima al 5 % | Fondo del aviso «Todo despejado por hoy»                  | `despejado`                                         |
| Menú             | `#111827`             | `#111827` | Menú lateral y barra inferior: de noche, como las tarjetas  | `tinta-noche`                                       |
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

- Texto principal: 17:1 de día; de noche, 17,1:1 sobre el lienzo y 16,1:1 sobre la tarjeta. Lima
  con `#111827` encima: 11:1.
- Texto secundario (`on-surface-variant`): 7,6:1 sobre la tarjeta y 7,2:1 sobre el fondo de día;
  de noche, 12,8:1 sobre el lienzo, 12:1 sobre la tarjeta y 10:1 sobre el gris de sección. Sin
  opacidades: ningún texto lleva `/60` ni `/70`.
- Humo (la pega): `#b4410f` da 5,7:1 sobre la tarjeta, 5,2:1 sobre el gris de sección y 4,9:1
  sobre humo suave. De noche `#fdba74`: 10,5:1 sobre la tarjeta, 11,2:1 sobre el lienzo, 8,7:1
  sobre el gris de sección y 8,9:1 sobre humo suave.
- Enlaces (`enlace`): `#4d7c0f` da 5:1 sobre la tarjeta, 4,8:1 sobre el fondo y 4,5:1 sobre el gris
  de sección; de noche el lima da 11,8:1 sobre la tarjeta y 12,5:1 sobre el lienzo.
- Menú (`#111827` en los dos modos): 16:1 la etiqueta y 11,7:1 la descripción; en la sección
  activa (#1f2937), 9,7:1 el lima y 13:1 la descripción.
- Bordes de 2 px: 17:1 de día y 12,8:1 de noche. La sombra de noche (`#6b7280`) da 3,9:1 sobre el
  lienzo y 3,7:1 sobre la tarjeta, para que se siga viendo.
- El borde fino de la tarjeta de recordatorio (1,2:1) es decorativo: las tarjetas se separan además
  con 8 px de aire y el foco no depende de él. Activo da 4,3:1 de noche y 3,4:1 de día.

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
  dura**. Es el favicon, la cabecera del menú y la presentación de Inicio en el móvil.
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
| Inicio                | La taza del café de la mañana                               |
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

`insignia` (styles.css): bloque plano sin borde, 24 px de alto como mínimo, 11 px en MAYÚSCULAS y
negrita, icono + texto. Nunca más ancha que su sitio: en una tarjeta estrecha (1366 px con todo
desplegado) el texto del encaje (`insignia-texto`) baja de línea en vez de cortarse. El color codifica el significado:

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
  lateral en 1366, 1440 y 1600 px. Las tiras siguen siendo zonas donde soltar: al arrastrar encima,
  la tira se marca (borde de 3 px y lima suave) y el hueco es una ranura discontinua de 40 px al pie,
  sin la tarjeta dentro. No se despliega a medio arrastre: el CDK mide las columnas al empezar.
- La lista de cada columna deja 20 px abajo y 12 px a la derecha para la sombra de la última
  tarjeta.
- **Carril sin barra de desplazamiento** (`scrollbar-oculta`). Cuando no caben todas las columnas,
  en el lado con columnas ocultas hay un degradado de 64 px del lienzo a transparente (la excepción
  a la regla 3) y una **flecha lateral**: 48 px, superficie de tarjeta, borde de 2 px del trazo y
  sombra dura de 3 px, **sin lima** (receta en «Recetas»). Es un control flotante sobre las
  columnas, así que lleva sombra: una excepción a «secundario sin sombra». Va centrada en el
  tablero y, si este es más alto que la ventana, se pega a su borde (`sticky`).
- Cada flecha **avanza una columna** y la deja a 64 px del borde (`scroll-px-16`), fuera del
  degradado; el foco del teclado que entra en una columna oculta la trae entera con ese mismo
  margen. Si una flecha desaparece al llegar al final, el foco pasa a la otra. Al arrastrar una
  tarjeta se apagan y dejan pasar el puntero (manda el desplazamiento automático del CDK). La rueda
  con Mayús y el trackpad siguen moviendo el tablero. **En el móvil no hay flechas**: se desliza con
  el dedo.
- **Filtros del móvil**: el activo no copia el botón principal. Va en gris más oscuro, con una raya
  de tinta abajo, negrita y una marca de visto.

### Menú

- Menú lateral y barra inferior de tinta (`#111827`) en los dos modos, sin transparencias. De
  noche es el tono de las tarjetas y no el del lienzo (`#0b111e`): con `#0b1120` casi no se
  distinguía del fondo. Lo separa además su borde de 2 px.
- La barra del navegador en el móvil (`<meta name="theme-color">` de `web/src/index.html`) toma el
  color del lienzo: `#f9fafb` de día y `#0b111e` de noche, con un `meta` por modo
  (`media="(prefers-color-scheme: …)"`).
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
| Lectura                | Ficha, Perfil, Importación, Estadísticas, Ajustes | Izquierda                                                           | `max-w-5xl` (Ficha), `max-w-3xl` (las demás)           |
| Formulario suelto      | Apuntar                    | **Centrado**                                                               | `max-w-2xl`                                            |
| Estado vacío de página | Inicio (sin datos)         | Título a la izquierda; el estado vacío, **centrado** en el área de trabajo | `max-w-xl`                                             |
| Lista de tareas        | Inicio (con datos)         | Izquierda; desde `lg`, con un panel pegajoso a su derecha                  | `max-w-3xl` la lista; 320 px el panel (380 desde `xl`) |

Así, al cambiar de sección con el menú, el título no salta de sitio. Lo que va centrado es lo que
se lee como una sola pieza: un formulario corto o un mensaje.

### Más

Rejilla de tarjetas-enlace (2 columnas desde `sm`, 1 en el móvil), una por pantalla que existe hoy:
«Perfil y filtros», «Estadísticas», «Importar del registro» y «Ajustes». Cada tarjeta es el
componente `tarjeta-enlace` (el icono, un svg de 24×24, va por `ng-content`) y es entera el enlace,
con icono en bloque gris, título con flecha y una línea de descripción; borde de 2 px, sombra dura
de 4 px que crece al pasar por encima y desaparece al pulsarla (se hunde), foco de 3 px y al menos
96 px de alto. Debajo, una línea en monoespaciada: «Los recordatorios y lo del día están en Inicio.»

### Estadísticas y Ajustes

Páginas de lectura de `max-w-3xl`, con «Volver a Más» y **sin relleno lima**: no tienen acción
principal. Cada bloque es una tarjeta de borde fino (`border-borde-fino`, sin sombra dura) con su
`h2`.

- **Estadísticas:** «Tus candidaturas» (total y barras por estado), «Enviadas por semana» (8
  semanas), «Respuestas» (en frases: «1 de 4 (25 %) han llegado a entrevista u oferta.» y «De media
  tardan 9 días en responder (calculado con 3).») y «Por qué se descartan». Las barras son el
  componente `lista-barras`: una lista en la que cada fila dice su etiqueta y su cifra en
  monoespaciada, y la barra (borde de 2 px, proporcional a la mayor, `aria-hidden`) solo acompaña.
  Por estado, cada barra lleva el color de su estado; por semana, el de Enviada; los motivos, gris.
  Una sección sin datos dice «Todavía no hay datos para esto.»; sin ninguna candidatura, la página
  enseña un estado vacío con «Ir al tablero» (con borde). Al final, la letra pequeña en
  monoespaciada dice que solo cuenta lo que está en la app y qué no entra en cada cifra.
- **Ajustes:** un bloque «Esta app» con el modo, la versión y los agentes (`dl` a dos columnas) y
  una línea en monoespaciada con lo que llegará (envío manual o automático y tope de gasto). Sin
  botones.

### Botones

| Tipo                | Aspecto                                                                                                       |
| ------------------- | ------------------------------------------------------------------------------------------------------------- |
| Principal (relleno) | `#111827` de día, lima de noche; sombra dura de 3 px; al pulsarlo se hunde (desplaza 3 px y pierde la sombra) |
| Secundario (borde)  | Borde de 2 px del color del trazo y texto de tinta (`on-surface`), sin sombra. De noche, gris claro: nada de lima |
| De texto            | Plano, en el color de los enlaces                                                                             |
| «Marcar enviada»    | El momento cumbre: lima también de día, borde de 3 px, sombra de 5 px y avión de papel                        |

De noche el primario de Material es el lima, y los botones de borde lo tomaban para el texto: todos
parecían de acento y el «Hecho» con `boton-acento` no se distinguía. Desde el 14/11/2026 el texto y
el velo de los de borde salen de `on-surface` (`material-theme.scss`): de día es el mismo `#111827`
de siempre y de noche, tinta clara. Solo `boton-acento` (lima) y `boton-peligro-borde` (rojo) llevan
color. Afecta a todas las pantallas: en la ficha, «Marcar enviada» con borde, «Buscar el CV» y
«Añadir recordatorio» pasan de lima a tinta clara de noche.

**Un solo relleno lima por pantalla.** De noche todos los principales son lima, así que en cada
pantalla solo uno va relleno: en la ficha, el cambio de estado («Buscar el CV» y «Añadir nota» van
con borde); en el tablero vacío, el «Nueva candidatura» del estado vacío (el de la cabecera se
oculta); en la bandeja de rastreos, el «Pasar al tablero» de la primera oferta pendiente; en Inicio,
«Nuevo recordatorio» (ver «Inicio y recordatorios»).

### Avisos

Snackbar plano, como una tarjeta: fondo de tarjeta, texto de tinta, borde de 2 px y sombra dura de
4 px. La acción va en negrita y en el color de los botones de texto: `#4d7c0f` de día y lima de
noche. Los de error, en el rol `error`. Tras cualquier cambio de estado el aviso ofrece
**«Deshacer»** durante 8 s (también con Ctrl+Z si el foco no está en un campo de texto o un
desplegable y no hay un diálogo abierto) y, si no se ha comentado, «Añadir nota»; al pasar a
Entrevista, en su lugar, «Apuntar la entrevista», que abre en la ficha el diálogo de recordatorio con
ese tipo. Deshecho el cambio,
el foco vuelve a la tarjeta en el tablero o al bloque «Cambiar estado» en la ficha.

### Estados vacíos

| Dónde                      | Ilustración                                                           | Texto principal                                    | Microcopy                                                                                     | Acciones                                                 |
| -------------------------- | --------------------------------------------------------------------- | -------------------------------------------------- | --------------------------------------------------------------------------------------------- | -------------------------------------------------------- |
| Inicio (sin nada apuntado) | Taza de café en un bloque de lima, centrada en la pantalla, sin marco | «Nada pendiente para hoy.» (Bricolage 800, 30 px)  | «De momento, café tranquilo.»                                                                 | «Buscar nuevas ofertas» (→ Rastreos) e «Ir al tablero»  |
| «Para hoy» sin nada        | — (aviso suave, ver «Hoy y recordatorios»)                            | «Todo despejado por hoy.»                          | «Tu próximo seguimiento es el mié 18 nov (5 tareas).», si hay algo en los próximos 7 días     | —                                                        |
| «Próximos 7 días» sin nada | —                                                                     | «Nada en los próximos 7 días.» (monoespaciada)     | —                                                                                             | —                                                        |
| Tablero sin candidaturas   | Página de clasificados con el hueco del anuncio                       | «Todavía no hay candidaturas.»                     | —                                                                                             | «Nueva candidatura»                                      |
| Columna vacía              | Ficha discontinua con chincheta del color del estado                  | «Sin candidaturas»                                 | Una frase por estado; Entrevista: «Nadie ha respondido todavía. Los procesos van a su ritmo.» | —                                                        |
| Rastreos sin rastreos      | El radar en un bloque de tinta                                        | «Todavía no hay rastreos.»                         | «El radar está encendido. Solo falta que pase algo por delante.»                              | —                                                        |
| Bandeja de rastreos a cero | Bandeja en un bloque de lima                                          | «Ya has mirado todas las ofertas de este rastreo.» | «Bandeja a cero. Hasta el próximo rastreo.»                                                   | Ir a otro rastreo, si tiene ofertas                      |

### Inicio y recordatorios

**Inicio** (hasta el 14/11/2026 se llamaba «Hoy»; decisión de Eduardo) responde a «¿qué tengo que
hacer hoy?». La ruta es `/inicio`, y `/` y los enlaces viejos a `/hoy` (con sus `?tipo=`, `?dia=` y
`?ver=`) llevan a ella. El nombre nuevo es el de la pantalla: «Para hoy», «Hechos hoy», «Hoy · 09:00»
y el atajo «Hoy» del diálogo hablan del día y no cambian. Por dentro (carpetas, clases y
`/api/panel/hoy`) sigue llamándose «hoy». Columna a la izquierda de `max-w-3xl`, con 32 px entre
secciones y 8 px entre tarjetas.

**Dos columnas desde `lg` (1024 px).** Decisión de Eduardo del 13/11/2026: a la derecha de la lista
va un panel pegajoso (`<aside>`, `sticky top-6`) de 320 px (380 px desde `xl`), separado 32 px.
Enseña el **resumen** y, al elegir una tarjeta, el **detalle** de ese recordatorio. Es una sola superficie,
como las tarjetas: fondo de tarjeta, borde fino y sin sombra dura; lo de dentro son cajas internas
grises sin marco. Si no cabe en la ventana, se desplaza por dentro. Por debajo de `lg` hay una sola
columna: el resumen va arriba en una `seccion-plegable` (plegada) y no hay panel de detalle. En el
estado vacío completo (la taza) no sale ni el panel ni el resumen.

**Resumen.** Tres cifras y la semana. Desde el 14/11/2026 no es solo para leer: las cifras llevan
a donde se ve el detalle y los días filtran la lista.

- **Cifras**: «Candidaturas activas» (todas menos descartadas y cerradas), «Entrevistas esta
  semana» (recordatorios de tipo Entrevista **pendientes** de lunes a domingo, también los de un día
  ya pasado sin marcar; una entrevista hecha ya no cuenta) y «Tasa de respuesta»
  (la definición de Estadísticas). Cifra grande en monoespaciada (30 px, negrita) y debajo su rótulo
  (`etiqueta`), en cajas grises: dos arriba y la tasa abajo, a todo el ancho, con una línea que dice
  de dónde sale («2 de 8 enviadas han llegado a entrevista u oferta.»). Sin enviadas no se inventa
  un 0 %: sale una raya «—» (para el lector de pantalla, «Sin datos») y «Todavía no hay candidaturas
  enviadas.».
- **Cifras que se pulsan.** «Candidaturas activas» es un enlace al tablero y «Entrevistas esta
  semana» un botón (`aria-pressed`) que pone los filtros **Entrevistas + esta semana**: la lista
  enseña justo las que cuenta. Otro clic quita los dos. Si esta semana no queda ninguna entrevista
  pendiente, se queda en cifra. Su nombre accesible es «Entrevistas pendientes esta semana: 2. Ver
  solo esas entrevistas». Siguen pareciendo cifras: se pulsa la caja
  entera, que aclara el fondo al pasar el ratón y lleva el anillo de foco, y arriba a la derecha
  hay una flecha gris de 16 px que pasa al color de los enlaces. Con el filtro puesto, la caja
  lleva un borde interior de 2 px de ese color. El nombre accesible lo dice entero: «Candidaturas
  activas: 12. Ir al tablero».
- **Barra de la tasa.** Bajo el porcentaje, una barra de 6 px del color de los enlaces (lima de
  noche, `#4d7c0f` de día) sobre una pista gris (`outline-variant`). Es decorativa (`aria-hidden`):
  el número ya está escrito. Sin enviadas no hay barra.
- **Esta semana**: siete botones de lunes a domingo (L M X J V S D) con el día del mes en
  monoespaciada. **Hoy** lleva el marco de 2 px del color del trazo, fondo de tarjeta y el número en
  negrita; los días con recordatorios pendientes, un punto redondo de 6 px del color de los enlaces.
  **Pulsar un día filtra la lista a ese día** y otro clic en el mismo lo quita: el elegido lleva el
  marco de 2 px del color de los enlaces, el fondo `despejado` y el número en negrita de ese color
  (`aria-pressed`). El nombre de cada uno es el texto entero, «miércoles 18 de noviembre: 2 tareas»
  (y «…, hoy: …» en el de hoy). Nada depende solo del color: el punto está o no está, y el marco
  también.
- **«Lo siguiente: lun 23 nov →».** Si de aquí al domingo ya no queda nada pendiente y lo hay más
  adelante, bajo el calendario sale esa línea (botón de texto) con el día del primero, que da la
  API (`resumen.siguienteDia`). Pulsarla filtra ese día. El lector de pantalla oye el día entero.
- Sin relleno lima: ni las cifras, ni el día elegido, ni la barra son botones rellenos.

**Detalle.** Con el panel a la vista, el título de cada tarjeta es un **botón** (`aria-pressed`,
nombre «Ver detalle: título») en vez del enlace a la ficha, y un clic en cualquier parte de la
tarjeta que no sea un botón hace lo mismo. La tarjeta elegida lleva el borde entero y una barra de
4 px a la izquierda del color de los enlaces (`#4d7c0f` de día, lima de noche), como la sección
activa del menú. El panel enseña entonces:

- «Volver al resumen» (botón de texto), el título como encabezado, la insignia del tipo y la fecha
  con hora en monoespaciada, y las notas del recordatorio.
- Si es de una candidatura: una caja gris con la empresa (rótulo), el puesto y la insignia del
  estado; «Abrir la oferta» (se abre en otra pestaña, con su icono y el aviso para el lector de
  pantalla); las notas de la candidatura; el historial de estados, del más reciente al más antiguo,
  con el punto del color de cada estado; y **«Abrir ficha completa»**, con borde y a todo el ancho.
- Si es suelto: «Editar», con borde.
- **«✓ Hecho»**, con borde, bajo la fecha (si no está hecho ya): lo marca sin salir del panel, con
  el mismo aviso y su «Deshacer»; vuelve el resumen y el foco pasa a la tarjeta vecina. Si se
  deshace (con el aviso o con Ctrl+Z), en cuanto llega el panel nuevo vuelve a abrirse su detalle.
- **«Añadir recordatorio»**, con borde y a todo el ancho, encima de «Abrir ficha completa», si la
  candidatura sigue activa: abre el diálogo para esa candidatura sin pasar por su ficha.
- La ficha de la candidatura se pide al abrir el detalle: barra de progreso en un hueco fijo y, si
  falla, el error con `role="alert"` y «Reintentar» dentro del panel.

Esc o «Volver al resumen» lo cierran, y también volver a pulsar el título. Al abrirlo el foco va a
su encabezado y, al cerrarlo, a la tarjeta. La elegida va en la URL (`/inicio?ver=12`) y aguanta una
recarga. Si el recordatorio deja de estar a la vista (hecho, pospuesto o borrado), vuelve el
resumen. En el móvil el título sigue siendo el enlace a la ficha.

**Secciones.** «Para hoy · N» y «Próximos 7 días · N» son `h2` con el total en monoespaciada. Lo
secundario («Enviadas sin respuesta», «Más adelante», «Hechos hoy») va plegado en `seccion-plegable`
con el aspecto suave, y no se pinta si está vacío. Para que salgan en la lista de encabezados del
lector de pantalla, cada plegable lleva justo antes un `h2` oculto (`sr-only`) con su título: un
`role="heading"` dentro del `<summary>` no lo respetan todos los lectores, porque el summary ya es un
botón.

**«Próximos 7 días», por día.** Las tarjetas van agrupadas bajo un encabezado (`h3`) por día:
«Miércoles, 18 nov · 5 tareas» (con una, «1 tarea»), el día en negrita de 14 px y el total en
monoespaciada gris. Dentro, cada tarjeta dice solo la hora. 20 px entre grupos y 8 px entre
tarjetas. «Para hoy» no se agrupa: lo de hoy dice «Hoy · 09:00» y lo vencido, «Vencido · mar 3 nov».

**«Tareas».** Todos los contadores a la vista usan la misma palabra: los encabezados de día, el
aviso «Todo despejado» («… (5 tareas)»), los días del calendario («2 tareas», «sin tareas») y los
chips para el lector de pantalla. «Recordatorio» queda para el objeto («Nuevo recordatorio»).

**Filtros.** Encima de las listas, una fila de chips de un solo uso: «Todas» y, solo si tienen algo
pendiente, «Seguimientos», «Entrevistas», «Plazos», «LinkedIn» y «Otros», cada uno con su número
en monoespaciada.

- Filtran «Para hoy», «Próximos 7 días» y «Más adelante» (y «Hechos hoy», para que no desentone).
  «Enviadas sin respuesta» no son recordatorios de ningún tipo: con cualquier filtro puesto, no
  salen.
- Chip: 36 px de alto con 48 px de zona de toque (`::after`), esquinas de 2 px, fondo gris de caja
  interna. **El elegido no va relleno**: borde de 2 px y texto del color de los enlaces sobre fondo
  de tarjeta (`aria-pressed`). Es un `role="group"` con nombre («Filtrar las tareas por tipo») y
  se maneja con Tab y Espacio o Intro, como cualquier botón.
- El filtro va en la URL (`/inicio?tipo=Entrevista`, sin mirar mayúsculas) y aguanta una recarga.
  Pulsar «Todas» o el chip elegido lo quita. Si se hace lo último de un tipo, su chip desaparece y
  el filtro vuelve solo a «Todas».
- **Por día** (`/inicio?dia=2026-11-18`), desde el calendario del resumen o «Lo siguiente». Bajo los
  chips sale el aviso «Mostrando el **mié 18 nov** · Quitar filtro» («Quitar filtro» es un botón de
  texto y devuelve el foco a los chips). El lector de pantalla lo oye por un `role="status"` oculto
  que está siempre en la página y solo cambia de texto («Mostrando el miércoles 18 de noviembre»):
  una región que aparece de golpe no siempre se anuncia.
- **Por semana** (`/inicio?semana=esta`), solo desde la cifra de las entrevistas: de lunes a domingo
  de esta semana, con el aviso «Mostrando **esta semana** · Quitar filtro». Día y semana se
  excluyen: elegir uno quita el otro, y «Quitar filtro» quita los dos. Afecta a todas las secciones, y
  «Enviadas sin respuesta» no sale, porque no es de ningún día. Los números de los chips pasan a
  ser los de ese día.
- Los filtros se suman. Los que se corrigen solos (un tipo sin nada, un `?ver=` que ya no está)
  esperan al panel que se pide al entrar: el que quedó de la visita anterior puede no tener lo que
  se apuntó después. Con un filtro puesto, las secciones que se quedan vacías no se pintan
  (tampoco el aviso «Todo despejado») y las plegadas que tengan algo salen abiertas. Si no queda
  nada: «Nada para ese día.» (o «Nada de «Entrevistas» para ese día.», o «… esta semana.») en
  monoespaciada gris, con
  «Quitar filtro» con borde.
- Sin barra de búsqueda: con estos volúmenes sobra.

**Tarjeta de recordatorio** (`app-tarjeta-recordatorio`, la misma en Inicio y en la ficha). Decisión de
Eduardo del 13/11/2026, que manda sobre la regla del marco:

- **Una sola superficie.** Fondo de tarjeta, borde de 1 px casi transparente (`borde-fino`) y sin
  sombra dura. De noche la tarjeta (`#111827`) es algo más clara que el lienzo (`#0b111e`); de día,
  blanca sobre `#f9fafb`. Al pasar el ratón o con el foco del teclado dentro, el borde pasa a lima
  suave (`borde-fino-activo`).
- **Compacta, en dos bloques.** A la izquierda, tres líneas: (1) la insignia del tipo y la fecha
  («mié 18 nov · 09:00») en monoespaciada y gris de lápiz; (2) el título en negrita de 16 px; (3) la
  empresa como rótulo y la insignia del estado, si es de una candidatura. Las notas, si las hay, van
  debajo en gris. A la derecha, centradas en vertical, las acciones: «✓ Hecho», «Posponer ▾» en lo
  de hoy y en lo de los próximos 7 días, el icono de la oferta si la hay y «⋮». Mide unos 78 px de
  alto la suelta y 106 px la de una candidatura (antes, 127 y 165).
- **Sin redundancia.** En Inicio, si el título guardado de un recordatorio de candidatura es el que se
  pone solo («Seguimiento: puesto en empresa», «Entrevista en empresa», «Plazo de empresa» o el
  antiguo «Preguntar cómo va empresa»), la tarjeta enseña **solo el puesto**: el tipo ya lo dice la
  insignia y la empresa va en la línea de debajo. Un título escrito a mano («Plazo de la prueba
  técnica») sale tal cual. Lo guardado no se toca, y el título entero sigue en los `aria-label`
  («Hecho: Entrevista en Consultora Ejemplo, Desarrollador .NET»), en el panel de detalle, en los
  avisos y en la ficha, donde no hay línea de empresa.
- **Abrir la oferta.** Si la candidatura tiene URL, un icono discreto (la flecha que sale de la
  caja, gris de lápiz; del color de los enlaces al pasar el ratón o con el foco) junto a «⋮». Se
  abre en otra pestaña (`rel="noopener noreferrer"`), se llama «Abrir la oferta de {puesto} en otra
  pestaña» y su zona de toque es de 48 px. No elige la tarjeta.
- **Estrecha.** La tarjeta mide su propio ancho (`@container`): con menos de 32 rem (el móvil y la
  columna de la ficha) las acciones bajan debajo, con los iconos al final de la fila. Ahí los
  botones llevan 12 px de relleno lateral (16 px en la ancha), 6 px entre ellos y los iconos se
  pegan al borde (`-mr-2`): «Hecho», «Posponer», la oferta y «⋮» caben en una fila a 360 px.
- **Acciones en columna.** En la tarjeta ancha de Inicio, la que no tiene oferta guarda su hueco
  (40 px, lo que mide el icono): «Hecho» y «Posponer» quedan alineados en toda la lista.
- **Título sin subrayar.** Tinta en reposo; al pasar el ratón o con el foco, el color de los enlaces
  (`#4d7c0f` de día, lima de noche: el lima no se lee sobre blanco). Si es de una candidatura es el
  enlace a su ficha: cursor de mano, cambio de color y, con el teclado, un anillo de 3 px del color
  primario separado 2 px. Su zona de toque es de 48 px gracias a un `::after`, sin engordar la
  tarjeta.
- **Insignias de tipo**, en gris de insignia, con icono de trazo editorial de 20×20 y puntas
  cuadradas, y siempre con texto: sobre de vuelta (Seguimiento), dos bocadillos (Entrevista),
  megáfono (Publicación en LinkedIn, sin el logotipo ajeno), reloj de arena (Plazo) y chincheta
  (Otro).
- **«Vencido · mar 3 nov»** es una insignia en humo sobre humo suave (4,9:1 de día y 8,9:1 de
  noche) y sustituye al día: al lado queda solo la hora. Lo de hoy dice «Hoy · 09:00».
- **Hecho.** Insignia gris «Hecho», título tachado y en gris de lápiz, y el botón pasa a
  «Pendiente otra vez».
- **«Se cerró solo al pasar a Entrevista»** (u Oferta): en «Hechos hoy», el seguimiento que se dio
  por hecho al mover la candidatura lleva esa línea en monoespaciada de 12 px y gris de lápiz, para
  que no parezca hecho a mano. Lo dice la API (`cerradoAlPasarA`, sacado del historial). Es la
  opción por defecto mientras Eduardo no diga otra cosa: quitarla es borrar ese bloque de la
  plantilla.
- **Botones** de 48 px de alto. «Hecho» lleva una marca de visto delante; «Posponer» y «⋮» abren un
  menú y llevan el título en el `aria-label` («Más acciones: Entrevista en…»).
- **Posponer.** Lo vencido y lo de hoy se cuentan desde hoy: «Mañana», «Dentro de 3 días» y «Dentro
  de una semana», cada una con su fecha al lado. **Si mañana es sábado o domingo, la primera es «El
  lunes»** (y no se repite un día: un viernes quedan «El lunes» y «Dentro de una semana»). Lo de
  los próximos 7 días se cuenta desde su propio día, para no adelantarlo: «Un día después», «3 días
  después» y «Una semana después» (o «El lunes»). Siempre al final, «Cambiar fecha u hora…».

**Un solo relleno: «Nuevo recordatorio».** Decisión de Eduardo del 14/11/2026. El único botón
relleno de Inicio es **«+ Nuevo recordatorio»**, en la cabecera (tinta de día, lima con texto oscuro
de noche): es la llamada principal de la pantalla, también en el estado vacío, donde «Buscar nuevas
ofertas» e «Ir al tablero» van con borde.

- **«✓ Hecho» de «Para hoy»**: ya no va relleno. Lleva el contorno con acento (`boton-acento`):
  borde de 2 px y texto del color de los enlaces (lima de noche, `#4d7c0f` de día). Todos los de
  «Para hoy», no solo el primero.
- **«Hecho» de lo demás** (próximos, más adelante, la ficha y el panel de detalle): el contorno
  normal, del color del trazo.
- En el panel de detalle todo va con borde o de texto. En la ficha manda el cambio de estado.

**«Para hoy» sin nada: aviso «Todo despejado».** No es una tarjeta: fondo de lima casi transparente
(`despejado`), raya de 3 px a la izquierda (lima de noche, `#4d7c0f` de día) y texto normal de
14 px. Dice «Todo despejado por hoy.» y, si hay algo en los próximos 7 días, «Tu próximo seguimiento
es el **mié 18 nov** (5 tareas).», con el día del primero y las tareas de **ese día** (no las de
toda la sección). Si lo primero
no es un seguimiento dice «Lo próximo es el…», para no llamar seguimiento a una entrevista.

**Enviadas sin respuesta.** El total del título las cuenta todas, también las de más de un mes:
«Enviadas sin respuesta · 11» con dos filas y «Y 9 más llevan más de un mes sin respuesta.». Si solo
hay antiguas: «Enviadas sin respuesta · 9» y «Las 9 llevan más de un mes sin respuesta.» (con una,
«Lleva más de un mes sin respuesta.»). Nunca «· 0» con enviadas esperando. Cada fila es un enlace
de 48 px a la ficha, sin subrayar, con el mismo cambio de color que los títulos.

**Diálogo de recordatorio.** El de siempre (borde de 2 px y sombra dura). «Para: puesto · empresa»
es una caja interna gris. Día y hora, en dos columnas desde `sm`.

- **Tipo.** Botones de radio a la vista (`radiogroup`), en **dos columnas también en el móvil** y
  filas de 48 px: en una sola columna, la hora quedaba bajo el pliegue a 390×844.
- **Tipo inicial.** En Inicio, «Publicación en LinkedIn». Desde una candidatura (la ficha o el panel
  de detalle), según su estado: «Entrevista» en Entrevista, «Plazo» en Oferta y «Seguimiento» en
  lo demás.
- **Título.** Viene puesto según el tipo y lo sigue mientras no se escriba otro: «Publicar en
  LinkedIn», «Entrevista en {empresa}», «Plazo de {empresa}» y «Seguimiento: {puesto} en
  {empresa}», el mismo que pone la API al programar el seguimiento sola (antes, «Preguntar cómo va
  {empresa}»).
- **Atajos de día.** Sobre el campo «Día», tres botones de texto de 48 px en un grupo («Poner el
  día»): «Hoy», «Mañana» y «En una semana». Ponen el día sin abrir el calendario y el elegido
  queda marcado (`aria-pressed`). Bajo el campo se lee en qué cae («Martes 17 de noviembre.») y,
  si ya ha pasado, «…: ya ha pasado, saldrá como vencido.».
- **Hora.** Opcional y a las 09:00 por defecto («Hora (opcional)», «Si la dejas vacía, a las
  09:00.»), **salvo en una entrevista, que la exige** (decisión de Eduardo del 14/11/2026): el
  campo pasa a «Hora», empieza vacío, la ayuda dice «Obligatoria: es una entrevista.» y, sin ella,
  no guarda y sale «Pon la hora de la entrevista.» en el campo. Al cambiar el tipo a Entrevista,
  las 09:00 que se pusieron solas se vacían (y vuelven al pasar a otro tipo); la hora que ha
  escrito el usuario se respeta. Una entrevista ya guardada conserva la suya, aunque sean las
  09:00. La API lo valida igual (400 en `hora`).
- **Hora que no existe.** La noche de marzo en que el reloj salta de las 02:00 a las 03:00, de
  02:00 a 02:59 no hay hora: el campo avisa antes de guardar, «Esa hora no existe ese día: el reloj
  se adelanta de 02:00 a 03:00.» (también si se elige el día después de la hora), y la API da 400
  en `hora` con el mismo texto. Al posponer no se pregunta: pasa a las 03:00.

**Carga y error.** Barra de progreso en un hueco fijo bajo la cabecera, y el error en el rol
`error` con `role="alert"` y «Reintentar», como en la ficha.

### El momento cumbre: pasar a «Enviada»

1. El botón «Marcar enviada» (ver Botones). Se hunde al pulsarlo.
2. Cae el **sello «ENVIADA»** con la fecha sobre la cabecera de la ficha: doble borde de 4 px del
   color de Enviada, girado −6°, entra grande y rebota en 0,5 s. Al lado: «La pelota está en su
   tejado.».
3. El aviso de abajo (ver Avisos) dice que se ha programado el seguimiento a 7 días y ofrece
   «Deshacer» por si se ha pulsado sin querer.

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
| `web/src/index.html`              | `theme-color` (la barra del navegador en el móvil): el lienzo, `#f9fafb` de día y `#0b111e` de noche                                                     |

### Material

```scss
@include mat.theme-overrides(
    (
        primary: light-dark(#111827, #a3e635),
        on-primary: light-dark(#f3f4f6, #111827),
        surface: light-dark(#f9fafb, #0b111e),
        surface-container-lowest: light-dark(#ffffff, #111827),
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
        outlined-label-text-color: var(--mat-sys-on-surface),
        outlined-state-layer-color: var(--mat-sys-on-surface),
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
    --color-borde-fino: var(--re-borde-fino); /* #111827 al 10 % / blanco al 8 % */
    --color-borde-fino-activo: var(--re-borde-fino-activo); /* #4d7c0f al 80 % / lima al 55 % */
    --color-enlace: var(--re-enlace); /* light-dark(#4d7c0f, #a3e635) */
    --color-despejado: var(--re-despejado); /* #4d7c0f al 6 % / lima al 5 % */
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
| Flecha lateral del tablero                   | `rounded-md border-2 border-trazo bg-surface-container-lowest shadow-dura-sm`; foco `ring-3 ring-primary ring-offset-2` (sin lima)                               |
| Tarjeta de recordatorio                      | `rounded-md border border-borde-fino bg-surface-container-lowest hover:border-borde-fino-activo has-focus-visible:border-borde-fino-activo` (sin sombra)       |
| Tarjeta de recordatorio elegida              | `data-seleccionada:border-enlace! data-seleccionada:shadow-[inset_4px_0_0_0_var(--re-enlace)]` (con `data-seleccionada` en la tarjeta)                          |
| Panel lateral de Inicio                      | `sticky top-6 rounded-2xl border border-borde-fino bg-surface-container-lowest p-5` (sin sombra)                                                                |
| Cifra del resumen                            | Caja `rounded-md bg-surface-container px-3 py-3`; cifra `font-mono text-3xl leading-none font-bold`; debajo, `etiqueta text-on-surface-variant`                 |
| Día del mini calendario                      | `rounded-md border-2 border-transparent bg-surface-container`; hoy, `data-hoy:border-trazo data-hoy:bg-surface-container-lowest`; marca `rounded-full bg-enlace` |
| Título-enlace                                | `font-semibold text-on-surface hover:text-enlace focus-visible:text-enlace` y anillo de foco de 3 px (sin subrayado)                                            |
| Aviso «Todo despejado»                       | `rounded-r-sm border-l-[3px] border-enlace bg-despejado px-4 py-3 text-sm`                                                                                      |
| Rótulo                                       | `etiqueta text-on-surface-variant`                                                                                                                              |
| Pega                                         | `text-humo` + banderita + «Pega:» en negrita                                                                                                                    |
| Botón principal / secundario                 | `mat-flat-button` / `mat-stroked-button`                                                                                                                        |
| «Marcar enviada»                             | `mat-flat-button` + `boton-enviar`                                                                                                                              |
| Sello                                        | `-rotate-6 animate-sello motion-reduce:animate-none`                                                                                                            |
| Datos                                        | `font-mono` (fechas, días, contadores)                                                                                                                          |

**No**: marcos dentro de marcos; lima como fondo de secciones o como color de texto sobre claro;
transparencias en el texto; sombras difuminadas; colores sueltos de Tailwind (`slate-*`, `blue-*`):
siempre tokens, para que funcione el modo oscuro.
