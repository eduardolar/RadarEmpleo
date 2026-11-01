// Plantilla con todas las opciones. Copia este archivo como environment.ts o environment.development.ts
// solo si necesitas apuntar a una API en otra dirección.
// apiUrl va sin barra final. Vacía = la misma dirección que la web: en ng serve, proxy.conf.json
// manda /api y /health a http://localhost:5180; en producción la API sirve la web desde wwwroot.
//
// El token X-Radar-Token del móvil NUNCA va aquí (estos archivos acaban en el repositorio y dentro
// del build): se escribe en Ajustes y se guarda en el dispositivo con Capacitor Preferences (F7).
export const environment = {
  production: true,

  apiUrl: '',
};
