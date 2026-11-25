import { TestBed } from '@angular/core/testing';
import { VistaTableroService } from './vista-tablero.service';

describe('VistaTableroService', () => {
  it('empieza sin búsqueda y con todos los estados', () => {
    const vista = TestBed.inject(VistaTableroService);

    expect(vista.texto()).toBe('');
    expect(vista.filtroMovil()).toBe('Todas');
  });

  it('guarda la búsqueda tal cual y el estado filtrado, cada uno por su lado', () => {
    const vista = TestBed.inject(VistaTableroService);

    vista.buscar('  Acme ');
    expect(vista.texto()).toBe('  Acme ');
    expect(vista.filtroMovil()).toBe('Todas');

    vista.filtrar('Entrevista');
    expect(vista.filtroMovil()).toBe('Entrevista');
    expect(vista.texto()).toBe('  Acme ');

    vista.buscar('');
    vista.filtrar('Todas');
    expect(vista.texto()).toBe('');
    expect(vista.filtroMovil()).toBe('Todas');
  });

  it('guarda el estado mientras la app sigue abierta', () => {
    const alSalir = TestBed.inject(VistaTableroService);
    alSalir.buscar('datos');
    alSalir.filtrar('Oferta');

    const alVolver = TestBed.inject(VistaTableroService);

    expect(alVolver).toBe(alSalir);
    expect(alVolver.texto()).toBe('datos');
    expect(alVolver.filtroMovil()).toBe('Oferta');
  });

  it('no deja cambiar la vista desde fuera si no es con buscar y filtrar', () => {
    const vista = TestBed.inject(VistaTableroService);

    expect('set' in vista.texto).toBe(false);
    expect('set' in vista.filtroMovil).toBe(false);
  });
});
