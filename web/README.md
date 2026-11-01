# RadarEmpleo · web

Front de RadarEmpleo: Angular 22 (standalone, signals, zoneless), Tailwind 4 y Angular Material.
Se usa en el navegador del PC y, a partir de la F7, en Android con Capacitor.

## Requisitos

- Node 24 y npm.
- .NET 10 SDK para arrancar la API.

La primera vez, instala las dependencias desde esta carpeta (`web/`):

```bash
npm ci
```

## Arrancar en desarrollo

Hacen falta dos terminales.

1. La API, desde esta carpeta (`web/`). Escucha en `http://localhost:5180`:

   ```bash
   dotnet run --project ../src/RadarEmpleo.Api
   ```

2. La web, también desde `web/`:

   ```bash
   npm start
   ```

   `npm start` es `ng serve`, que ya usa `proxy.conf.json` (configurado en `angular.json`): las
   peticiones a `/api` y `/health` se reenvían a `http://localhost:5180`, así que no hay CORS ni
   URL de la API en el código (`environment.apiUrl` está vacía).

Abre `http://localhost:4200`. En «Hoy» tiene que salir «API conectada · modo Local». Si sale «No se
puede conectar con la API», comprueba que la API sigue en marcha y pulsa «Reintentar».

En producción no hay proxy: la API sirve el build de Angular desde `wwwroot` (misma dirección).

## Tests, formato y build

```bash
npx ng test --watch=false     # tests unitarios con Vitest
npx prettier --check src      # formato (npx prettier --write src para corregirlo)
npx ng build                  # build de producción en dist/
```

El CI (`.github/workflows/ci.yml`) pasa los tres en cada push y pull request a `main`.

## Estructura

Una carpeta por funcionalidad, cada una con las subcarpetas que necesite:

```text
src/
  app/
    app.config.ts            providers: router, HttpClient, idioma español, fechas de Material
    app.routes.ts            rutas; el layout se carga al arrancar y cada página en diferido
    core/                    lo que usa toda la app (servicio e indicador de estado de la API)
    layout/                  menú lateral (PC), barra inferior (móvil) y página que los envuelve
    hoy/                     página Hoy
    candidaturas/            Tablero y Apuntar
    rastreos/                ofertas de los rastreos
    mas/                     agenda, perfil, ajustes...
    shared/                  componentes sin lógica de negocio (cabecera de página)
  environments/              environment*.ts; environment.example.ts documenta las opciones
  material-theme.scss        tema de Material (colores --mat-sys-*)
  styles.css                 Tailwind, Roboto local y colores del tema expuestos a Tailwind
```

Dentro de cada funcionalidad:

- `pages/`: páginas enrutadas (`xxx-page.component.ts`, con `export default` para cargarlas en diferido).
- `components/`: piezas de una página.
- `services/`: estado compartido (signals privados con `asReadonly()`) y llamadas HTTP.
- `interfaces/`: tipos de las respuestas de la API.
- `utils/`: funciones puras y constantes.
