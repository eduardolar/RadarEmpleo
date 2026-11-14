# F6: Recordatorios, Hoy, estadísticas y Ajustes

Plan del arquitecto del 13/11/2026 para la fila F6 de `docs/PLAN-AMPLIACION.md`. Respeta `docs/PLAN.md` (apartados 7 y 11) y `docs/DISENO.md`. Pendiente de que Eduardo responda a las decisiones del apartado 9; donde no las haya tomado, se sigue la recomendada.

## 1. Resumen

«Hoy» deja de ser un estado vacío y pasa a responder a «¿qué tengo que hacer hoy?». Arriba sale lo vencido y lo de hoy, con «Hecho», «Posponer» y «Deshacer». Debajo, lo de los próximos 7 días y las enviadas que siguen sin respuesta. Los recordatorios se apuntan con un diálogo corto: los sueltos (publicaciones de LinkedIn) desde Hoy y los de una candidatura (entrevistas, plazos) desde su ficha.

Al pasar a Entrevista u Oferta, el seguimiento pendiente se da por hecho, y el «Deshacer» de siempre lo devuelve. Las estadísticas van en una página propia a la que se entra desde «Más». Ajustes es una página casi vacía, también desde «Más», con lo poco que ya es real. «Esperan tu OK» queda como un hueco reservado en Hoy, sin pintar nada.

Se propone partir la fase en dos:

| Parte | Qué | Días | Cómo se da por buena |
|---|---|---|---|
| **F6a** | Modelo y migración, cierre del seguimiento en Entrevista/Oferta, API de recordatorios y del panel, Hoy, diálogo de recordatorio y bloque de la ficha | 1 | Los posts de LinkedIn del 3/11 y del 17/11; un seguimiento real en Hoy; Entrevista cierra el seguimiento y «Deshacer» lo devuelve |
| **F6b** | Estadísticas (API y página), Ajustes, Más y menú, «Apuntar la entrevista» en el aviso, documentación | 0,5 | Ajustes desde «Más»; las estadísticas cuadran con el tablero |

Las dos partes van en ramas cortas sobre `main` y se revisan por separado. La F6b no toca nada de la F6a salvo `opciones-menu.ts` y el aviso del cambio de estado.

## 2. Alcance

**Entra**
- `Recordatorio` con `FechaHecho`, y control de concurrencia sobre `Hecho` y `FechaHora`.
- Al pasar a Entrevista u Oferta, el seguimiento pendiente se da por hecho y queda apuntado en el historial, para que el «Deshacer» lo reabra. Una migración hace lo mismo con los que ya están en Entrevista u Oferta.
- Crear, editar, marcar como hecho o pendiente, posponer y borrar recordatorios. Sueltos desde Hoy y de una candidatura desde su ficha.
- `GET /api/panel/hoy` con estas listas: para hoy (vencidos y de hoy), próximos 7 días, más adelante, hechos hoy y enviadas sin respuesta.
- Página Hoy completa, con estados vacíos y el hueco «Esperan tu OK» (sin pintar).
- `GET /api/estadisticas` y su página: candidaturas por estado, enviadas por semana (8 semanas), tasa de respuesta, días medios hasta la respuesta y motivos de descarte.
- Página Ajustes con el modo y la versión (ya los da `GET /api/sistema/info`) y una línea que dice lo que llegará.
- Más: tarjetas de Estadísticas y Ajustes, y el menú, que marca «Más» también en `/estadisticas` y `/ajustes`.

**No entra** (y por qué)
- **Importar los posts de LinkedIn desde un archivo** (`/recordatorios/importar-linkedin`). Son pocos y se apuntan a mano en 20 s. Ya estaba en la lista de recortes de `PLAN-AMPLIACION.md`.
- **Repetición** («cada lunes»): ningún caso real la pide; los posts tienen fechas sueltas.
- **Página «Agenda»** y `GET /recordatorios?desde&hasta`: el panel ya trae «Más adelante», así que nada apuntado desaparece de la vista.
- **Notificaciones** del sistema o del móvil: son de la v2.
- **Cualquier dato o botón de «Esperan tu OK»**: llega en la F8-F11 con `Preparacion` y `Envio`. Ahora no hay campo en la API ni nada pintado.
- **Ajustes que se puedan cambiar**: el modo Manual o Automático y los topes son de la F11.
- **Seguimiento automático nuevo tras la entrevista**: no se sabe la fecha y sería un dato inventado. En su lugar, el aviso ofrece «Apuntar la entrevista» (F6b).

**Supuestos**
- Los recordatorios de una candidatura se crean desde su ficha y los sueltos desde Hoy. Así el diálogo no necesita un buscador de candidaturas.
- La hora es opcional: si no se pone, son las 09:00 de Madrid, como los seguimientos. No se añade «todo el día».
- «Posponer» solo se ofrece en lo vencido y en lo de hoy. Para mover algo futuro está «Cambiar fecha u hora», que abre el diálogo de edición.

## 3. Modelo y migración

### 3.1 `Recordatorio` (`src/RadarEmpleo.Domain/Candidaturas.cs`)

| Cambio | Para qué |
|---|---|
| `DateTime? FechaHecho` (UTC) | Lista «Hechos hoy», que deja deshacer después de los 8 s del aviso. Es null en lo pendiente y en lo cerrado antes de la F6 |
| `MarcarHecho(DateTime ahoraUtc)` | Pone `Hecho = true` y `FechaHecho` = ahora. Si ya estaba hecho, no hace nada (idempotente, contra el doble clic) |
| `MarcarPendiente()` | Pone `Hecho = false` y `FechaHecho = null` |
| `PosponerA(DateOnly diaMadrid)` | Mismo reloj de Madrid, otro día (cuenta con el cambio de hora). Lanza `ReglaNegocioException` si está hecho |

No hace falta columna de repetición, ni de «pospuesto desde», ni `Origen`.

**Concurrencia optimista:** `Hecho` y `FechaHora` pasan a ser tokens de concurrencia (`IsConcurrencyToken()` en `AppDbContext`). Así no hace falta ninguna columna nueva, y cubre todos los caminos que tocan un recordatorio: el panel, `CambiarEstado` y `DeshacerUltimoCambio`. El servicio de recordatorios captura `DbUpdateConcurrencyException` y la convierte en `ReglaNegocioException`: «Otra pestaña acaba de cambiar este recordatorio. Vuelve a cargar Hoy para verlo como ha quedado.» (409). Así no sale el mensaje de la candidatura de `ManejadorExcepciones`. Igual que hoy en Candidatura, el cliente no manda la versión que leyó.

### 3.2 Reglas puras: `src/RadarEmpleo.Domain/ReglasRecordatorio.cs` (nuevo)

- `HoraPorDefecto = ReglasEstado.HoraSeguimiento` (09:00).
- `LargoTitulo = 200`, `LargoNotas = 2000` y `DiasProximos = 7`.
- `DateTime FechaHoraUtc(DateOnly dia, TimeOnly? hora)` usa `Fechas.MadridAUtc`.
- `DateTime Pospuesta(DateTime fechaHoraUtc, DateOnly dia)` conserva la hora de Madrid.
- `TimeOnly LeerHora(string? texto)` admite solo «HH:mm» (00:00-23:59), y si viene vacía devuelve `HoraPorDefecto`. Si no es válida, lanza `ValidacionException(..., "hora")`.

### 3.3 Seguimiento al pasar a Entrevista u Oferta (`Candidatura.CambiarEstado`)

Hoy el `switch` solo rellena `FechaEnvio` en Entrevista y Oferta. Pasa a ser así:

```csharp
case EstadoCandidatura.Entrevista or EstadoCandidatura.Oferta:
    FechaEnvio ??= ahoraUtc;                       // igual que el «when FechaEnvio is null» actual
    CerrarSeguimientosPendientes(cambio, ahoraUtc);
    break;
case EstadoCandidatura.Descartada or EstadoCandidatura.Cerrada or EstadoCandidatura.Detectada:
    CerrarSeguimientosPendientes(cambio, ahoraUtc);
    break;
```

`CerrarSeguimientosPendientes` es el bucle que ya existe: llama a `MarcarHecho(ahoraUtc)` y apunta el primero en `cambio.SeguimientoCerrado`.

`DeshacerUltimoCambio` ya reabre `SeguimientoCerrado`. Solo hay que cambiar `cerrado.Hecho = false` por `cerrado.MarcarPendiente()`. Encaja sin más con el «Deshacer», porque es el mismo mecanismo que el de Descartada. Hay que actualizar los `/// <summary>` de `CambiarEstado` y de `CandidaturasController.CambiarEstado`.

Efecto colateral bueno: la importación que hace avanzar de Enviada a Entrevista (`ImportacionService.AvanzarAsync`) también cierra el seguimiento.

### 3.4 Descartar, cerrar, reabrir y borrar

- **Seguimiento:** como hasta ahora, se da por hecho y el «Deshacer» lo reabre.
- **Entrevista, Plazo y Otro de una candidatura descartada o cerrada:** no se tocan, porque el historial solo puede apuntar un recordatorio cerrado y el «Deshacer» no sabría reabrir varios. Lo que se hace es que **el panel no los enseña** mientras la candidatura siga descartada o cerrada. En la ficha siguen saliendo, con una línea: «No sale en Hoy porque la candidatura está descartada». Si se reabre o se deshace el descarte, vuelven solos a Hoy: no hay nada que restaurar.
- **Borrar una candidatura:** borra sus recordatorios en cascada, como ya pasa.
- **Borrar a mano un seguimiento que apunta el historial:** la clave ajena tiene `SetNull`, así que el «Deshacer» posterior no encuentra nada que quitar y no falla. Hay test.

### 3.5 Migración `RecordatoriosHoy`

```
dotnet ef migrations add RecordatoriosHoy --project src/RadarEmpleo.Data --startup-project src/RadarEmpleo.Api --output-dir Migraciones
```

- `AddColumn<DateTime?>("FechaHecho", "Recordatorios")`.
- Los tokens de concurrencia solo cambian el snapshot.
- Datos, con `migrationBuilder.Sql`: se da por hecho todo seguimiento pendiente de una candidatura que ya está en Entrevista u Oferta.
  ```sql
  UPDATE "Recordatorios" SET "Hecho" = 1
  WHERE "Tipo" = 'Seguimiento' AND "Hecho" = 0
    AND "CandidaturaId" IN (SELECT "Id" FROM "Candidaturas" WHERE "Estado" IN ('Entrevista', 'Oferta'));
  ```
  Se deja `FechaHecho` en NULL, porque no se sabe cuándo pasó (no se inventa). `Down` solo quita la columna. Cerrar esos seguimientos no se puede deshacer, y así lo dice un comentario en la migración.

## 4. API

Se siguen las convenciones de la API actual:
- Errores con ProblemDetails y el mensaje en español en `detail`. `ValidacionException` da 400, con el campo en `errors`. `ReglaNegocioException` y la concurrencia dan 409.
- Enums solo como texto: `{"tipo": 3}` da 400.
- `[Consumes("application/json")]` y `415` en todo POST o PUT con cuerpo, así que un formulario de otra web no cambia nada.
- `AllowedHosts` no se toca.
- Los controladores son finos: devuelven `null`/`false` → 404, como en `CandidaturasController`.

### 4.1 Contratos (`src/RadarEmpleo.Services/Contratos/Recordatorios.cs`, nuevo)

```csharp
// Mismo cuerpo para crear (suelto o de una candidatura) y editar. La candidatura va en la URL, nunca en el cuerpo.
public sealed record GuardarRecordatorioRequest(
    [Required(ErrorMessage = "Elige qué tipo de recordatorio es.")] TipoRecordatorio? Tipo,
    [Required(ErrorMessage = "El título es obligatorio."), MaxLength(200, ErrorMessage = "...")] string Titulo,
    [Required(ErrorMessage = "El día es obligatorio.")] DateOnly? Dia,      // "2026-11-11", día de Madrid
    string? Hora = null,                                                    // "15:00" de Madrid; vacía = 09:00
    [MaxLength(2000, ErrorMessage = "...")] string? Notas = null);

public sealed record MarcarHechoRequest([Required(ErrorMessage = "Falta decir si está hecho.")] bool? Hecho);
public sealed record PosponerRecordatorioRequest([Required(ErrorMessage = "Falta el día.")] DateOnly? Dia);

public sealed record CandidaturaBreveDto(int Id, string Empresa, string Puesto, EstadoCandidatura Estado);
public sealed record RecordatorioAgendaDto(int Id, int? CandidaturaId, TipoRecordatorio Tipo, string Titulo,
    DateTime FechaHora, bool Hecho, DateTime? FechaHecho, string? Notas, CandidaturaBreveDto? Candidatura);
public sealed record EnviadaSinRespuestaDto(int Id, string Empresa, string Puesto, DateTime FechaEnvio, int DiasDesdeEnvio);
public sealed record PanelHoyDto(
    DateOnly Hoy,                                    // día de Madrid según el servidor: el front no lo calcula
    IReadOnlyList<RecordatorioAgendaDto> ParaHoy,     // pendientes con FechaHora < mañana 00:00 Madrid (vencidos incluidos), por fecha
    IReadOnlyList<RecordatorioAgendaDto> Proximos,    // de mañana 00:00 a hoy+8 00:00 (los 7 días siguientes)
    IReadOnlyList<RecordatorioAgendaDto> MasAdelante, // a partir de hoy+8
    IReadOnlyList<RecordatorioAgendaDto> HechosHoy,   // FechaHecho dentro de hoy (Madrid), el más reciente primero
    IReadOnlyList<EnviadaSinRespuestaDto> SinRespuesta,
    int SinRespuestaAntiguas);                        // ver apartado 9, decisión 2
```

- `RecordatorioDto` (en `Contratos/Candidaturas.cs`) gana `DateTime? FechaHecho`, y `CandidaturasService.ADetalle` la rellena.
- `Hora` va como texto «HH:mm» y no como `TimeOnly`, porque System.Text.Json espera «15:00:00» y `<input type="time">` da «15:00». La valida `ReglasRecordatorio.LeerHora` con el campo `hora`.
- `Dia` fuera de 2000-2100 da 400 (`dia`), como en la importación.

**Reglas del panel.** Los cortes de día se calculan con `Fechas.MedianocheMadridEnUtc` sobre el día de Madrid de `IClock.AhoraUtc`, nunca sumando 24 h.
- Las tres listas de pendientes **excluyen** los recordatorios de candidaturas en Descartada o Cerrada.
- `SinRespuesta` reúne las candidaturas en **Enviada** sin ningún seguimiento pendiente, y con `FechaEnvio` de hace entre 7 y 30 días naturales de Madrid. Van de la más reciente a la más antigua.
- `SinRespuestaAntiguas` cuenta las de más de 30 días.

### 4.2 Endpoints

| Método y ruta | Cuerpo | Respuestas |
|---|---|---|
| `GET /api/panel/hoy` | — | 200 `PanelHoyDto` |
| `POST /api/recordatorios` (suelto) | `GuardarRecordatorioRequest` | 201 `RecordatorioDto` + `Location: /api/recordatorios/{id}` · 400 · 415 |
| `POST /api/candidaturas/{id}/recordatorios` | `GuardarRecordatorioRequest` | 201 · 400 · 404 (no existe la candidatura) · 415. La ruta copia la de `POST /candidaturas/{id}/notas` y va en `CandidaturasController` |
| `GET /api/recordatorios/{id}` | — | 200 `RecordatorioDto` · 404 (sirve para la `Location`) |
| `PUT /api/recordatorios/{id}` | `GuardarRecordatorioRequest` | 200 · 400 · 404 · 409 (concurrencia) · 415. No cambia `Hecho` ni la candidatura |
| `POST /api/recordatorios/{id}/hecho` | `{"hecho": true}` o `{"hecho": false}` | 200 `RecordatorioDto` · 400 (`{}`) · 404 · 409 (concurrencia) · 415. Es idempotente |
| `POST /api/recordatorios/{id}/posponer` | `{"dia": "2026-11-14"}` | 200 · 400 · 404 · 409 (hecho: «Ya está hecho. Márcalo como pendiente para moverlo.», o concurrencia) · 415. Se queda con la misma hora de Madrid. Admite días pasados, porque así el «Deshacer» del front devuelve el día original |
| `DELETE /api/recordatorios/{id}` | — | 204 · 404 (también si otra petición lo acaba de borrar) |
| `GET /api/estadisticas` (F6b) | — | 200 `EstadisticasDto` |

Crear o editar un recordatorio **no** toca `Candidatura.FechaUltimoCambio` ni regenera `radar\candidaturas.md`, porque en el radar no salen recordatorios.

### 4.3 Estadísticas (F6b): `Contratos/Estadisticas.cs` y `Services/Estadisticas/`

Hay un solo endpoint y no tres como en `PLAN.md` §7: la página las pinta juntas y así se hace una sola petición. El servicio solo lee: proyecta `Candidaturas` (Id, Estado, FechaEnvio, MotivoDescarte) y `CambiosEstado` (CandidaturaId, A, Fecha), y se lo pasa a `CalculadoraEstadisticas`, que es una clase pura y estática con tests.

```csharp
public sealed record EstadisticasDto(
    int Total,
    IReadOnlyList<TotalPorEstadoDto> PorEstado,         // los 7 estados, también con 0, en el orden del tablero
    IReadOnlyList<EnviadasSemanaDto> EnviadasPorSemana, // 8 semanas (lunes a domingo de Madrid), la actual la última, con ceros
    RespuestaDto Respuesta,
    IReadOnlyList<TotalPorMotivoDto> MotivosDescarte);  // solo las descartadas ahora, motivos con algún caso, de más a menos
public sealed record EnviadasSemanaDto(DateOnly Lunes, int Total);
public sealed record RespuestaDto(int Enviadas, int ConRespuesta, int? Porcentaje, double? DiasMedios, int CasosDias);
```

Definiciones, sin inventar datos. Van tal cual en los `/// <summary>` y en la letra pequeña de la página:
- **Enviadas por semana:** cada apunte del historial que pasa a Enviada cuenta en la semana de Madrid en que se hizo, así que un reenvío tras reabrir cuenta otra vez. Las importadas que ya estaban en Entrevista no tienen ese apunte y no cuentan.
- **Tasa de respuesta:** `ConRespuesta` son las candidaturas con algún paso a Entrevista u Oferta en el historial, aunque luego se descartaran. `Enviadas` son las candidaturas con `FechaEnvio`. `Porcentaje` es null si `Enviadas` = 0, y se redondea a entero.
- **Días medios hasta la respuesta:** solo cuentan las candidaturas en las que el primer paso a Entrevista u Oferta va **después** de un paso a Enviada. Se mide en días naturales de Madrid entre el último Enviada anterior y ese paso. Las importadas que saltan directamente a Entrevista no cuentan, porque su fecha de envío es la de la entrevista. Es null si no hay casos. `CasosDias` dice sobre cuántas se calcula.

## 5. Angular

Se siguen las convenciones del proyecto:
- Carpeta por funcionalidad, con páginas `export default` cargadas en diferido.
- Servicios con `signal` privado y `asReadonly()`, y operaciones que devuelven `Observable` con `conErrorApi()`.
- Funciones puras en `utils/` con su `.spec.ts`.
- Fechas en pantalla con `fechaMadrid`.
- `floatLabel="always"` en los campos.
- Zonas de toque de 48 px (`min-h-12`), y solo tokens de color.

### 5.1 Datos y estado

- **`web/src/app/recordatorios/services/recordatorios.service.ts`** (nuevo) tiene el panel en signals (`panel`, `cargando`, `error`) y todas las operaciones: `cargarPanel()`, `crear(datos, candidaturaId?)`, `editar`, `marcarHecho(r, hecho)`, `posponer(r, dia)` y `borrar`.
  - Si una operación sale bien, el servicio refresca el panel sin poner «cargando» si ya estaba cargado (como `refrescarLista`). Si el recordatorio es de la ficha abierta, también la pone al día con dos métodos públicos nuevos de `CandidaturasService`: `ponerRecordatorioEnDetalle(r)` y `quitarRecordatorioDeDetalle(id, candidaturaId)`, que se apoyan en el `actualizarDetalle` que ya existe.
  - Admite una operación a la vez por recordatorio y devuelve `false` si llega otra mientras tanto, como `CambioEstadoService.deshacer`. Así el doble clic y Ctrl+Z no mandan dos.
  - Si la respuesta es 409 o 404, vuelve a pedir el panel.
- **Interfaces:**
  - `Recordatorio` (en `candidaturas/interfaces/candidatura.interface.ts`) gana `fechaHecho: string | null`. No se mueve de sitio, para no tocar más archivos.
  - `recordatorios/interfaces/recordatorio.interface.ts` (nuevo) tiene `GuardarRecordatorio` (`tipo`, `titulo`, `dia: 'AAAA-MM-DD'`, `hora: 'HH:mm' | null` y `notas`).
  - `hoy/interfaces/panel-hoy.interface.ts` (nuevo) tiene `PanelHoy`, `RecordatorioAgenda`, `CandidaturaBreve` y `EnviadaSinRespuesta`.
- **Utilidades puras:**
  - `shared/utils/fechas-madrid.ts` gana `horaMadrid(fecha) → '15:00'` y `sumarDias('2026-11-13', 1) → '2026-11-14'`. Se calcula con fechas de calendario, sin depender del navegador.
  - `recordatorios/utils/titulo-por-defecto.ts` devuelve, según el tipo y la candidatura:
    - «Publicar en LinkedIn»;
    - «Entrevista en {empresa}»;
    - «Preguntar cómo va {empresa}»;
    - «Plazo de {empresa}»;
    - con Otro, el título va vacío.
  - `recordatorios/utils/opciones-posponer.ts` da las opciones «Mañana», «Dentro de 3 días» y «Dentro de una semana», con el día que toca a partir de `panel.hoy`.
  - `recordatorios/utils/recordatorio-de-prueba.ts` es un dato de ejemplo para los specs, como `candidatura-de-prueba.ts`.

### 5.2 Componentes

- **`recordatorios/components/tarjeta-recordatorio/`** (nuevo) se usa en Hoy y en la ficha. Sus entradas son `recordatorio`, `hoy` (el día del panel), `destacada` (si «Hecho» va relleno), `conPosponer` y `conCandidatura`. Emite `hecho`, `pendiente`, `posponer(dia)`, `editar` y `borrar`. Por dentro:
  - Arriba van la insignia del tipo (icono y texto, `insignia` en gris) y la hora en monoespaciada. Si está vencido, una insignia «Vencido · 10 nov» en humo, siempre con texto.
  - El título. Si es de una candidatura, el título es un enlace a la ficha de 48 px de alto, y debajo van la empresa y la insignia de estado.
  - Las notas («Por Teams»), si las hay.
  - Las acciones:
    - **«Hecho»**: relleno solo si `destacada`; si no, con borde. En los hechos, pasa a «Pendiente otra vez».
    - **«Posponer ▾»**: un `mat-menu` con las tres opciones y «Cambiar fecha u hora…».
    - **«⋮»**: un `mat-menu` con «Editar» y «Borrar». Lleva un `aria-label` con el título, por ejemplo «Más acciones: Entrevista en Consultora Ejemplo».
  - Hay un solo marco por nivel: la tarjeta lleva `rounded-md border-2 border-trazo bg-surface-container-lowest shadow-dura-sm` y lo de dentro no lleva borde.
- **`recordatorios/components/dialogo-recordatorio/`** (nuevo) sirve para dar de alta y para editar. Recibe `{ recordatorio?, candidatura?: CandidaturaBreve, tipoInicial }`. Los campos van en este orden:
  1. **Tipo:** un `mat-radio-group` de 5 opciones en dos columnas, con filas de 48 px. Por defecto, en Hoy es «Publicación en LinkedIn», y en la ficha «Entrevista» si la candidatura está en Entrevista y «Seguimiento» si no.
  2. **Título:** viene relleno con `tituloPorDefecto`. Si no se ha tocado, cambia al cambiar el tipo.
  3. **Día:** `<input matInput type="date">`, obligatorio.
  4. **Hora:** `<input matInput type="time">`, que empieza en 09:00.
  5. **Notas:** un textarea opcional.

  Si es de una candidatura, se ve como una línea de solo lectura: «Para: {puesto} · {empresa}». Abajo van «Guardar recordatorio» (relleno) y «Cancelar». Reutiliza `intentosDeCerrar` y `confirmarSalirSinGuardar` (si hay algo escrito, pregunta antes de cerrar), `obligatorio`, `mensajeError` y `aplicarErroresServidor`, como `DialogoCandidatura`. Al guardar sale el aviso «Recordatorio guardado para el 17 nov a las 09:00.».
- **Ficha** (`ficha-page.component.html` y `.ts`): el bloque «Recordatorios» deja su lista propia y usa `app-tarjeta-recordatorio`, sin `destacada` (el relleno de la ficha es el del cambio de estado) y sin posponer. Además:
  - Gana un botón con borde, «Añadir recordatorio». Se oculta si la candidatura está descartada o cerrada.
  - Gana la línea «No sale en Hoy porque la candidatura está descartada/cerrada» cuando hay recordatorios pendientes en ese caso.
  - Se mantiene el texto vacío actual.
  - «Borrar» pide confirmación con `confirmar(...)`, como las notas.

### 5.3 Pantalla Hoy (`hoy/pages/hoy-page/`)

Va de arriba abajo:
1. La marca en el móvil y la cabecera, como ahora. Al lado de `app-estado-api` va un botón con borde, «Nuevo recordatorio», que abre el diálogo con un recordatorio suelto.
2. `<!-- Hueco F8-F11: «Esperan tu OK» va aquí, encima de todo. Ahora no se pinta nada. -->`. No hay componente, ni campo en el DTO, ni texto.
3. **«Para hoy · 3»** (`section` con `h2`): lo vencido y lo de hoy, con posponer. **El «Hecho» del primero es el único botón relleno de la pantalla.** Si está vacía pero hay algo más, sale una línea: «Nada para hoy. Lo siguiente: lun 17 nov.».
4. **«Próximos 7 días · 2»**: sin posponer. Para moverlos está «Cambiar fecha u hora…» en el menú ⋮. Si está vacía, sale «Nada en los próximos 7 días.».
5. **«Enviadas sin respuesta · 4»**: va en `app-seccion-plegable`, con el aspecto «suave», plegada. Cada fila es un enlace a la ficha: «{empresa} · {puesto} · enviada hace 12 días». Debajo, si `sinRespuestaAntiguas > 0`, una línea en monoespaciada: «Y 9 más llevan más de un mes sin respuesta.».
6. **«Más adelante · 5»**: plegada, sin posponer.
7. **«Hechos hoy · 2»**: plegada, con «Pendiente otra vez».

Las secciones 5 a 7 no se pintan si están vacías.

**Estado vacío** (todas las listas vacías y `sinRespuestaAntiguas` = 0): la taza de ahora, centrada, con «Nada pendiente para hoy.» y «De momento, café tranquilo.». Los dos botones de ahora siguen igual, con «Buscar nuevas ofertas» relleno. En este caso el relleno es ese y no hay ningún «Hecho».

**Carga y error:** la barra de progreso de la ficha. El error va con `role="alert"` y «Reintentar», con el mismo patrón que la ficha.

**Flujos** (pocos toques):
- **Hecho:** 1 toque. Sale el aviso «Hecho: «{título}».» con «Deshacer» (8 s y Ctrl+Z, `avisos.deshacible`), que manda `hecho:false`. El foco pasa al «Hecho» del siguiente o, si no queda ninguno, al `h2` de la sección (`tabindex="-1"`).
- **Posponer:** 2 toques, «Posponer» y «Mañana». Sale el aviso «Pospuesto al vie 14 nov.» con «Deshacer», que manda `posponer` con el día original.
- **Apuntar un post de LinkedIn:** «Nuevo recordatorio», el día (el tipo y el título ya vienen) y «Guardar»: 3-4 toques.
- **Apuntar la entrevista** desde la ficha: «Añadir recordatorio», el tipo ya es Entrevista, el día, la hora y «Guardar».
- **Ir a la ficha:** 1 toque en el título.

**Al cambiar el día con la pestaña abierta:** un `effect` sobre `diaMadrid(fechaActual.ahora())` vuelve a pedir el panel cuando cambia el día (`FechaActualService` ya se actualiza al volver a la pestaña).

### 5.4 Estadísticas, Ajustes, Más y menú (F6b)

- **`estadisticas/`** (nuevo) tiene:
  - `pages/estadisticas-page`, una página de lectura con `max-w-3xl`;
  - `services/estadisticas.service.ts`;
  - `interfaces/estadisticas.interface.ts`;
  - `components/lista-barras`, que es reutilizable;
  - `utils/barras.ts`, para el ancho en % y la etiqueta «Semana del 2 nov».
- **Gráfico accesible (`lista-barras`):** un `<ul>`. Cada `<li>` lleva la etiqueta, una barra decorativa (`aria-hidden`, con el ancho proporcional al máximo) y el número **visible** en monoespaciada. El lector de pantalla lee «Enviada, 12», y nada depende del color.
  - Por estado: la barra va en `bg-estado` con `data-estado`.
  - Por semana: en el color de Enviada.
  - Motivos: en `bg-surface-container-high`.
  - No lleva relleno lima, porque esta página no tiene acción principal.
- **Contenido de la página:** cuatro secciones.
  - «Tus candidaturas», con el total y las barras por estado.
  - «Enviadas por semana».
  - «Respuestas»: «1 de 4 (25 %) han llegado a entrevista u oferta» y «De media tardan 9 días en responder (calculado con 3)». Con 0 o 1 días se dice «el mismo día» o «1 día».
  - «Por qué se descartan».

  Sin datos, cada sección dice «Todavía no hay datos para esto.». La letra pequeña dice «Solo cuenta lo que está en la app.» y explica las dos exclusiones del apartado 4.3. La página entera sin candidaturas muestra un estado vacío con «Ir al tablero» (con borde).
- **`ajustes/pages/ajustes-page`** (nuevo, `max-w-3xl`) tiene un panel «Esta app» con el modo (Local o Demo), la versión y los agentes («Apagados»), sacados de `SistemaService.info()` con `cargarInfoSiFalta()`. Debajo va una línea en monoespaciada: «Aquí llegarán el envío manual o automático y el tope de gasto diario.». No tiene botones.
- **Más:** las tarjetas pasan a un componente `mas/components/tarjeta-enlace`, con entradas `ruta`, `titulo` y `descripcion`, y el icono por `ng-content`. Así no se repiten cuatro veces las 40 líneas de clases. Quedan cuatro tarjetas:
  - Perfil y filtros;
  - Estadísticas («Cómo va la búsqueda: por estado, por semana y respuestas»);
  - Importar del registro;
  - Ajustes.

  La línea final pasa a decir «Los recordatorios y lo del día están en Hoy.». La descripción de la cabecera queda en «Perfil, estadísticas, importación y ajustes.».
- **Rutas:** `estadisticas` («Estadísticas · RadarEmpleo») y `ajustes` («Ajustes · RadarEmpleo») en `app.routes.ts`.
- **`opciones-menu.ts`:** «Más» queda con `subrutas: ['/importacion', '/perfil', '/estadisticas', '/ajustes']` y la descripción «Perfil, estadísticas y ajustes».
- **«Apuntar la entrevista»** (`cambio-estado.service.ts`): cuando el destino es Entrevista, la acción secundaria del aviso pasa de «Añadir nota» a «Apuntar la entrevista». Lleva a la ficha y abre `DialogoRecordatorio` con el tipo Entrevista y la candidatura. «Deshacer» sigue igual.

## 6. Archivos

**F6a: backend**
- `src/RadarEmpleo.Domain/Candidaturas.cs`: `Recordatorio` gana `FechaHecho` y sus métodos; `CambiarEstado` cierra el seguimiento en Entrevista y Oferta; `DeshacerUltimoCambio` usa `MarcarPendiente`.
- `src/RadarEmpleo.Domain/ReglasRecordatorio.cs` (nuevo): hora por defecto, conversión de día y hora de Madrid, posponer y leer la hora.
- `src/RadarEmpleo.Data/AppDbContext.cs`: tokens de concurrencia `Hecho` y `FechaHora`.
- `src/RadarEmpleo.Data/Migraciones/*_RecordatoriosHoy.cs`, su `.Designer.cs` y el snapshot (generados), más el `UPDATE` de datos.
- `src/RadarEmpleo.Services/Contratos/Recordatorios.cs` (nuevo): peticiones y DTO del panel.
- `src/RadarEmpleo.Services/Contratos/Candidaturas.cs`: `RecordatorioDto` gana `FechaHecho`.
- `src/RadarEmpleo.Services/RecordatoriosService.cs` (nuevo): crear, editar, hecho, posponer, borrar y `PanelHoyAsync`.
- `src/RadarEmpleo.Services/CandidaturasService.cs`: `ADetalle` con `FechaHecho`.
- `src/RadarEmpleo.Api/Controllers/RecordatoriosController.cs` y `PanelController.cs` (nuevos).
- `src/RadarEmpleo.Api/Controllers/CandidaturasController.cs`: `POST {id}/recordatorios` y los comentarios de `CambiarEstado`.
- `src/RadarEmpleo.Api/Program.cs`: `AddScoped<RecordatoriosService>()`.

**F6a: front**
- `web/src/app/recordatorios/` (nuevo): `services/recordatorios.service.ts`, `components/tarjeta-recordatorio/`, `components/dialogo-recordatorio/`, `interfaces/recordatorio.interface.ts`, `utils/titulo-por-defecto.ts`, `utils/opciones-posponer.ts` y `utils/recordatorio-de-prueba.ts`.
- `web/src/app/hoy/interfaces/panel-hoy.interface.ts` (nuevo), `hoy/pages/hoy-page/*` (reescrita) y `hoy-page.component.spec.ts` (nuevo).
- `web/src/app/candidaturas/interfaces/candidatura.interface.ts`: `fechaHecho`.
- `web/src/app/candidaturas/services/candidaturas.service.ts`: los dos métodos públicos para poner al día los recordatorios de la ficha.
- `web/src/app/candidaturas/pages/ficha-page/*`: el bloque Recordatorios con la tarjeta, «Añadir recordatorio» y la línea de descartada.
- `web/src/app/shared/utils/fechas-madrid.ts`: `horaMadrid` y `sumarDias`.
- `web/src/app/app.routes.spec.ts`: al entrar en Hoy ahora también se pide `/api/panel/hoy`.

**F6b**
- `src/RadarEmpleo.Services/Contratos/Estadisticas.cs`, `Services/Estadisticas/CalculadoraEstadisticas.cs` y `EstadisticasService.cs` (nuevos); `Api/Controllers/EstadisticasController.cs` (nuevo); `Program.cs`.
- `web/src/app/estadisticas/` y `web/src/app/ajustes/` (nuevos); `mas/components/tarjeta-enlace/` (nuevo); `mas/pages/mas-page/*`; `app.routes.ts`; `layout/utils/opciones-menu.ts` y su spec; `candidaturas/services/cambio-estado.service.ts` y su spec.
- Documentación:
  - `docs/DISENO.md`: Hoy, tarjeta de recordatorio, estados vacíos, Más, Estadísticas y Ajustes.
  - `docs/PLAN.md` §11: marcar como hecho «Seguimiento al pasar a Entrevista u Oferta».
  - `README.md`: el apartado «Estado».

## 7. Pasos (en orden)

**F6a**
1. *(Desarrollador)* Dominio: `ReglasRecordatorio`, los métodos de `Recordatorio` y el cambio de `CambiarEstado` y `DeshacerUltimoCambio`. Primero los tests de dominio del apartado 8.
2. *(Desarrollador)* `AppDbContext` (tokens) y la migración `RecordatoriosHoy`, con el `UPDATE` y su test de migración. Comprobar que el snapshot solo cambia lo esperado.
3. *(Desarrollador)* Contratos, `RecordatoriosService` (con la captura de la concurrencia) y `PanelHoyAsync`, con tests de servicio con `RelojFijo`.
4. *(Desarrollador)* Controladores, `POST /candidaturas/{id}/recordatorios` y `Program.cs`, con tests de API (400, 404, 409 y 415, y `Location`).
5. *(Desarrollador)* Front: interfaces, `fechas-madrid` y utilidades con sus specs; `RecordatoriosService` y los dos métodos de `CandidaturasService`.
6. *(Desarrollador)* `DialogoRecordatorio` y `TarjetaRecordatorio`, primero solo con la lógica y la accesibilidad (roles, `aria-label`, foco y 48 px).
7. *(Desarrollador)* La página Hoy: secciones, flujos, avisos con «Deshacer», foco y recarga al cambiar el día. Después, la ficha. Arreglar `app.routes.spec.ts`.
8. *(Diseñador)* Aspecto de la tarjeta de recordatorio:
   - iconos por tipo, con trazo editorial de 20×20: sobre de vuelta, dos bocadillos, megáfono, reloj de arena y chincheta (sin el logotipo de LinkedIn);
   - la insignia «Vencido» en humo;
   - el espaciado de las secciones de Hoy;
   - el texto del estado vacío;
   - comprobar que en Hoy hay un solo relleno lima, de día y de noche.

   Actualizar en `docs/DISENO.md` la fila «Hoy» de los estados vacíos y añadir una sección «Hoy y recordatorios».
9. *(Desarrollador)* `dotnet test`, `ng test` y `ng build`, y la prueba en vivo del apartado 10 (F6a).

**F6b**
10. *(Desarrollador)* `CalculadoraEstadisticas`, que es pura, con sus tests; después el servicio, el controlador y el test de API.
11. *(Desarrollador)* Front de Estadísticas (servicio, `lista-barras` y página), Ajustes, `tarjeta-enlace`, Más, rutas y menú.
12. *(Desarrollador)* «Apuntar la entrevista» en `CambioEstadoService`, con su spec.
13. *(Diseñador)* Estadísticas (barras, cifras y letra pequeña), Ajustes, las cuatro tarjetas de Más y su línea final. Actualizar «Más» y «Maquetación de página» en `DISENO.md`: Estadísticas y Ajustes son páginas de lectura de `max-w-3xl`.
14. *(Desarrollador)* La documentación del apartado 6, los tests y la prueba en vivo del apartado 10 (F6b).

## 8. Tests

**.NET** (se mantienen los 997; los nombres siguen la forma `Metodo_Situacion_Resultado`)
- **Dominio: `ReglasRecordatorioTests` y `CandidaturaTests` / `DeshacerCambioEstadoTests`**
  - `FechaHoraUtc`:
    - 11/11/2026 a las 15:00 da `14:00Z`;
    - sin hora, 09:00 de Madrid;
    - 29/03/2026 a las 09:00 da `07:00Z`;
    - 25/10/2026 a las 09:00 da `08:00Z`.
  - `LeerHora`: rechaza «25:00», «9h», «15:00:00» y «abc» (campo `hora`); acepta «09:05».
  - `PosponerA`: conserva la hora de Madrid al cruzar el cambio de hora (de 23/10 a las 15:00 al 27/10 a las 15:00, que da `14:00Z`). Si está hecho, lanza `ReglaNegocioException`.
  - `MarcarHecho`: es idempotente (la segunda vez no cambia `FechaHecho`). `MarcarPendiente` borra `FechaHecho`.
  - `CambiarEstado`:
    - de Enviada a Entrevista cierra el seguimiento pendiente (`FechaHecho` = ahora) y lo apunta en `SeguimientoCerrado`;
    - de Enviada a Oferta, lo mismo;
    - de Entrevista a Oferta, sin nada pendiente, no toca nada;
    - de Preparada a Entrevista rellena `FechaEnvio` y no crea ni cierra nada;
    - descartar cierra el seguimiento pero no una Entrevista ni un Plazo pendientes.
  - `Deshacer` de Entrevista: el seguimiento vuelve a estar pendiente con `FechaHecho` null, y `FechaEnvio` se recalcula.
- **Migración: `MigracionRecordatoriosHoyTests`**, con el patrón de `MigracionHistorialParaDeshacerTests` y la migración anterior `20261113083513_SeguimientosEnHistorial`. Tras migrar, solo los seguimientos pendientes de Entrevista y Oferta quedan hechos, con `FechaHecho` NULL. Siguen igual:
  - el seguimiento de una Enviada;
  - un Plazo de una Entrevista;
  - un recordatorio suelto.
- **Servicio: `RecordatoriosServiceTests`** (arnés SQLite y `RelojFijo` el 13/11/2026 a las 10:00 de Madrid)
  - Crear:
    - suelto, de LinkedIn;
    - de una candidatura;
    - si la candidatura no existe, devuelve null (404);
    - con título en blanco, tipo 99, `dia` del año 1999 o una hora mala, `ValidacionException` con su campo.
  - Editar cambia el título, la fecha y las notas, pero no `Hecho` ni la candidatura; si no existe, null.
  - Hecho y pendiente; posponer; posponer algo hecho da 409.
  - Borrar: true, y false si no existe. Borrar un seguimiento que apunta el historial y luego deshacer ese cambio no falla.
  - Concurrencia: con `AntesDeGuardar`, si otra petición lo marca como hecho entre la lectura y el guardado, salta `ReglaNegocioException` con el mensaje del recordatorio.
- **Panel: `PanelHoyTests`**
  - Bordes del día: el 10/11 va a `ParaHoy`; el 13/11 a las 23:30 de Madrid (`22:30Z`) también; el 14/11 a las 00:00 de Madrid va a `Proximos`; el 20/11 a las 23:59, a `Proximos`; el 21/11 a las 00:00, a `MasAdelante`.
  - Hechos: los hechos no salen en las pendientes; lo hecho hoy sale en `HechosHoy` y lo hecho ayer no.
  - Candidaturas descartadas o cerradas: sus recordatorios no salen; los de una Preparada sí; lo ligado trae `Candidatura` (empresa, puesto y estado).
  - `SinRespuesta`:
    - una Enviada de hace 8 días sin seguimiento sale;
    - con seguimiento pendiente, no;
    - de hace 6 días, no;
    - de hace 31 días, solo cuenta en `SinRespuestaAntiguas`;
    - una Entrevista, no;
    - el orden es de la más reciente a la más antigua.
  - Un día de cambio de hora (reloj el 25/10/2026 a las 00:30Z): `Hoy` es 25/10 y los cortes son los de Madrid.
- **API: `RecordatoriosApiTests` y `PanelApiTests`** (`FabricaApi` con `RelojFijo`, como `DeshacerApiTests`)
  - POST da 201 con `Location` y `fechaHora` en UTC con «Z».
  - `{}` da 400 con `errors.tipo`, `errors.titulo` y `errors.dia` en español; `"tipo": 3` da 400; `"hora": "25:00"` da 400 con `errors.hora`.
  - 415 con `application/x-www-form-urlencoded` y con `text/plain`, en POST de suelto y de candidatura, PUT, `hecho` y `posponer`. En todos se comprueba después que no ha cambiado nada.
  - 404 en GET, PUT, `hecho`, `posponer` y DELETE de un id que no existe, y en `POST /candidaturas/999/recordatorios`.
  - 409 al posponer algo hecho; DELETE da 204.
  - De principio a fin: crear una candidatura, pasarla a Enviada, avanzar el reloj 7 días y el seguimiento sale en `paraHoy`. Al pasar a Entrevista desaparece y su `fechaHecho` queda rellena; tras `estado/deshacer`, vuelve.
  - En `ApiTests`, el `HostQueNoEsLocal_Devuelve400ContraElDnsRebinding` también con `/api/panel/hoy`.
- **Estadísticas (F6b): `CalculadoraEstadisticasTests` y `EstadisticasApiTests`**
  - Por estado: los 7, con ceros, y el total.
  - Por semana: 8 lunes de Madrid con ceros. Un envío el domingo a las 23:30 de Madrid cuenta en esa semana; un reenvío tras reabrir cuenta dos veces; una Entrevista importada sin Enviada no cuenta.
  - Tasa: 0 enviadas da `Porcentaje` null; con 4 enviadas y 1 con Entrevista (que luego se descartó), sale 1 de 4 y 25.
  - Días medios: solo cuentan los casos con Enviada anterior, en días naturales de Madrid. Sin casos, null y `CasosDias` 0.
  - Motivos: solo las descartadas ahora, de más a menos.
  - API: 200 y la forma del JSON.

**Angular (Vitest; se mantienen los 599)**
- **Utilidades:**
  - `horaMadrid` y `sumarDias`: fin de mes, de año y cambio de hora;
  - `tituloPorDefecto`, con y sin candidatura;
  - `opcionesPosponer`;
  - `barras` (F6b): ancho con máximo 0, y la etiqueta de la semana.
- **`RecordatoriosService`:**
  - URL y cuerpo de cada operación (`dia` «AAAA-MM-DD», `hora` «HH:mm» o null);
  - refresca el panel sin «cargando»;
  - pone al día la ficha abierta, y no otra;
  - no deja la segunda operación sobre el mismo id mientras va la primera;
  - con 409 o 404, vuelve a pedir el panel.
- **`DialogoRecordatorio`:**
  - el tipo por defecto según de dónde se abre;
  - el título se rellena, y cambia con el tipo solo si no se ha tocado;
  - la hora empieza en 09:00 y el día es obligatorio;
  - los errores del servidor van a sus campos;
  - si hay algo escrito, pregunta antes de cerrar;
  - al editar, el día y la hora salen en hora de Madrid.
- **`TarjetaRecordatorio`:**
  - la insignia «Vencido» solo en lo pasado;
  - el enlace a la ficha solo si es de una candidatura;
  - los `aria-label` llevan el título;
  - «Hecho» va relleno solo con `destacada`;
  - en los hechos sale «Pendiente otra vez»;
  - el menú de posponer manda el día correcto.
- **`HoyPage`:**
  - carga, error y «Reintentar»;
  - el estado vacío completo (taza y dos botones);
  - las secciones con sus totales, y las plegadas que no se pintan si están vacías;
  - **un solo `mat-flat-button`** en cada caso;
  - «Hecho»: aviso con «Deshacer», que manda `hecho:false`, y el foco va al siguiente;
  - «Posponer» y «Mañana» mandan el día de mañana según `panel.hoy`;
  - no hay ningún texto «Esperan tu OK»;
  - vuelve a pedir el panel al cambiar el día.
- **Ficha:**
  - «Añadir recordatorio» abre el diálogo con la candidatura, y no sale si la candidatura está descartada;
  - marcar como hecho pone al día la lista;
  - sale la línea «No sale en Hoy…».
- **F6b:**
  - `EstadisticasPage`: las cifras se ven como texto; los textos de «Todavía no hay datos»; «el mismo día»; ningún relleno lima.
  - `AjustesPage`: el modo y la versión, sin botones.
  - `MasPage`: cuatro enlaces con sus rutas.
  - `opciones-menu.spec`: `/estadisticas` y `/ajustes` cuentan como Más.
  - `app.routes.spec`: los títulos de las dos rutas.
  - `cambio-estado.service.spec`: Entrevista ofrece «Apuntar la entrevista» y otros destinos, «Añadir nota».

## 9. Riesgos y decisiones de Eduardo

**Decisiones** (se sigue la recomendada si no dice otra cosa)
1. **Al pasar a Entrevista u Oferta:** cerrar el seguimiento pendiente, no crear otro automático y ofrecer «Apuntar la entrevista» en el aviso. **Recomendado.** La alternativa es crear un seguimiento a +7 días tras la entrevista, que sería una fecha inventada.
2. **«Enviadas sin respuesta» en Hoy:** solo las de 7 a 30 días, más una línea «Y N más llevan más de un mes». **Recomendado**, porque con unas 45-50 enviadas del registro la lista entera serían decenas de filas sin acción útil. La alternativa es enseñarlas todas, plegadas.
3. **Estadísticas en su página, desde «Más»**, y Hoy solo con lo que hay que hacer. **Recomendado.**
4. **Posts de LinkedIn:** apuntarlos a mano en la F6. Leer `Marca personal\linkedin\publicaciones_*.md` queda fuera. **Recomendado.**

**Riesgos**
- **La migración cambia datos de la BD real** al primer arranque, porque `Migrate()` se aplica sola: cierra los seguimientos de las candidaturas en Entrevista u Oferta, y no se puede deshacer. **Antes del primer arranque de la F6 con su BD, Eduardo copia `%LOCALAPPDATA%\RadarEmpleo\radar.db`.** El tester nunca la usa.
- **Specs de navegación:** Hoy pide ahora también `/api/panel/hoy`, y `http.verify()` fallará en `app.routes.spec.ts` y en cualquier spec que pase por `/hoy` hasta que respondan esa petición.
- **Los tokens de concurrencia nuevos** cambian el `UPDATE` de los recordatorios también en `CambiarEstado` y en «Deshacer». Hay que pasar la batería entera, sobre todo `DeshacerApiTests` y los de `AntesDeGuardar`.
- **Fechas:** el día y la hora se interpretan siempre en Madrid en el servidor. Los tests con cambio de hora son obligatorios. Si el navegador está en otra zona, `<input type="date">` no se ve afectado, porque manda la fecha como texto.
- **Honestidad de las estadísticas con datos importados:** el registro no trae la fecha de envío de las que ya estaban en Entrevista, y las que avanzan al importar llevan la fecha de hoy (`AvanzarAsync`). Los días medios pueden salir algo altos, y la letra pequeña lo dice.
- **Recordatorios ocultos en Hoy por estar la candidatura descartada o cerrada:** podrían despistar. La ficha lo explica con una línea.
- **Un solo relleno lima:** depende del contenido (el primer «Hecho» o el estado vacío). Un test lo vigila.
- **No hace falta tocar `AllowedHosts`** ni nada del móvil (F12).

## 10. Cómo comprobarlo (tester)

Siempre con una BD y una carpeta temporales; **nunca** `%LOCALAPPDATA%\RadarEmpleo\radar.db`. Los procesos van sin ventanas visibles, como en las fases anteriores. `appsettings.Local.json` apunta a la carpeta real, pero la línea de comandos manda sobre él.

```powershell
$real = "$env:LOCALAPPDATA\RadarEmpleo\radar.db"
$antes = if (Test-Path $real) { (Get-Item $real).LastWriteTimeUtc }
$prueba = Join-Path $env:TEMP "radar-f6-$(Get-Date -Format yyyyMMddHHmmss)"
Copy-Item -Recurse C:\Users\Usuario\Desktop\RadarEmpleo\datos-ejemplo "$prueba\Curriculum"
Rename-Item "$prueba\Curriculum\registro-ejemplo.md" ofertas-enviadas.md
Rename-Item "$prueba\Curriculum\perfil-ejemplo.md" perfil-busqueda.md
dotnet run --project C:\Users\Usuario\Desktop\RadarEmpleo\src\RadarEmpleo.Api -- `
  --RadarEmpleo:RutaBaseDatos="$prueba\radar.db" --RadarEmpleo:CarpetaCurriculum="$prueba\Curriculum"
# Front: en web\, npm start (proxy a :5180) y abrir http://localhost:4200
```

Las fechas que se esperan son las del 13/11/2026. Si se prueba más tarde, lo del 17/11 puede caer ya en «Para hoy».

**F6a: API**
1. `POST /api/recordatorios` con `{"tipo":"PublicacionLinkedIn","titulo":"Publicar en LinkedIn: post de Supabase","dia":"2026-11-03","hora":"09:00"}` da **201**, `Location` y `fechaHora` `2026-11-03T08:00:00Z`. Otro igual para el `2026-11-17` (post de Angular) da `2026-11-17T08:00:00Z`.
2. `GET /api/panel/hoy`: `hoy` es `2026-11-13`; el del 3/11 está en `paraHoy` y el del 17/11 en `proximos`.
3. `POST /api/recordatorios/{id del 3/11}/hecho` con `{"hecho":true}` da 200: pasa a `hechosHoy` con `fechaHecho`. Repetirlo da 200 y `fechaHecho` no cambia.
4. **Seguimiento real:**
   - `POST /api/candidaturas` con `{"empresa":"Consultora Ejemplo","puesto":"Desarrollador .NET"}` y `POST .../estado` con `{"estado":"Enviada"}`. La ficha trae un Seguimiento a las 09:00 de Madrid de dentro de 7 días (`2026-11-20T08:00:00Z`), y en el panel sale en `proximos` con `candidatura.empresa`.
   - `POST /api/recordatorios/{id}/posponer` con `{"dia":"2026-11-13"}` lo pasa a `paraHoy`.
5. **Entrevista y «Deshacer»:**
   - `POST .../estado` con `{"estado":"Entrevista"}`: el seguimiento queda hecho con `fechaHecho` y sale de `paraHoy`.
   - `POST .../estado/deshacer` con el `cambioId` del último apunte: vuelve a estar pendiente y otra vez en `paraHoy`.
   - De nuevo a Entrevista, y `POST /api/candidaturas/{id}/recordatorios` con `{"tipo":"Entrevista","titulo":"Entrevista en Consultora Ejemplo","dia":"2026-11-11","hora":"15:00","notas":"Por Teams"}` da `fechaHora` `2026-11-11T14:00:00Z`.
6. **Descartada:**
   - Con un Plazo pendiente en otra candidatura, descartarla (motivo Zona): el Plazo sale del panel y sigue en la ficha.
   - Reabrirla: el Plazo vuelve.
7. **Errores:**
   - `POST /api/recordatorios` con `Content-Type: text/plain` da **415**; con un formulario, **415**.
   - `{}` da **400**, con `errors.tipo`, `errors.titulo` y `errors.dia` en español.
   - `"hora":"25:00"` da **400** (`errors.hora`).
   - `GET /api/recordatorios/999` da **404** ProblemDetails.
   - Posponer algo hecho da **409** con el mensaje del apartado 4.2.
   - Una petición con `Host: evil.example` da **400**.

**F6a: pantalla** (Chrome a 1366 px y a 390 px; también en modo oscuro; también solo con teclado)
8. **Hoy:**
   - «Para hoy» tiene el seguimiento y lo vencido, con «Vencido · …» en texto.
   - «Próximos 7 días» tiene el post del 17/11.
   - **Solo el primer «Hecho» va relleno.**
   - No sale nada de «Esperan tu OK».
9. **«Hecho»:**
   - Pulsarlo: sale el aviso con «Deshacer», y Ctrl+Z lo devuelve.
   - Pulsarlo otra vez: pasa a «Hechos hoy», y el foco queda en el siguiente «Hecho».
10. **«Posponer» y «Mañana»:**
    - El recordatorio pasa a Próximos con la misma hora.
    - «Deshacer» lo devuelve a su día.
11. **«Nuevo recordatorio»:**
    - El tipo sale como LinkedIn y el título ya relleno.
    - Al cambiar a «Otro», el título se vacía si no se había tocado.
    - Guardar sin día da el error en el campo.
    - Guardar con día sale el aviso con la fecha.
12. **Desde la ficha:**
    - Tocar el título de un recordatorio de una candidatura abre su ficha.
    - En la ficha, «Añadir recordatorio» sale con el tipo Entrevista si la candidatura está en Entrevista.
    - El bloque se pone al día sin recargar.
    - En una descartada no hay botón y sale la línea «No sale en Hoy…».
13. **Accesibilidad:**
    - Todo se maneja con Tab, Intro y Esc, y las flechas en los menús.
    - Las zonas de toque son de 48 px o más a 390 px.
    - No hay desplazamiento lateral a 1366 ni a 390 px.
    - El lector de pantalla (Narrador) lee «Más acciones: {título}».
14. **Importación:** importar la tabla del registro de ejemplo (Más, Importar). Llamamos N al número de Enviadas que salgan en la vista previa (todas son de agosto). Debe salir la línea «Y N más llevan más de un mes sin respuesta». Sin respuesta de entre 7 y 30 días no hay ninguna.

**F6b**
15. **Más:**
    - Salen cuatro tarjetas.
    - **Ajustes** se abre desde «Más», con el modo Local, la versión y «Apagados», sin botones.
    - El menú marca «Más» en `/ajustes` y en `/estadisticas`.
16. **Estadísticas:**
    - Por estado: las cifras cuadran con los totales del tablero.
    - Respuestas: «2 de N+2». Las 2 que llegan a entrevista son la Entrevista importada y la «Consultora Ejemplo»; el total suma además las N Enviadas importadas.
    - Días medios: «el mismo día (calculado con 1)». La Entrevista importada no cuenta porque no tiene Enviada antes.
    - Enviadas por semana: solo la de esta semana tiene 1.
    - Las cifras se leen como texto y no hay ningún relleno lima.
17. **Aviso:** pasar a Entrevista, desde la ficha, una de las Enviadas importadas. El aviso ofrece «Apuntar la entrevista», que abre el diálogo con el tipo Entrevista y la candidatura.

**Siempre al final**
18. Ejecutar `dotnet test RadarEmpleo.slnx` y `cd web; npx ng test --watch=false`: todo en verde, con más de 997 y de 599 tests. `npx ng build` sin avisos de tamaño.
19. Comprobar que `(Get-Item $real).LastWriteTimeUtc` es igual a `$antes`, es decir, que no se ha tocado la BD real. Parar la API y el front, y borrar `$prueba`.

## Cambios aprobados durante la F6a

Lo que se cambió sobre este plan mientras se construía y revisaba la F6a (13-14/11/2026). Manda sobre lo de arriba; el detalle está en `docs/DISENO.md` («Inicio y recordatorios» y «Tablero»).

- **Nombre:** la pantalla «Hoy» pasa a llamarse **Inicio** (decisión de Eduardo del 14/11). La ruta es `/inicio`; `/` y los enlaces viejos a `/hoy` (con `?tipo=`, `?dia=` y `?ver=`) llevan a ella. «Para hoy», «Hechos hoy» y el atajo «Hoy» del diálogo no cambian: hablan del día. Por dentro (carpetas `hoy/`, clases y `/api/panel/hoy`) sigue llamándose «hoy». Donde arriba dice «Hoy» como pantalla, léase «Inicio» (por ejemplo, «No sale en Inicio porque la candidatura está descartada» y «Vuelve a cargar Inicio…»).
- **Un solo relleno: «Nuevo recordatorio».** Ningún «Hecho» va relleno: los de «Para hoy» llevan el contorno con acento (`boton-acento`) y los demás, el normal. En el estado vacío, «Buscar nuevas ofertas» e «Ir al tablero» van con borde. De noche los botones de borde van en tinta clara y solo el acento en lima.
- **Título visible:** en Inicio, si el título guardado de un recordatorio de candidatura es el que se pone solo, la tarjeta enseña solo el puesto; el título entero sigue en los `aria-label`, el detalle, los avisos y la ficha.
- **Filtros:** chips por tipo con su número, filtro por día desde el calendario o «Lo siguiente», y filtro por esta semana desde la cifra de las entrevistas. Van en la URL (`?tipo=`, `?dia=`, `?semana=esta`). Con cualquier filtro no salen las «Enviadas sin respuesta».
- **Calendario clicable:** el resumen deja de ser solo para leer. «Candidaturas activas» lleva al tablero, «Entrevistas esta semana» cuenta las entrevistas **pendientes** de lunes a domingo y filtra esas mismas, y cada día filtra la lista.
- **Hora obligatoria en Entrevista**, en el diálogo y en la API (400 en `hora`). Además, una hora que no existe por el cambio de hora de marzo da 400 en `hora`; al posponer a ese día pasa a las 03:00.
- **«El lunes»:** en «Posponer», si mañana es sábado o domingo, la primera opción es «El lunes».
- **Panel a dos columnas** desde `lg`: a la derecha, un panel pegajoso con el resumen o el detalle del recordatorio elegido (`?ver=12`). En el móvil, el resumen va arriba plegado y no hay detalle.
- **Flechas del tablero:** el carril horizontal va sin barra; en el escritorio, flechas laterales de 48 px que avanzan una columna, con un degradado hacia el lienzo en el lado con columnas ocultas.
- **Seguimiento cerrado solo:** en «Hechos hoy», el que se cerró al pasar a Entrevista u Oferta lleva la línea «Se cerró solo al pasar a Entrevista» (`cerradoAlPasarA` en el panel). Opción por defecto mientras Eduardo no diga otra cosa.
- **Reglas de servidor añadidas en la revisión:** no se apuntan recordatorios en una candidatura descartada o cerrada (409), y dos «Hecho» a la vez del mismo recordatorio dan 200 los dos.
