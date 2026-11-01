import {
  afterNextRender,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { catchError, debounceTime, distinctUntilChanged, filter, map, of, switchMap } from 'rxjs';
import {
  aplicarErroresServidor,
  mensajeError,
  obligatorio,
  textoOpcional,
} from '../../../shared/utils/validadores';
import {
  CandidaturaDetalle,
  Encaje,
  GuardarCandidatura,
  Modalidad,
} from '../../interfaces/candidatura.interface';
import { EmpresasService } from '../../services/empresas.service';
import { CAMPOS_CANDIDATURA, MAXIMOS_CANDIDATURA } from '../../utils/campos-candidatura';
import { ETIQUETAS_ENCAJE, ETIQUETAS_MODALIDAD, opciones } from '../../utils/etiquetas';
import { enlaceWeb, normalizarEnlace } from '../../utils/normalizar-enlace';
import { portalDesdeEnlace } from '../../utils/portal-desde-enlace';
import { puestoDesdeEnlace } from '../../utils/puesto-desde-enlace';

const MAX = MAXIMOS_CANDIDATURA;

// Para dar a cada <form> un id único (los botones de fuera se asocian con form="...")
let siguienteId = 0;

function texto(maximo?: number): FormControl<string> {
  return new FormControl('', {
    nonNullable: true,
    validators: maximo ? [Validators.maxLength(maximo)] : [],
  });
}

/**
 * Formulario de alta y edición de una candidatura. Valida en el cliente lo mismo que la API
 * (obligatorios y longitudes) y, si aun así la API devuelve un 400, enseña cada error en su campo.
 * En modo `rapido` (las altas) solo pide enlace, empresa y puesto: el resto toma los valores por defecto
 * de la API, salvo el portal, que se saca del enlace si es de un portal conocido. «Añadir más datos»
 * despliega el formulario completo sin perder lo escrito.
 * Con `conBotones` a false no pinta el botón de guardar: quien lo use (el diálogo) pone el suyo fuera
 * con `form="idFormulario"`, y así Intro y el clic siguen enviando este formulario.
 */
@Component({
  imports: [
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  selector: 'app-formulario-candidatura',
  templateUrl: './formulario-candidatura.html',
  host: { class: 'block' },
})
export class FormularioCandidatura {
  private readonly empresasService = inject(EmpresasService);
  private readonly elemento = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** Datos para editar; null para dar de alta */
  readonly inicial = input<CandidaturaDetalle | null>(null);
  readonly rapido = input(false);
  readonly guardando = input(false);
  /** Errores por campo que devolvió la API en un 400 */
  readonly erroresServidor = input<Record<string, string[]>>({});
  readonly textoGuardar = input('Guardar');
  readonly conBotones = input(true);

  readonly guardar = output<GuardarCandidatura>();

  readonly idFormulario = `formulario-candidatura-${++siguienteId}`;

  protected readonly formulario = new FormGroup({
    empresa: new FormControl('', {
      nonNullable: true,
      validators: [obligatorio, Validators.maxLength(MAX.empresa)],
    }),
    puesto: new FormControl('', {
      nonNullable: true,
      validators: [obligatorio, Validators.maxLength(MAX.puesto)],
    }),
    enlace: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAX.enlace), enlaceWeb],
    }),
    portal: texto(MAX.portal),
    modalidad: new FormControl<Modalidad>('NoIndicada', { nonNullable: true }),
    modalidadDetalle: texto(MAX.modalidadDetalle),
    ubicacion: texto(MAX.ubicacion),
    encaje: new FormControl<Encaje>('SinValorar', { nonNullable: true }),
    pega: texto(MAX.pega),
    salario: texto(MAX.salario),
    analisis: texto(),
  });

  protected readonly campos = CAMPOS_CANDIDATURA;
  protected readonly mensajeError = mensajeError;
  protected readonly modalidades = opciones(ETIQUETAS_MODALIDAD);
  protected readonly encajes = opciones(ETIQUETAS_ENCAJE);

  /** En un alta se ha pulsado «Añadir más datos» */
  private readonly masDatos = signal(false);
  protected readonly soloBasicos = computed(() => this.rapido() && !this.masDatos());

  // Con el esquema ya puesto: «www.infojobs.net/...» también dice de qué portal es y qué puesto trae
  private readonly enlaceEscrito = this.formulario.controls.enlace.valueChanges.pipe(
    map(normalizarEnlace),
  );

  /** Portal reconocido en el enlace (en el alta rápida se manda sin enseñar el campo) */
  protected readonly portalDetectado = toSignal(this.enlaceEscrito.pipe(map(portalDesdeEnlace)), {
    initialValue: null,
  });

  // Lo último que se puso en Puesto a partir del enlace (si se escribe otra cosa, ya no se toca)
  private readonly puestoSugerido = signal('');
  private readonly valorPuesto = toSignal(this.formulario.controls.puesto.valueChanges, {
    initialValue: '',
  });
  protected readonly puestoDelEnlace = computed(
    () => this.puestoSugerido() !== '' && this.valorPuesto() === this.puestoSugerido(),
  );

  // `guardando` es un input y no se actualiza hasta el siguiente pintado: esto cubre ese hueco, en
  // el que un segundo envío aún lo vería en false y crearía la candidatura dos veces
  private envioSinConfirmar = false;

  /** Mensajes de la API que no corresponden a ningún campo del formulario */
  protected readonly erroresGenerales = signal<string[]>([]);

  protected readonly sugerenciasEmpresa = toSignal(
    this.formulario.controls.empresa.valueChanges.pipe(
      // Rellenar el formulario al editar también emite: solo se busca lo que escribe la persona
      filter(() => this.formulario.controls.empresa.dirty),
      debounceTime(250),
      map((valor) => valor.trim()),
      distinctUntilChanged(),
      switchMap((valor) =>
        valor === '' ? of([]) : this.empresasService.buscar(valor).pipe(catchError(() => of([]))),
      ),
    ),
    { initialValue: [] },
  );

  constructor() {
    // En el formulario completo, el portal se rellena solo al pegar el enlace, salvo que ya tenga
    // uno escrito a mano: solo se sustituye si está vacío o si lo había puesto este mismo código
    let portalPuesto: string | null = null;
    this.enlaceEscrito.pipe(map(portalDesdeEnlace), takeUntilDestroyed()).subscribe((portal) => {
      const control = this.formulario.controls.portal;
      if (control.value === '' || control.value === portalPuesto) {
        portalPuesto = portal;
        control.setValue(portal ?? '');
      }
    });

    // En las altas, el enlace de InfoJobs o Tecnoempleo ya trae el título: se deja escrito en Puesto,
    // con el mismo cuidado de no pisar lo que haya tecleado la persona
    this.enlaceEscrito.pipe(map(puestoDesdeEnlace), takeUntilDestroyed()).subscribe((puesto) => {
      const control = this.formulario.controls.puesto;
      const libre = control.value === '' || control.value === this.puestoSugerido();
      if (this.inicial() === null && libre) {
        this.puestoSugerido.set(puesto ?? '');
        control.setValue(puesto ?? '');
      }
    });

    effect(() => {
      const candidatura = this.inicial();
      untracked(() => {
        if (candidatura) {
          this.rellenar(candidatura);
        }
      });
    });

    effect(() => {
      const errores = this.erroresServidor();
      untracked(() => {
        this.erroresGenerales.set(aplicarErroresServidor(this.formulario, errores));
        if (Object.keys(errores).length > 0) {
          this.enfocarPrimerError();
        }
      });
    });
  }

  /** Hay algo escrito que no se ha guardado (para no perderlo al cerrar sin querer) */
  tieneCambios(): boolean {
    return this.formulario.dirty;
  }

  /** Al desaparecer el botón, el foco pasa al primer campo nuevo */
  protected mostrarMasDatos(): void {
    this.masDatos.set(true);
    afterNextRender(
      () =>
        this.elemento.nativeElement
          .querySelector<HTMLElement>('[formcontrolname="portal"]')
          ?.focus(),
      { injector: this.injector },
    );
  }

  protected enviar(): void {
    if (this.guardando() || this.envioSinConfirmar) {
      return;
    }
    if (this.formulario.invalid) {
      this.formulario.markAllAsTouched();
      this.enfocarPrimerError();
      return;
    }
    this.envioSinConfirmar = true;
    afterNextRender(() => (this.envioSinConfirmar = false), { injector: this.injector });
    this.guardar.emit(this.datos());
  }

  private datos(): GuardarCandidatura {
    const v = this.formulario.getRawValue();
    const basicos: GuardarCandidatura = {
      empresa: v.empresa.trim(),
      puesto: v.puesto.trim(),
      enlace: textoOpcional(normalizarEnlace(v.enlace)),
    };
    if (this.soloBasicos()) {
      const portal = portalDesdeEnlace(basicos.enlace);
      return portal ? { ...basicos, portal } : basicos;
    }
    // La edición es un PUT completo: se mandan todos los campos, también los vacíos
    return {
      ...basicos,
      portal: textoOpcional(v.portal),
      modalidad: v.modalidad,
      modalidadDetalle: textoOpcional(v.modalidadDetalle),
      ubicacion: textoOpcional(v.ubicacion),
      encaje: v.encaje,
      pega: textoOpcional(v.pega),
      salario: textoOpcional(v.salario),
      analisis: textoOpcional(v.analisis),
    };
  }

  private rellenar(c: CandidaturaDetalle): void {
    this.formulario.reset({
      empresa: c.empresa,
      puesto: c.puesto,
      enlace: c.enlace ?? '',
      portal: c.portal ?? '',
      modalidad: c.modalidad,
      modalidadDetalle: c.modalidadDetalle ?? '',
      ubicacion: c.ubicacion ?? '',
      encaje: c.encaje,
      pega: c.pega ?? '',
      salario: c.salario ?? '',
      analisis: c.analisis ?? '',
    });
  }

  /** Lleva el foco al primer campo con error para que el teclado y el lector de pantalla lo encuentren */
  private enfocarPrimerError(): void {
    // Se espera a que Angular pinte el estado de error antes de buscarlo
    queueMicrotask(() =>
      this.elemento.nativeElement
        .querySelector<HTMLElement>('[formcontrolname].ng-invalid')
        ?.focus(),
    );
  }
}
