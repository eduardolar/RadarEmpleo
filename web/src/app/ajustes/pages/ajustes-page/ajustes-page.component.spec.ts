import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { crearSistemaInfo } from '../../../core/utils/sistema-de-prueba';
import AjustesPageComponent from './ajustes-page.component';

describe('AjustesPageComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function pintar() {
    const fixture = TestBed.createComponent(AjustesPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  const texto = (elemento: HTMLElement) => elemento.textContent!.replace(/\s+/g, ' ');

  it('enseña el modo, la versión y los agentes de la API', () => {
    const fixture = pintar();
    http.expectOne('/api/sistema/info').flush(crearSistemaInfo({ version: '0.6.0' }));
    fixture.detectChanges();

    const elemento = fixture.nativeElement as HTMLElement;
    expect(texto(elemento)).toContain('Local');
    expect(texto(elemento)).toContain('0.6.0');
    expect(texto(elemento)).toContain('Apagados');
    expect(elemento.querySelector('button')).toBeNull();
  });

  it('si la API no responde, lo dice', () => {
    const fixture = pintar();
    http.expectOne('/api/sistema/info').error(new ProgressEvent('error'));
    fixture.detectChanges();

    expect(texto(fixture.nativeElement)).toContain('No se puede conectar con RadarEmpleo');
  });
});
