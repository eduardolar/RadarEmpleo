# RadarEmpleo: ampliación «Preparar y enviar» (F4 en adelante)

Plan del arquitecto del 06/11/2026, **aprobado por Eduardo el 06/11/2026 («adelante»: sí a todas las recomendaciones)**. Sustituye a las fases F4-F8 del apartado 8 de `PLAN.md`; el resto de `PLAN.md` sigue valiendo. Pendiente de decidir más adelante (antes de la F11): si la «casilla de privacidad imprescindible de la candidatura» es permiso permanente.

## 1. Resumen

Dos acciones nuevas desde la ficha:
- **«Preparar CV y carta»**: lanza `redactor-cv` y `revisor-candidatura` (hasta 2 vueltas de corrección). Los archivos van a `Curriculum\Candidaturas\Semana …\` como siempre; la app los muestra y los vincula como Documento.
- **«Enviar candidatura»**: por **correo** con código C# (Microsoft Graph + su Outlook.com), sin IA; por **portal** con un agente que usa su Chrome en dos pasos: «rellenar y pararse antes del último clic» y «confirmar».
- **Manual** (por defecto): entre los dos pasos está el OK de Eduardo. **Automático**: el OK lo sustituyen reglas estrictas, solo en canales permitidos.

Antes de programar, **F4 = 1 día de pruebas de viabilidad**: no se sabe sin probar si Claude in Chrome funciona desde `claude -p` sin nadie delante ni si deja dar el último clic de «enviar» en una sesión sin persona (en el chat lo bloquea si Eduardo no lo ordena). **Nunca se intenta saltar ese bloqueo ni se finge una confirmación.**

## 2. Fases

| Fase | Qué | Días | Cómo comprobarlo |
|---|---|---|---|
| **F4** | **Pruebas de viabilidad** (scripts en el scratchpad, sin tocar la app ni datos reales): (1) `claude -p --chrome` usa su Chrome en una pestaña sin quitarle el foco; (2) rellenar un formulario de PRUEBA local y ver si deja pulsar «Enviar» sin persona, con y sin la frase «confirmado por Eduardo en RadarEmpleo»; (3) `--resume <sesión>` recupera la misma pestaña; (4) `claude -p --agent redactor-cv` genera un CV real en una carpeta temporal (tiempo y coste); (5) el Word de PowerShell no cierra ni bloquea un Word abierto de Eduardo; (6) registrar una app en Microsoft Entra con su hotmail y mandarse un correo con PDF por Graph (lo hace Eduardo); (7) reglas de permiso por ruta (`Write(...)`/`Edit(...)`) impiden escribir en `ofertas-enviadas.md` | 1 | Informe «funciona / no funciona / coste» de los 7 puntos. Si falla (1) o (3): plan B de portales (el paso B vuelve a rellenar y compara). Si falla (2): el envío por portal acaba siempre con el clic de Eduardo y el Automático queda solo para el correo. Si falla (6): correo por Chrome en outlook.live.com |
| F5 | Documentos, perfil y bandeja de rastreos (antigua F4) + `Documento` ampliado (Origen, Vigente, Hash, PreparacionId), visor de PDF en la ficha, función pura `CarpetaSemana`, limpieza de `.tmp` huérfanos en `radar\` | 2 | Abrir en la app el PDF del CV de Marco Talento; pasar una oferta de ejemplo a Detectada; `CarpetaSemana(06/11/2026)` = «Semana 2026-11-02 (2 - 8 nov)» y `CarpetaSemana(29/10/2026)` = «Semana 2026-10-26 (26 oct - 1 nov)» |
| F6 | Recordatorios, Hoy y estadísticas (antigua F5) + página Ajustes vacía + hueco «Esperan tu OK» en Hoy | 1,5 | 3/11 y 17/11 de LinkedIn; un seguimiento real en Hoy; Ajustes desde «Más» |
| F7 | Agentes base (antigua F6) + `EjecucionAgente` enlazada a preparación/envío, bloqueo compartido de Chrome (tarea de los lunes y `/empleo`), gasto del día, hash del registro antes y después de cada ejecución | 2,5 | Extraer registro propone ~45-50; Analizar encaje rellena la pega; 403 por el 5181; hash de `ofertas-enviadas.md` igual tras cada ejecución |
| **F8** | **Preparar candidatura** | 2,5 | En una candidatura real: en 10-25 min, CV y carta en PDF de 1 página en la carpeta de la semana, vinculados, con veredicto del revisor; pasa a Preparada |
| **F9** | **Enviar por correo** (Manual) | 1 | Con su permiso, correo de prueba a sí mismo con el CV; aparece en Enviados; candidatura de prueba Enviada con seguimiento +7 y línea en `radar\envios.md` |
| **F10** | **Enviar por portal** (Manual): agente `enviador-candidaturas`; LinkedIn solicitud sencilla, InfoJobs, webs sin cuenta (Viterbit, Bizneo, SmartRecruiters, Teamtailor); Workday con «inicia tú sesión y pulsa Seguir» | 2 | Una real por InfoJobs o LinkedIn: resumen y parada; «Enviar ahora» → texto de confirmación del portal en la ficha. Una con pregunta nueva → «Necesita tu ayuda» sin enviar |
| F11 | Modo Automático con salvaguardas | 1,5 | Tests de `ReglasEnvioAutomatico`; en vivo con correo desbloqueado: preparar → se envía sola → aviso por correo y en Hoy; «Pausar» detiene la siguiente |
| F12 | Móvil (antigua F7) + vista de solo lectura de «Esperan tu OK» y envíos | 1,5 | Con datos móviles: tablero, nota que aparece en el PC, envío pendiente con «Confírmalo en el PC» |
| F13 | Demo y portfolio (antigua F8) + preparaciones y envíos de ejemplo, envío y correo apagados en Demo | 1,5 | URL pública con datos inventados; CI en verde; script sin datos personales |

Total ~17 días. Si hay que recortar: F11, estadísticas por motivo e importación de LinkedIn.

Pendientes del apartado 11 de `PLAN.md`: `.tmp` huérfanos y Curriculum vacía en tests → F5; seguimiento al pasar a Entrevista/Oferta → F6; endpoint de agentes por nombre, Origen en el servidor, «Aplicar» revalidado con hash → F7; `Version` + `PATCH` → F12; Scalar en Demo y datos personales del historial → F13.

## 3. Preparar candidatura (F8)

**Flujo:** botón destacado «Preparar CV y carta» (en Detectada, solo PC) → diálogo con canal deducido del enlace (`ReglasCanal`, desde `portal-desde-enlace.ts`; con Correo se pide la dirección) y «Con carta: Sí/No» → `OrquestadorPreparacion` encola:

| Paso | Qué | Entrada (la monta la app) | Salida (`--json-schema`) |
|---|---|---|---|
| 0 | `--agent analista-ofertas` si falta el encaje | enlace, puesto, empresa | encaje, pega, CV de partida, qué destacar, puentes |
| 1 | `--agent redactor-cv` | oferta y análisis, conCarta, canal, carpeta de la semana calculada por la app, nombre de empresa limpio, «no toques ofertas-enviadas.md ni perfil-busqueda.md», y asunto/cuerpo si es Correo | rutas .docx/.pdf, CV de partida, páginas, cambios, puentes, dudas, asunto/cuerpo |
| 2 | `--agent revisor-candidatura` | rutas, oferta, análisis, correo | veredicto LISTA/CORREGIR, fallos[], sugerencias[] |
| 3-4 | redactor con fallos[] + revisor (máx. 2 vueltas más) | | |

LISTA → preparación «Lista», Documentos CV/Carta (Vigente, Hash SHA-256), candidatura a Preparada. CORREGIR tras 2 vueltas → «Con fallos» con «Pedir que lo corrija otra vez» o «Usarla igualmente» (solo Manual, marca `AceptadaPorEduardo`).

**Archivos:** en `Curriculum\Candidaturas\Semana …\` con los nombres de siempre (`CV_Eduardo_Lara_Navarro_<Stack>_<Empresa>`, `Carta_Presentacion_…`, `Correo_<Empresa>.txt`); si existe, `_2`, nunca sobrescribir otra candidatura. Regla 6 nueva del híbrido: los agentes lanzados por la app solo escriben en la carpeta de la semana (permisos por ruta + validar rutas devueltas + hash del registro y del perfil antes y después). Lo interno va a `%LOCALAPPDATA%\RadarEmpleo\ejecuciones\<id>\` y a la BD.

**En la app:** panel «CV y carta» con pestañas CV | Carta | Correo (PDF en iframe vía `GET /documentos/{id}/archivo`), «Lo que ha cambiado», «Qué decir en la entrevista», «Dudas para ti», veredicto; barra de pasos con tiempo y «Cancelar» (sondeo cada 5 s). Móvil v1: solo el nombre.

**Pedir cambios:** (1) «Abrir en Word» + «Revisar otra vez»; (2) chips fijos («Más corto», «Quitar/Añadir la carta», «Destacar más el proyecto X» de una lista, «Cambiar el titular»); (3) comentario ≤300 caracteres, solo puerto local, entre delimitadores como dato, revisor siempre después, guardado como Nota (excepción acotada aprobada).

**Coste estimado (a medir en F4):** encaje 0,1-0,4 USD; redactor 0,5-1,5; revisor 0,2-0,6; peor caso 20-30 min y 2-6 USD. Topes: 0,5 / 2 / 1 por paso, 8 por preparación, 15 al día (Ajustes, «Gastado hoy»). `sonnet` en el revisor si basta.

**Fallos:** tiempo/CLI/tope → «Error» + «Reintentar» (no se borra nada en Curriculum); JSON fuera de esquema o rutas fuera de la carpeta → no se vincula; PDF de 2 páginas → fallo del revisor; hash del registro/perfil cambiado → aviso rojo; Word abierto → «Cierra el CV en Word y pulsa Reintentar»; API reiniciada → «Error: se interrumpió».

## 4. Enviar candidatura (F9-F10)

| Canal | Cómo | Parada en Manual | Especiales |
|---|---|---|---|
| Correo (Outlook.com) | C# sin IA: Graph `POST /me/sendMail`, MSAL cliente público con código de dispositivo, `Mail.Send` + `User.Read`, token cifrado con DPAPI en `%LOCALAPPDATA%\RadarEmpleo`; adjuntos PDF | Vista previa (para, asunto, cuerpo editable, adjuntos) y «Enviar correo» | Plan B: agente con Chrome en outlook.live.com. SMTP con contraseña no sirve |
| LinkedIn solicitud sencilla | Agente `enviador-candidaturas` con `--chrome`, una pestaña sin traerla al frente; truco del input de archivo | Página «Revisar» | Desmarcar «Seguir a la empresa»; si redirige a la web, cambiar canal; carta solo si hay campo |
| InfoJobs | El mismo agente; carta como texto | Antes del último «Inscribirme/Enviar» | Un solo CV adjunto: si hay otro, para («Borra el CV antiguo y pulsa Seguir»); avisos de datos: siempre su sí; PrevIA aceptada (permiso permanente); comprobar «Ya te has inscrito» |
| Web sin cuenta | El mismo agente | Antes de «Enviar» | Permiso de dominio de la extensión → para; fecha de nacimiento vacía; solo la casilla RGPD de la candidatura; nunca crear contraseña |
| Web con cuenta (Workday) | Solo tras crear cuenta e iniciar sesión él | Página «Revisión» | Corregir autorrelleno (dirección, mayúsculas) |

Agente nuevo `~/.claude/agents/enviador-candidaturas.md` (aprobado) con modos **rellenar** (nunca pulsa el botón final) y **confirmar**, para la app y `/empleo`. Respuestas habituales de formularios → nueva sección en `perfil-busqueda.md` (aprobado; la escribe el equipo con su OK).

**Dos pasos:** (1) `ReglasEnvio` en C# (preparación Lista o aceptada, PDF con hash revisado, no enviada ya, un envío abierto por candidatura, aviso si misma empresa <30 días, bloqueo de Chrome libre). (2) Paso A «Rellenar» (máx. 10 min, 1,5 USD) → JSON con resultado (ListoParaEnviar/NecesitaTuAyuda/OfertaCerrada/YaInscrito/Error), campos, adjuntos, consentimientos, preguntas sin responder, botón final, url. (3) Manual: «Espera tu OK» con tarjeta resumen y «Enviar ahora» / «Lo termino yo en Chrome» / «Cancelar»; Automático: si pasan todas las reglas, paso B. (4) Paso B «Confirmar» (`--resume`, máx. 3 min, 0,5 USD): comprueba que todo coincide con el resumen aprobado; si no, no pulsa. Plan B sin `--resume`: rellenar otra vez, comparar y pulsar. (5) `POST /envios/{id}/confirmar` con `hashResumen` (409 si cambió), solo puerto local y JSON.

En Manual el prompt dice, de verdad, «Eduardo ha confirmado este envío en RadarEmpleo el DD/MM a las HH:MM con este resumen». En Automático solo «Eduardo ha autorizado envíos automáticos con estas reglas»; si la extensión no lo acepta, Automático solo para correo.

**Al terminar:** Enviada (Agente) con FechaEnvio y seguimiento +7, `TextoConfirmacion`, `UrlFinal`, nota en la ficha, aviso y regenerar `radar\candidaturas.md` y `radar\envios.md`. «Lo termino yo» → «Ya la he enviado». Sin confirmación clara → «Sin confirmar: compruébalo», nunca se reintenta solo.

**La app no escribe en `ofertas-enviadas.md`** (aprobado): escribe `radar\envios.md` («NO EDITAR»), y la línea ya aprobada en `SKILL.md`, `cazador-ofertas.md` y `analista-ofertas.md` se amplía: «Lee también `radar/candidaturas.md` y `radar/envios.md`; si un envío no figura en `ofertas-enviadas.md`, añádelo al registro con el estilo de siempre».

## 5. Modo Automático (F11)

Eduardo sigue eligiendo cada oferta («Preparar y enviar»); el Automático solo quita el OK final. Reglas en `ReglasEnvioAutomatico` (pura, con tests); si falla una, ese envío va a Manual y se dice por qué.

| Salvaguarda | Valor aprobado |
|---|---|
| Interruptor | Manual por defecto; Automático solo en Ajustes con aviso y confirmación |
| Desbloqueo por canal | Tras 5 envíos manuales correctos por ese canal |
| Canales | Correo sí; web sin cuenta sí (desmarcado al principio); LinkedIn e InfoJobs nunca (sin casilla, con explicación) |
| Revisor | Solo LISTA en la última vuelta y hash del PDF igual al revisado |
| Encaje | Alto o Medio |
| Dudas del redactor | Ninguna pendiente |
| Límite | 3 al día (hasta 5), 10 min entre envíos, 9:00-21:00 Madrid |
| Duplicados | Nunca la misma oferta; misma empresa no en 30 días |
| Avisos de datos/RGPD/términos | Nunca sin su sí, salvo permisos permanentes: PrevIA (sí); «casilla de privacidad imprescindible» (pendiente de que la elija) |
| Gasto | Tope diario; al llegar, pausa |
| Registro y aviso | `Envio` + `radar\envios.md` + log; correo a sí mismo por Graph y línea en Hoy |
| Pausa | Botón grande en Hoy y Ajustes; pausa sola tras 2 fallos seguidos, captcha o tope |

Siempre a Manual: pregunta abierta, salario, pregunta nueva, captcha (pausa el canal 24 h), crear cuenta, contraseña/iniciar sesión, permiso de dominio, aviso de datos no cubierto, datos sensibles, InfoJobs con otro CV, pide carta y no hay, autorrelleno que no coincide, >6 páginas, cualquier diferencia con el resumen.

## 6. Modelo de datos y endpoints

- `Documento` (F5): + Origen (Registro, Manual, Preparacion), PreparacionId?, Vigente, HashSha256?, Paginas?, FechaAlta; TipoDocumento + Correo.
- `Preparacion`: Id, CandidaturaId, Estado, Vuelta, ConCarta, CanalPrevisto, DestinoCorreo?, CarpetaSemana, CvPartida?, Veredicto?, InformeRevisorJson?, CambiosJson?, PuentesJson?, DudasJson?, AceptadaPorEduardo, CosteUsd, Inicio, Fin?, Error?.
- `Envio`: Id, CandidaturaId, PreparacionId, Canal (Correo, LinkedIn, InfoJobs, WebEmpresa), Modo, Estado, Destino, Asunto?, Cuerpo?, AdjuntosJson, ResumenJson, HashResumen, ConsentimientosJson, MotivoParada?, SesionClaude?, ConfirmadoEn?, FechaEnvio?, TextoConfirmacion?, UrlFinal?, CosteUsd. Índice único filtrado: un envío por candidatura en {Rellenando, EsperaTuOk, Enviando, Enviada}.
- `AjustesEnvio` (una fila, en BD): Modo, CanalesAutomaticos, LimiteDiario, MinutosEntreEnvios, HoraDesde/HoraHasta, Pausado, MotivoPausa?, PermisoPrevIA, PermisoPrivacidadBasica, TopeGastoDiarioUsd, CorreoAvisos.
- `EjecucionAgente`: + PreparacionId?, EnvioId?, Paso?; TareaAgente + Redactar, Revisar, RellenarEnvio, ConfirmarEnvio.

Estados:
```
Preparación: EnCola → Redactando → Revisando → (Corrigiendo → Revisando)×2 → Lista | ConFallos ; → Error | Cancelada ; Lista/ConFallos → Corrigiendo
Envío: Comprobando → Rellenando → EsperaTuOk → Enviando → Enviada ; Rellenando/Enviando → NecesitaTuAyuda | LoTerminasTu ; Enviando → SinConfirmar ; antes de Enviando → Cancelada | Fallido | OfertaCerrada
```

Endpoints (los que lanzan agentes o envían, solo endpoint local): `POST /candidaturas/{id}/preparaciones`, `GET /preparaciones/{id}`, `GET /candidaturas/{id}/preparaciones`, `POST /preparaciones/{id}/cambios`, `/revisar-otra-vez`, `/usar-igualmente`, `/cancelar`, `POST /documentos/{id}/abrir-en-word`, `POST /candidaturas/{id}/envios`, `GET /envios/{id}`, `GET /envios?desde&estado`, `POST /envios/{id}/confirmar {hashResumen}`, `PUT /envios/{id}/correo`, `POST /envios/{id}/seguir|lo-termino-yo|ya-enviada|cancelar`, `GET|PUT /ajustes/envio`, `POST /ajustes/envio/pausar|reanudar`, `GET /correo/estado`, `POST /correo/conectar|desconectar`; `GET /panel/hoy` gana «Esperan tu OK», «Necesitan tu ayuda», «Enviadas hoy» y estado del Automático.

## 7. Riesgos nuevos

| Riesgo | Mitigación |
|---|---|
| Condiciones de LinkedIn/InfoJobs (automatizar puede restringir la cuenta; rellenar con agente ya lo es) | Nunca en Automático; poco volumen, una pestaña, su sesión, clic final con su OK; nada extra (seguir, guardar) |
| Captchas | Nunca se resuelven; «Necesita tu ayuda» y canal en pausa 24 h |
| Irreversibles (doble envío, CV de otra empresa) | Índice único, «Ya te has inscrito», paso B compara con el resumen, hash del PDF, revisor busca restos; «Sin confirmar» no se reintenta |
| Extensión sin persona / último clic bloqueado | Se averigua en F4; plan B: clic de Eduardo y Automático solo correo; nunca fingir confirmación |
| Quitarle el foco | Una pestaña sin traer Chrome al frente; procesos ocultos; Word solo cuando él lo pide |
| Choque con `/empleo` o la tarea de los lunes | Bloqueo compartido de Chrome; una ejecución a la vez |
| Agente toca registro/perfil | Permisos por ruta, prompt, hash antes/después con aviso |
| Datos personales | BD local y `%LOCALAPPDATA%`, DPAPI, Graph con permisos mínimos, ClientId en `appsettings.Local.json`, Demo apagada, script en CI |
| Coste | Topes por paso, preparación y día; «Gastado hoy»; `sonnet` donde baste |
| Inyección desde la web de la oferta | Enviador sin herramientas de archivos; resumen revisado; paso B compara |
| Web abierta llama a `localhost:5180` | Solo JSON con preflight, sin CORS ajeno, `hashResumen`, solo puerto local |
| Entra no deja con cuenta personal | F4; plan B Chrome en outlook.live.com |

## 8. Decisiones de Eduardo (06/11/2026)

Todas aprobadas según la recomendación: (1) ciclo completo antes de móvil y demo; (2) F4 primero y aceptar el plan B que salga; (3) CV en `Candidaturas\Semana…`, los escribe el agente; (4) la app no escribe `ofertas-enviadas.md`, escribe `radar\envios.md` y `/empleo` lo pasa; (5) correo por Graph con su hotmail; (6) agente `enviador-candidaturas`; (7) «Respuestas habituales de formularios» en el perfil; (8) Word + chips + comentario ≤300; (9) en Automático él elige cada oferta; (10) correo y webs sin cuenta sí, LinkedIn/InfoJobs nunca; (11) desbloqueo tras 5 manuales; (12) 3/día, 10 min, 9-21 h; (13) PrevIA sí, casilla de privacidad **pendiente**; (14) 8 USD por preparación y 15 al día; (15) aviso por correo a sí mismo; (16) en el móvil solo ver, no confirmar.
