import { Routes } from '@angular/router';
import LayoutPageComponent from './layout/pages/layout-page/layout-page.component';
import { sinCambiosGuard } from './shared/guards/sin-cambios.guard';

export const routes: Routes = [
  {
    path: '',
    // El layout sale en todas las pantallas: diferirlo solo añadiría una descarga más al arrancar
    component: LayoutPageComponent,
    children: [
      {
        path: 'hoy',
        title: 'Hoy · RadarEmpleo',
        loadComponent: () => import('./hoy/pages/hoy-page/hoy-page.component'),
      },
      {
        path: 'tablero',
        title: 'Tablero · RadarEmpleo',
        loadComponent: () => import('./candidaturas/pages/tablero-page/tablero-page.component'),
      },
      {
        path: 'candidaturas/:id',
        // La ficha cambia el título por «Puesto · Empresa · RadarEmpleo» en cuanto carga
        title: 'Ficha · RadarEmpleo',
        loadComponent: () => import('./candidaturas/pages/ficha-page/ficha-page.component'),
      },
      {
        path: 'apuntar',
        title: 'Apuntar · RadarEmpleo',
        loadComponent: () => import('./candidaturas/pages/apuntar-page/apuntar-page.component'),
        canDeactivate: [sinCambiosGuard],
      },
      {
        path: 'rastreos',
        title: 'Rastreos · RadarEmpleo',
        loadComponent: () => import('./rastreos/pages/rastreos-page/rastreos-page.component'),
      },
      {
        path: 'mas',
        title: 'Más · RadarEmpleo',
        loadComponent: () => import('./mas/pages/mas-page/mas-page.component'),
      },
      {
        path: '**',
        redirectTo: 'hoy',
      },
    ],
  },
];
