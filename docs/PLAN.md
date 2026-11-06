# RadarEmpleo — Plan de arquitectura (v1)

Preparado por el arquitecto el 27/10/2026. Pendiente de que Eduardo tome las decisiones del apartado 9 antes de empezar a programar.

## 1. Resumen

API ASP.NET Core (.NET 10) con EF Core y SQLite, organizada por capas como Siessta y Descanso, y una app Angular 22 (standalone, signals, zoneless) que se usa en el navegador del PC y en Android con Capacitor 8.

- **Datos (híbrido):** la base de datos manda en el **estado** de las candidaturas. Los `.md` de `Curriculum` siguen siendo el **diario** que escriben los agentes. La app los lee (nunca los modifica) y los importa con vista previa, con un analizador propio en C# y con `claude -p --json-schema`.
- **Dónde se ejecuta:** en local, en el PC de Eduardo. El móvil entra por Tailscale. Habrá una demo pública aparte, con datos inventados y los agentes desactivados.

Datos de partida (leídos el 27/10): `ofertas-enviadas.md` tiene 358 líneas, una tabla de 15 filas que solo llega al 04/09, 37 secciones «## …» no siempre en orden, 29 menciones de «ENVIAD…» y 48 enlaces. Después del 04/09 casi todo está en texto libre, así que importar solo la tabla trae 15 de unas 45-50 candidaturas: la importación necesita la ayuda de Claude desde el principio. La carpeta `Curriculum\rastreos\` todavía no existe.

## 2. Alcance

**Versión 1 (2-3 semanas)**
1. Tablero por estados: Detectada → Preparada → Enviada → Entrevista → Oferta, más Descartada y Cerrada. Arrastrar en el PC; botón «Mover a…» en el móvil.
2. Ficha: empresa, puesto, enlace, modalidad, ubicación, encaje, pega, fechas, motivo de descarte, notas con fecha, historial de estados y documentos (abrir el PDF).
3. Importación del registro con vista previa, repetible sin duplicar: la tabla con un analizador propio y el texto libre con Claude.
4. Perfil y filtros (solo lectura de `perfil-busqueda.md`).
5. Bandeja de rastreos: leer el informe y pasar una oferta a «Detectada» con un clic.
6. Recordatorios (seguimientos, entrevistas, plazos, publicaciones de LinkedIn) y pantalla «Hoy». Al marcar «Enviada» se crea un seguimiento a 7 días.
7. Estadísticas: por semana, por estado y motivos de descarte.
8. Agentes, solo desde el PC: «Lanzar rastreo», «Analizar encaje» y «Extraer del registro con Claude», con lista de ejecuciones.
9. Android: APK con tablero, ficha, notas rápidas y alta rápida.
10. Demo pública con datos de ejemplo, README con capturas y CI en GitHub Actions.

**Para después (v2):** «Preparar candidatura» desde la app (usa Word desde PowerShell); servidor MCP de RadarEmpleo para que el coordinador registre directamente en la API; iOS; notificaciones locales; trabajo sin conexión; compartir enlace desde InfoJobs; filtros editables; JWT multiusuario; SQL Server como alternativa.

## 3. Arquitectura y versiones

| Pieza | Elección | Versión |
|---|---|---|
| SDK | .NET | 10.0.400 (instalado) |
| API | ASP.NET Core Web API con controladores | net10.0 |
| ORM | EF Core + SQLite (`Microsoft.EntityFrameworkCore.Sqlite` y `.Design`) | `10.0.*` (última 10.0.14) |
| Documentación API | `Microsoft.AspNetCore.OpenApi` + Scalar.AspNetCore | Scalar: comprobar al empezar |
| Tests API | xUnit como en Siessta + `Microsoft.AspNetCore.Mvc.Testing` | 10.0.14; si la plantilla trae xUnit v3, usar esa |
| Front | Angular standalone, signals, zoneless | CLI 22.2.0, paquetes `^22.2.0` |
| Estilos | Tailwind 4 (como StickerApp) | `^4.3.3` |
| Componentes | Angular Material + CDK | 22.2.4 |
| Tests front | Vitest con `@angular/build:unit-test` | el del CLI 22 |
| Markdown | `marked` | comprobar al empezar |
| Móvil | `@capacitor/core`, `android`, `cli`, `preferences` | 8.5.3 / 8.0.1; iOS preparado sin compilar |

- **SQLite** (no SQL Server): un archivo, nada que instalar, lo que ya domina, tests con el mismo motor en memoria y gratis en la demo. Cambiar de motor después es configuración.
- **Angular Material + CDK con Tailwind** (no Ionic): Ionic 9 exige `zone.js`, lo que choca con zoneless; Material trae diálogos, selector de fecha, menús, snackbar, bottom sheet y el drag & drop del CDK; las ofertas piden Angular, no Ionic.

**Convenciones que se copian**
- Backend, de [SiestaDescanso](https://github.com/eduardolar/SiestaDescanso): proyectos Domain, Data, Services y Tests; `IDbContextFactory<AppDbContext>` en los servicios; enums como texto (`HasConversion<string>()`); carpeta `Migraciones`; `IClock` / `RelojSistema` y `ReglaNegocioException`; `AppDbContextFactory` para `dotnet ef`; BD en `%LOCALAPPDATA%\RadarEmpleo\radar.db`; `/// <summary>` en español; tests `Metodo_Situacion_Resultado`; copiar `ArnesSqliteTests`.
- Front, de StickerApp (un proyecto de prácticas en Angular): carpeta por funcionalidad con `pages/`, `components/`, `services/`, `interfaces/`, `utils/` (funciones puras con `.spec.ts`); páginas `xxx-page.component.ts` con `export default` y carga diferida; servicios con `signal` privado y `asReadonly()`; `skipTests` en schematics; `environment.example.ts`; copiar `scripts/android-gradle.mjs` y `java-home.mjs`.
- Dominio en español (Candidatura, Estado…), sufijos técnicos en inglés (Service, Controller).

## 4. Dónde viven los datos: modelo híbrido (recomendado)

| | (a) BD como única fuente | (b) Los .md como fuente | (c) Híbrido |
|---|---|---|---|
| Qué es | Importación inicial; los agentes escriben por API | La app analiza los .md cada vez | La BD manda en el estado y los .md en el texto, con sincronización y vista previa |
| A favor | Una sola verdad | Cero cambios para los agentes | Los agentes siguen igual y la app tiene datos estructurados |
| En contra | Reescribir skill y 6 agentes; si la app está apagada no se registra nada | Análisis frágil; las notas de la app no tienen dónde guardarse | Dos sitios: hace falta una regla clara |
| Esfuerzo v1 | Alto | Medio, sin fin | Medio y acotado |

Reglas del híbrido:
1. Los `.md` originales los escriben solo los agentes y el coordinador. La app solo los lee (`FileAccess.Read` + `FileShare.ReadWrite`, a través de `ILectorCurriculum`, que solo tiene métodos de lectura; un test lo comprueba).
2. La base de datos solo la escribe la app.
3. Archivo nuevo `Curriculum\radar\candidaturas.md`, generado por la app («NO EDITAR: lo genera RadarEmpleo»), con una línea por candidatura. Se añade una línea a `SKILL.md`, `cazador-ofertas.md` y `analista-ofertas.md`: «Lee también `radar/candidaturas.md` si existe».
4. Sincronización incremental: el registro se parte por secciones «## », se guarda el hash de cada una (`SeccionImportada`) y solo se procesan las nuevas o cambiadas, con vista previa («nueva», «ya existe», «avanza de estado», «conflicto»).
5. Conflictos (`ReglasImportacion`, con tests): nunca hace retroceder un estado, nunca borra y no sobrescribe las notas de la app (las del .md se añaden con origen «Registro»).

Importación: formato común `LoteImportacion` (JSON con versión). Productor A: analizador determinista en C# de la tabla. Productor B: `claude -p` con el texto por la entrada estándar, sin herramientas y con `--json-schema`. Consumidor: `ImportacionService.Previsualizar(lote)` y `Aplicar(lote, idsAceptados)`, idempotente por `ClaveImportacion` (empresa + puesto normalizados + fecha, índice único). `ResolverDocumentos` busca en las carpetas `Semana*` el archivo de cada nombre y, si no lo encuentra, lo marca como «no encontrado».

## 5. Dónde se ejecuta

- **En el PC:** la API escucha en dos puertos.
  - `localhost:5180` (local): para el navegador del PC; incluye los agentes y no pide token.
  - `localhost:5181` (remoto): para el móvil; pide la cabecera `X-Radar-Token` y no tiene endpoints de agentes. Los endpoints de agentes comprueban `HttpContext.Connection.LocalPort == 5180` y `Agentes:Habilitado`. Hacen falta dos puertos porque `tailscale serve` reenvía desde localhost.
  - En producción, la API sirve el build de Angular desde `wwwroot` (un proceso, una URL, sin CORS).
- **Móvil por Tailscale** (gratis para uso personal, comprobar condiciones): `tailscale serve --https=443 http://localhost:5181` y el móvil usa `https://<pc>.<tailnet>.ts.net`. Funciona fuera de casa, con HTTPS válido y sin exponer nada. El PC tiene que estar encendido; la app avisa con un banner.
- **Datos personales:** BD fuera del repositorio; rutas y token en `appsettings.Local.json` (en `.gitignore`) o `user-secrets`; el repositorio solo lleva `datos-ejemplo/` inventados; `scripts/comprobar-datos-personales.ps1` (también en CI) busca teléfonos, DNI y correos que no sean de `example.com`.
- **Demo pública:** `RadarEmpleo:Modo=Demo` resiembra los datos de ejemplo al arrancar y cada 24 h; agentes desactivados con ejecuciones de ejemplo; limitador de peticiones; `Dockerfile` en el plan gratis de Render (o Azure App Service F1); APK de demo en GitHub Releases.

## 6. Integración con Claude (seguridad)

- `IEjecutorClaude` con `EjecutorClaudeCli` (lanza `claude.cmd`) y `EjecutorClaudeFalso` para tests.
- El usuario no escribe prompts: tareas fijas (`TareaAgente`: Rastreo, AnalizarEncaje, ExtraerRegistro) con plantillas en `prompts/*.md` y esquemas `prompts/*.schema.json`; los parámetros son ids validados.
- El prompt va por la entrada estándar (comprobar al empezar que `claude -p` 2.1.185 lo admite).
- Opciones: `-p`, `--output-format json`, `--json-schema`, `--allowedTools` / `--disallowedTools`, `--permission-mode`, `--max-budget-usd`. Carpeta de trabajo: Curriculum.
  - AnalizarEncaje: solo `Read,Grep,Glob,WebFetch,WebSearch`.
  - ExtraerRegistro: sin herramientas.
  - Rastreo: lo mismo que `_rastreo_semanal.ps1`, más guardar `rastreos\rastreo_AAAA-MM-DD.json`.
- Cola con `Channel<T>` + `BackgroundService`: una ejecución a la vez, tiempo máximo (rastreo 30 min, análisis 5 min), cierre del árbol de procesos y archivo de bloqueo `rastreos\.en-curso` para no pisarse con la tarea de los lunes.
- Cada ejecución se guarda en `EjecucionAgente` (estado, tiempos, coste, resumen, log en `%LOCALAPPDATA%\RadarEmpleo\logs`). El front consulta el estado cada 5 s.

## 7. Modelo de datos, endpoints, repositorio y pantallas

**Entidades** (enums como texto)
- `Empresa`: Id, Nombre, NombreNormalizado (único), Web?, Notas?
- `Candidatura`: Id, EmpresaId, Puesto, Enlace?, Portal?, Modalidad (Remoto, Hibrido, Presencial, NoIndicada), ModalidadDetalle?, Ubicacion?, Encaje (SinValorar, Alto, Medio, TiroLargo, No), Pega?, Analisis?, Salario?, Estado, FechaDeteccion, FechaEnvio?, FechaUltimoCambio, MotivoDescarte? (Experiencia, Ingles, Titulacion, Zona, Modalidad, Perfil, Salario, Cerrada, DecisionPropia, Otro), MotivoDescarteTexto?, Origen (Manual, Registro, Rastreo, Movil, Agente), ClaveImportacion? (única), SeccionOrigen?. Métodos `PuedePasarA(estado)` y `CambiarEstado(...)`.
- `CambioEstado`: Id, CandidaturaId, De?, A, Fecha, Comentario?, Origen
- `Nota`: Id, CandidaturaId, Texto, Fecha, Origen
- `Documento`: Id, CandidaturaId, Tipo (CV, Carta, GuiaEntrevista, Otro), NombreArchivo, RutaRelativa, Encontrado
- `Recordatorio`: Id, CandidaturaId?, Tipo (Seguimiento, Entrevista, PublicacionLinkedIn, Plazo, Otro), Titulo, FechaHora, Hecho, Notas?
- `Rastreo`: Id, Fecha, RutaInforme, Origen (TareaLunes, App), EjecucionAgenteId?
- `OfertaRastreo`: Id, RastreoId, TextoOriginal, Empresa?, Puesto?, Enlace?, Encaje?, Pega?, Estado (Pendiente, Convertida, Ignorada), CandidaturaId?
- `EjecucionAgente`: Id, Tarea, ParametrosJson, Estado (EnCola, EnCurso, Ok, Error, Cancelada), Inicio, Fin?, CosteUsd?, Resumen?, RutaLog, CandidaturaId?
- `SeccionImportada`: Id, Archivo, Titulo, Hash, FechaImportacion

Relaciones: Empresa 1-N Candidatura; Candidatura 1-N CambioEstado, Nota, Documento y Recordatorio (cascada); Rastreo 1-N OfertaRastreo; OfertaRastreo 0..1 → Candidatura.

**Reglas de estado** (`ReglasEstado`, con tests): avanzar a cualquier estado posterior; Descartada y Cerrada desde cualquiera (Descartada pide motivo); «Reabrir» lleva a Detectada; al pasar a Enviada se rellena FechaEnvio y se crea un seguimiento a +7 días.

**Endpoints** (`/api`; errores con ProblemDetails; `ReglaNegocioException` → 409)
- Candidaturas: `GET /candidaturas?estado&texto&desde`, `GET /candidaturas/{id}`, `POST`, `PUT /{id}`, `DELETE /{id}`, `POST /candidaturas/{id}/estado`, `POST /candidaturas/{id}/notas`, `DELETE /notas/{id}`
- Empresas: `GET /empresas?texto`
- Documentos: `GET /documentos/sugeridos?candidaturaId`, `POST /candidaturas/{id}/documentos`, `DELETE /documentos/{id}`, `GET /documentos/{id}/archivo` (solo .pdf/.docx dentro de la carpeta raíz, con comprobación contra «..»)
- Recordatorios: `GET /recordatorios?desde&hasta&pendientes`, `POST`, `PUT /{id}`, `POST /{id}/hecho`, `DELETE /{id}`, `POST /recordatorios/importar-linkedin/previsualizar` y `/aplicar`
- `GET /panel/hoy`
- Estadísticas: `/estadisticas/por-estado`, `/por-semana`, `/motivos-descarte`
- `GET /perfil`
- Rastreos: `GET /rastreos`, `GET /rastreos/{id}`, `POST /rastreos/escanear`, `POST /rastreos/ofertas/{id}/detectar`, `POST /rastreos/ofertas/{id}/ignorar`
- Importación (F3): `POST /importacion/tabla/previsualizar` (lee la tabla del registro y la compara con la BD, sin escribir) y `POST /importacion/lote/aplicar` (recibe el lote y las claves elegidas). El análisis de las secciones de texto libre (y con él una vista previa de un lote propuesto por un agente) va en la F6, con «Extraer registro».
- Agentes (solo puerto local): `POST /agentes/rastreo`, `POST /agentes/analizar/{candidaturaId}`, `POST /agentes/extraer-registro`, `GET /agentes/ejecuciones`, `GET /agentes/ejecuciones/{id}`, `POST /agentes/ejecuciones/{id}/cancelar`
- Sistema: `GET /sistema/info`, `/health`

**Repositorio**
```
RadarEmpleo.slnx
README.md  .gitignore  .editorconfig  Dockerfile
.github/workflows/ci.yml
src/RadarEmpleo.Domain/
src/RadarEmpleo.Data/          (AppDbContext, AppDbContextFactory, Migraciones/, SembradorDemo)
src/RadarEmpleo.Services/      (servicios, Importacion/, Agentes/, Curriculum/ILectorCurriculum, IClock, Contratos/)
src/RadarEmpleo.Api/           (Program.cs, Controllers/, Seguridad/, wwwroot/)
tests/RadarEmpleo.Tests/
web/                           (Angular + Capacitor)
prompts/
datos-ejemplo/
scripts/
docs/PLAN.md
```

**Pantallas** (lateral en el PC, barra inferior en el móvil: Hoy · Tablero · + Apuntar · Rastreos · Más)
1. Hoy: recordatorios vencidos y de la semana, contadores por estado, aviso de informe nuevo.
2. Rastreos: informe a la izquierda y ofertas a la derecha con «→ Detectada» o «Ignorar»; «Lanzar rastreo» solo en el PC.
3. Tablero: columnas por estado (en el móvil, lista con filtro y «Mover a…»).
4. Ficha: datos, «Analizar encaje con Claude», documentos sugeridos, botón «Marcar enviada», línea de tiempo y recordatorios.
5. Apuntar (móvil): nota rápida o alta rápida.
6. Agenda, Estadísticas (barras con Tailwind), Perfil, Importación y Ajustes.

Flujo más corto: Rastreos → «Detectada» → abrir ficha → vincular CV sugerido → «Marcar enviada» (4 toques; el seguimiento se programa solo).

## 8. Fases (unos 13-14 días)

| Fase | Qué | Días | Cómo comprobarlo |
|---|---|---|---|
| F0 | Esqueleto: git privado, solución, `webapi --use-controllers`, `ng new` en `web/` sin SSR, Tailwind, Material, proxy, `/health`, `/api/sistema/info`, `.gitignore`, CI mínima | 0,5 | `dotnet test` y `ng test` en verde; `ng serve` muestra «API conectada · modo Local» |
| F1 | Dominio y API de candidaturas, migración `Inicial`, `ReglasEstado`, Scalar, ~30 tests | 2 | Desde Scalar: crear, pasar a Enviada y ver historial y seguimiento a +7 días |
| F2 | Tablero y ficha en Angular (signals), diálogo de estado, notas | 2 | Crear 3, arrastrar una a Enviada, recargar y que siga igual |
| F3 | Importación determinista, `ExportadorRadarMd`, página de Importación | 1,5 | Todas las filas de la tabla del registro en la vista previa (27 el 01/11; los ~41 envíos que solo constan en texto libre quedan para la F6, «Extraer registro»); aplicar dos veces sin duplicados; `Get-FileHash` del registro igual antes y después; aparece `radar\candidaturas.md` |
| F4 | Documentos, perfil y bandeja de rastreos | 1,5 | Abrir el PDF del CV de Marco Talento; pasar una oferta de ejemplo a Detectada |
| F5 | Recordatorios, Hoy y estadísticas (con `IClock`) | 1,5 | Aparecen el 3/11 y el 17/11 de LinkedIn; un seguimiento real en Hoy |
| F6 | Agentes: ejecutor, cola, prompts y esquemas, filtro de puerto, sincronización por hash | 2 | Extraer registro propone ~45-50 candidaturas; Analizar encaje rellena la pega; 403 por el puerto 5181 |
| F7 | Móvil: Capacitor, interceptor, Ajustes, CORS, Tailscale, APK | 1,5 | Con datos móviles, ver el tablero y apuntar una nota que aparece en el PC |
| F8 | Demo y portfolio: Modo Demo, Dockerfile, Render, APK de demo, script de datos personales en CI, README y GIF | 1,5 | URL pública con datos inventados; CI en verde; el script no encuentra datos personales |

## 9. Decisiones de Eduardo antes de empezar

1. Modelo híbrido y añadir una línea en la skill `/empleo` y en dos agentes para que lean `radar/candidaturas.md`. **Recomendado: sí.**
2. Móvil por Tailscale (**recomendado**) o solo con la misma wifi.
3. Agentes en la v1: Lanzar rastreo, Analizar encaje y Extraer registro (**recomendado**); «Preparar candidatura» en la v1.1.
4. Repositorio privado hasta pasar la comprobación de datos personales en la F8, y después público (**recomendado**).
5. Demo en Render gratis con Docker (**recomendado**) o Azure App Service F1.

## 10. Riesgos

- Texto libre irregular en el registro → siempre vista previa; la clave de importación incluye el puesto.
- El informe de los lunes aún no existe → analizador por URL como alternativa; pedir un `.json` añadiendo una línea al prompt de `_rastreo_semanal.ps1`.
- Cambios en la CLI de Claude → todo aislado en `EjecutorClaudeCli`; fijar en el README la versión probada (2.1.185).
- Coste y concurrencia → una ejecución a la vez, bloqueo compartido con la tarea de los lunes, tiempo máximo y `--max-budget-usd`.
- Seguridad de agentes → protección por puerto, nunca texto libre como prompt.
- Material con el reset de Tailwind 4 → probar en la F0.
- PDF en Android detrás de token → en la v1 del móvil solo se muestra el nombre.
- Fugas de datos personales → script, `.gitignore` y `datos-ejemplo/` desde el primer commit.
- Alcance justo → si se retrasa, recortar la importación de LinkedIn, las estadísticas por motivo y el arrastrar del tablero.

## 11. Pendientes detectados en la revisión de F0-F1

Anotados al corregir la revisión de las fases 0 y 1; se harán en la fase indicada.

- **Concurrencia y móvil (F7):** columna `Version` (token de concurrencia) en Candidatura para no pisar cambios entre el PC y el móvil, y un `PATCH` para ediciones parciales desde el móvil. (Desde la F3, `FechaUltimoCambio` ya es token de concurrencia y un choque da 409; falta que el front mande la versión que leyó para detectar ediciones hechas sobre una ficha vieja.)
- **Origen fijado en el servidor (F6/F7):** que el `Origen` (Manual, Movil, Agente…) lo decida la API según el puerto o la ruta por la que llega la petición, no lo que mande el cliente.
- **Empresas:** endpoint para renombrar una empresa o fusionar/limpiar duplicadas y huérfanas.
- **Modo Demo (F8):** ocultar Scalar y `/openapi` en modo Demo.
- **Datos personales antes de publicar (F8):** quitar de este `PLAN.md` y de los comentarios del código el nombre «Eduardo», las empresas reales y las rutas locales antes de hacer público el repositorio. Quitarlos también del historial de git o publicar con un historial nuevo: el `PLAN.md` ya está en el primer commit.
- **Endpoint de agentes (F6):** comprobar que una petición llega por el endpoint permitido por su nombre o por la configuración de Kestrel, no comparando con el puerto 5180 escrito en el código.
- **Filtro de texto del listado:** si el volumen crece, proyectar solo los campos del resumen antes de filtrar en memoria (hoy se cargan las entidades completas con la empresa).

### Detalles menores del segundo test de F0-F1 (para la F2)
- Las altas simultáneas de una empresa nueva funcionan, pero EF Core deja en el log líneas `fail:` con «UNIQUE constraint failed» aunque el conflicto ya se reintenta: bajar ese caso a información o filtrar el log.
- Tras pulsar «Reintentar» y reconectar, el foco cae en `<body>`: llevarlo al indicador o a `main` (`estado-api.ts`).
- Un JSON cortado (`{"empresa": "A", "puesto": `) devuelve «El valor de «puesto» no es válido.» en vez del mensaje genérico de JSON no válido.

### Pendientes de la revisión de la F2
- **Validar el enlace en la API (F3):** el front normaliza y rechaza esquemas raros, pero la importación podría traer enlaces sin `https://`.
- **Seguimiento al pasar a Entrevista u Oferta (F5):** sigue pendiente aunque ya haya respuesta de la empresa; decidir si se marca como hecho al preparar «Hoy».
- **Alta solo con el enlace (decisión de Eduardo):** la API exige empresa y puesto. Opciones: un endpoint que lea la oferta o hacerlos opcionales.
- **Aviso de enlace duplicado al apuntar:** la búsqueda de la API no mira el enlace.
- **Probar en vivo sin abrir ventanas:** `spawn` sin `detached`, porque `detached` anula `windowsHide` y la API abre su propia ventana.
- **Sin comprobar en un móvil real:** el arrastre táctil y el teclado virtual. Los cambios finales de la F2 (botón único, «Más opciones», 48 px y textos) solo tienen tests, sin prueba en vivo.

### Pendientes de la revisión de la F3
- **Enlaces guardados antes de la F3:** normalizar los que ya están en la BD sin esquema (`www.x.com` → `https://www.x.com`); la importación y la API ya normalizan los nuevos.
- **«Aplicar» se fía del lote del cliente (F6):** el servidor lo recalcula todo, pero no comprueba que el lote salga del registro actual. Revalidarlo con el hash del registro cuando llegue la sincronización por hash.
- **`.tmp` huérfano en `radar\`:** si la API se cae entre escribir el temporal y moverlo, queda un `candidaturas.md.<guid>.tmp`. Limpiarlos al arrancar.
- **`<details>` repetido en `importacion-page.component.html`:** los tres grupos plegados repiten el mismo marcado; sacarlo a un componente.
- **Carpeta Curriculum vacía:** probar el caso de `CarpetaCurriculum` vacía con una configuración de test que no use la ruta real (hoy `appsettings.Local.json` apunta a ella).

## 12. Ampliación pedida por Eduardo el 06/11/2026 (replanificar F4-F6 al cerrar la F3)

Eduardo quiere que RadarEmpleo haga **todo el ciclo** de la búsqueda, como el equipo /empleo: rastrear → analizar encaje → **adaptar CV y carta a la oferta** → **enviar la candidatura**.

- **Preparar candidatura** (se adelanta de la v1.1): desde la ficha, lanzar redactor-cv + revisor-candidatura; ver el PDF en la app, pedir cambios y vincularlo como Documento.
- **Enviar candidatura:** por correo (mensaje con adjunto) o por portal (LinkedIn solicitud sencilla, InfoJobs, web de la empresa) con un agente que usa su Chrome. Al terminar: estado Enviada, FechaEnvio y seguimiento a +7 días.
- **Ajuste «Envío: Manual / Automático»** en Ajustes (decisión de Eduardo):
  - Manual (por defecto): la app lo deja todo listo y se para antes del último clic; Eduardo confirma cada envío.
  - Automático: envía sola. A proponer por el arquitecto con salvaguardas: solo si el revisor da OK, límite diario, lista de canales permitidos (avisar de que LinkedIn/InfoJobs pueden bloquear cuentas por envíos automatizados), registro de todo lo enviado y aviso inmediato a Eduardo.
- Sigue valiendo: nunca texto libre como prompt, protección por puerto y una ejecución de agente a la vez.
