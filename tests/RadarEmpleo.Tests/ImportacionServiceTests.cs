using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Importacion;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="ImportacionService"/> sobre SQLite en memoria con <c>registro-ejemplo.md</c> o textos
/// inventados. El reloj del arnés marca el 27/10/2026.
/// </summary>
public class ImportacionServiceTests : ArnesSqliteTests
{
    private const string Cabecera = """
        | Fecha | Empresa | Puesto | Modalidad | Estado | Enlace |
        |---|---|---|---|---|---|
        """;

    private readonly LectorFalso _lector = new();
    private readonly ImportacionService _importacion;
    private readonly CandidaturasService _candidaturas;

    public ImportacionServiceTests()
    {
        _importacion = new ImportacionService(Factory, _lector, Exportador, Reloj);
        _candidaturas = new CandidaturasService(Factory, Reloj, Exportador);
    }

    private void UsarEjemplo() => _lector.Texto = File.ReadAllText(FabricaApi.RutaRegistroEjemplo);

    private void UsarFilas(params string[] filas) => _lector.Texto = $"{Cabecera}\n{string.Join("\n", filas)}\n";

    /// <summary>Hace lo que hará la página: vista previa y aplicar todas las nuevas y las que avanzan.</summary>
    private async Task<ResultadoAplicacionDto> AplicarTodoAsync()
    {
        var vista = await _importacion.PrevisualizarTablaAsync();
        var claves = vista.Filas
            .Where(f => f.Resultado is ResultadoFila.Nueva or ResultadoFila.AvanzaEstado)
            .Select(f => f.Clave).ToList();
        return await _importacion.AplicarAsync(new AplicarLoteRequest(vista.Lote, claves));
    }

    private async Task<CandidaturaDetalleDto> FichaAsync(string puesto)
    {
        var fila = Assert.Single(await _candidaturas.ListarAsync(), c => c.Puesto == puesto);
        return (await _candidaturas.ObtenerAsync(fila.Id))!;
    }

    [Fact]
    public async Task Previsualizar_RegistroDeEjemplo_TodasNuevasYNoEscribeNada()
    {
        UsarEjemplo();

        var vista = await _importacion.PrevisualizarTablaAsync();

        Assert.Equal(new ResumenImportacionDto(8, 0, 0, 0, 0, 3), vista.Resumen);
        Assert.Equal(8, vista.Lote.Filas.Count);
        Assert.Equal(8, vista.Filas.Count);
        Assert.Empty(vista.SinLeer);
        await using var db = CreateContext();
        Assert.Equal(0, await db.Candidaturas.CountAsync());
        Assert.Equal(0, await db.Empresas.CountAsync());
    }

    [Fact]
    public async Task Previsualizar_SinTabla_LanzaReglaNegocio()
    {
        _lector.Texto = "# Sin tabla\n";

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => _importacion.PrevisualizarTablaAsync());
        Assert.Contains("tabla", ex.Message);
    }

    [Fact]
    public async Task Previsualizar_FilaIlegible_SeCuentaYSeEnsenaConSuLinea()
    {
        UsarFilas("| 2026-09-01 | Acme | Uno | Remoto | Enviada | — |", "| ayer | Acme | Dos | Remoto | Enviada | — |");

        var vista = await _importacion.PrevisualizarTablaAsync();

        Assert.Equal(1, vista.Resumen.Nuevas);
        Assert.Equal(1, vista.Resumen.SinLeer);
        Assert.Equal(4, Assert.Single(vista.SinLeer).Linea);
    }

    [Fact]
    public async Task Aplicar_DosVeces_NoDuplica()
    {
        UsarEjemplo();

        var primera = await AplicarTodoAsync();
        var segunda = await AplicarTodoAsync();

        Assert.Equal(8, primera.Creadas);
        Assert.Equal(0, primera.SinCambios);
        Assert.Equal(0, segunda.Creadas);
        Assert.Equal(0, segunda.Avanzadas);
        Assert.Equal(8, segunda.SinCambios);
        Assert.Equal(0, (await _importacion.PrevisualizarTablaAsync()).Resumen.Nuevas);
        await using var db = CreateContext();
        Assert.Equal(8, await db.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task Aplicar_MismoLoteDosVecesSinPrevisualizarOtraVez_NoDuplica()
    {
        UsarEjemplo();
        var vista = await _importacion.PrevisualizarTablaAsync();
        var peticion = new AplicarLoteRequest(vista.Lote, vista.Filas.Select(f => f.Clave).ToList());

        await _importacion.AplicarAsync(peticion);
        var segunda = await _importacion.AplicarAsync(peticion);

        Assert.Equal(0, segunda.Creadas);
        Assert.Equal(8, segunda.SinCambios);
    }

    [Fact]
    public async Task Aplicar_EmpresaNuevaEnDosFilas_CreaUnaSolaEmpresa()
    {
        UsarEjemplo();

        await AplicarTodoAsync();

        await using var db = CreateContext();
        var talleres = await db.Empresas.Include(e => e.Candidaturas).SingleAsync(e => e.NombreNormalizado == "talleres ficticios");
        Assert.Equal("Talleres Ficticios S.L.", talleres.Nombre);
        Assert.Equal(2, talleres.Candidaturas.Count);
    }

    [Fact]
    public async Task Aplicar_EmpresaYaExistenteEscritaDistinto_LaReutiliza()
    {
        await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("ACME, S.L.", "Otro puesto"));
        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Enviada | — |");

        await AplicarTodoAsync();

        await using var db = CreateContext();
        Assert.Equal(1, await db.Empresas.CountAsync());
        Assert.Equal(2, await db.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task Aplicar_Nueva_LlevaLaFechaDelRegistroEnHistorialEnvioYDiasEnElEstado()
    {
        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Enviada (LinkedIn) | https://ejemplo.com/1 |");
        // 1/9 en Madrid (verano, UTC+2) empieza el 31/8 a las 22:00 UTC.
        var fecha = new DateTime(2026, 8, 31, 22, 0, 0, DateTimeKind.Utc);

        await AplicarTodoAsync();

        var ficha = await FichaAsync("Backend");
        Assert.Equal(EstadoCandidatura.Enviada, ficha.Estado);
        Assert.Equal(Origen.Registro, ficha.Origen);
        Assert.Equal(AnalizadorRegistro.SeccionTabla, ficha.SeccionOrigen);
        Assert.Equal(fecha, ficha.FechaDeteccion);
        Assert.Equal(fecha, ficha.FechaEnvio);
        Assert.Equal(fecha, ficha.FechaEstado);
        Assert.Equal(fecha, ficha.FechaUltimoCambio);
        Assert.Equal([null, EstadoCandidatura.Detectada], ficha.Historial.Select(h => h.De));
        Assert.Equal([EstadoCandidatura.Detectada, EstadoCandidatura.Enviada], ficha.Historial.Select(h => h.A));
        Assert.All(ficha.Historial, h =>
        {
            Assert.Equal(fecha, h.Fecha);
            Assert.Equal(Origen.Registro, h.Origen);
        });
        Assert.Equal("LinkedIn", ficha.Portal);
        Assert.Equal(Modalidad.Remoto, ficha.Modalidad);
        Assert.Equal("Remoto", ficha.ModalidadDetalle);
        Assert.Equal("https://ejemplo.com/1", ficha.Enlace);
        var nota = Assert.Single(ficha.Notas);
        Assert.Equal("Enviada (LinkedIn)", nota.Texto);
        Assert.Equal(Origen.Registro, nota.Origen);
        Assert.Equal(fecha, nota.Fecha);
    }

    [Fact]
    public async Task Aplicar_NuevaEnDetectada_SoloTieneElAlta()
    {
        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Detectada | — |");

        var resultado = await AplicarTodoAsync();

        Assert.Equal(1, resultado.Creadas);
        var ficha = await FichaAsync("Backend");
        Assert.Equal(EstadoCandidatura.Detectada, ficha.Estado);
        Assert.Single(ficha.Historial);
        Assert.Null(ficha.FechaEnvio);
    }

    [Fact]
    public async Task Aplicar_Descartada_GuardaMotivoOtroConElTextoOriginal()
    {
        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Descartada (piden inglés) | — |");

        await AplicarTodoAsync();

        var ficha = await FichaAsync("Backend");
        Assert.Equal(EstadoCandidatura.Descartada, ficha.Estado);
        Assert.Equal(MotivoDescarte.Otro, ficha.MotivoDescarte);
        Assert.Equal("Descartada (piden inglés)", ficha.MotivoDescarteTexto);
    }

    [Fact]
    public async Task Aplicar_Descartada_ElTextoNoSaleDosVecesEnLaFicha()
    {
        UsarFilas("| 2026-09-03 | Galandor | Dev | Remoto | Descartado tras el quiz de código y lógica | — |");

        await AplicarTodoAsync();

        var ficha = await FichaAsync("Dev");
        Assert.Equal("Descartado tras el quiz de código y lógica", ficha.MotivoDescarteTexto);
        Assert.Empty(ficha.Notas);
    }

    [Fact]
    public async Task Aplicar_LoteConLaNotaIgualAlMotivo_NoCreaLaNota()
    {
        const string texto = "Piden inglés C1";
        var fila = new FilaLote(3, new DateOnly(2026, 9, 1), false, "Acme", "Backend", Estado: EstadoCandidatura.Descartada,
            MotivoDescarte: MotivoDescarte.Otro, MotivoDescarteTexto: texto, Nota: texto);
        var lote = new LoteImportacion(1, OrigenLote.Tabla, [fila]);

        await _importacion.AplicarAsync(new AplicarLoteRequest(lote, [ReglasImportacion.Clave("Acme", "Backend", fila.Fecha)]));

        var ficha = await FichaAsync("Backend");
        Assert.Equal(texto, ficha.MotivoDescarteTexto);
        Assert.Empty(ficha.Notas);
    }

    [Fact]
    public async Task Previsualizar_EstadoDeducidoDelTexto_LlevaAviso()
    {
        UsarFilas(
            "| 2026-10-30 | NovaIT | Dev | Remoto | Llamada para concertar; **entrevista por Teams el 11/11** | — |",
            "| 2026-10-30 | Otra | Dev | Remoto | Enviada (LinkedIn) | — |");

        var vista = await _importacion.PrevisualizarTablaAsync();

        Assert.Equal(EstadoCandidatura.Entrevista, vista.Filas[0].EstadoRegistro);
        Assert.Equal([ReglasImportacion.AvisoEstadoDeducido], vista.Filas[0].Avisos);
        Assert.Empty(vista.Filas[1].Avisos);
    }

    [Theory]
    // Hoy es 27/10/2026: el seguimiento de un envío del 21/10 sería el 28/10, aún no ha pasado.
    [InlineData("2026-10-21", true)]
    // El del 20/10 sería hoy mismo a las 09:00 de Madrid (08:00 UTC) y ya son las 10:00 UTC.
    [InlineData("2026-10-20", false)]
    [InlineData("2026-09-01", false)]
    public async Task Aplicar_EnviadaImportada_SeguimientoSoloSiNoHaVencido(string dia, bool conSeguimiento)
    {
        UsarFilas($"| {dia} | Acme | Backend | Remoto | Enviada | — |");

        await AplicarTodoAsync();

        var ficha = await FichaAsync("Backend");
        Assert.Equal(conSeguimiento, ficha.Recordatorios.Count == 1);
        Assert.NotNull(ficha.FechaEnvio);
    }

    [Fact]
    public async Task Aplicar_TrasEditarLaImportada_NoDuplicaNiPisaLoEditado()
    {
        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Enviada | — |");
        await AplicarTodoAsync();
        var ficha = await FichaAsync("Backend");
        await _candidaturas.EditarAsync(ficha.Id, new GuardarCandidaturaRequest("Acme", "Backend senior", Salario: "35.000 €"));

        var vista = await _importacion.PrevisualizarTablaAsync();
        var fila = Assert.Single(vista.Filas);
        Assert.Equal(ResultadoFila.YaExiste, fila.Resultado);
        await AplicarTodoAsync();

        var editada = await FichaAsync("Backend senior");
        Assert.Equal("35.000 €", editada.Salario);
        await using var db = CreateContext();
        Assert.Equal(1, await db.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task Aplicar_EstadoPosterior_AvanzaConElTextoDelRegistroComoComentarioSinTocarNotasNiDatos()
    {
        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Enviada | — |");
        await AplicarTodoAsync();
        var ficha = await FichaAsync("Backend");
        await _candidaturas.AnadirNotaAsync(ficha.Id, new CrearNotaRequest("Nota mía"));
        await _candidaturas.EditarAsync(ficha.Id, new GuardarCandidaturaRequest("Acme", "Backend", Salario: "35.000 €", FechaDeteccion: ficha.FechaDeteccion));
        Reloj.Avanzar(TimeSpan.FromHours(2));

        UsarFilas("| 2026-09-01 | Acme | Backend | Híbrido Málaga | Entrevista (primera llamada) | https://ejemplo.com/otro |");
        var resultado = await AplicarTodoAsync();

        Assert.Equal(new ResultadoAplicacionDto(0, 1, 0, 0, true), resultado);
        var avanzada = await FichaAsync("Backend");
        Assert.Equal(EstadoCandidatura.Entrevista, avanzada.Estado);
        var cambio = avanzada.Historial[^1];
        Assert.Equal("Entrevista (primera llamada)", cambio.Comentario);
        Assert.Equal(Origen.Registro, cambio.Origen);
        Assert.Equal(Reloj.AhoraUtc, cambio.Fecha);
        // El estado es lo único que cambia: ni notas nuevas ni datos del registro.
        Assert.Equal("Nota mía", Assert.Single(avanzada.Notas).Texto);
        Assert.Equal("35.000 €", avanzada.Salario);
        Assert.Null(avanzada.Enlace);
        Assert.Equal(Modalidad.NoIndicada, avanzada.Modalidad);
    }

    [Fact]
    public async Task Aplicar_RegistroConEstadoAnterior_NuncaRetrocede()
    {
        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Entrevista | — |");
        await AplicarTodoAsync();

        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Enviada | — |");
        var resultado = await AplicarTodoAsync();

        Assert.Equal(1, resultado.SinCambios);
        Assert.Equal(EstadoCandidatura.Entrevista, (await FichaAsync("Backend")).Estado);
    }

    [Fact]
    public async Task Aplicar_ConflictoPedidoEnLasClaves_SeSaltaYSeCuenta()
    {
        var manual = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme", "Backend"));
        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Enviada | — |");
        var vista = await _importacion.PrevisualizarTablaAsync();
        var fila = Assert.Single(vista.Filas);
        Assert.Equal(ResultadoFila.Conflicto, fila.Resultado);
        Assert.Equal(manual.Id, fila.CandidaturaId);

        var resultado = await _importacion.AplicarAsync(new AplicarLoteRequest(vista.Lote, [fila.Clave]));

        Assert.Equal(new ResultadoAplicacionDto(0, 0, 0, 1, true), resultado);
        Assert.Equal(EstadoCandidatura.Detectada, (await _candidaturas.ObtenerAsync(manual.Id))!.Estado);
        await using var db = CreateContext();
        Assert.Equal(1, await db.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task Aplicar_ConflictoNoPedido_TambienSeCuentaParaQueLasCifrasSumenElLote()
    {
        await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme", "Backend"));
        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Enviada | — |", "| 2026-09-02 | Otra | Tester | Remoto | Enviada | — |");

        var resultado = await AplicarTodoAsync();

        Assert.Equal(new ResultadoAplicacionDto(1, 0, 0, 1, true), resultado);
    }

    [Fact]
    public async Task Aplicar_FilaRepetidaEnElRegistro_CreaSoloLaPrimera()
    {
        UsarFilas("| 2026-09-01 | Acme | Backend | Remoto | Enviada | — |", "| 2026-09-01 | ACME S.L. | backend | Remoto | Entrevista | — |");

        var resultado = await AplicarTodoAsync();

        Assert.Equal(new ResultadoAplicacionDto(1, 0, 0, 1, true), resultado);
        Assert.Equal(EstadoCandidatura.Enviada, (await FichaAsync("Backend")).Estado);
    }

    [Fact]
    public async Task Aplicar_ClavesParciales_SoloActuaSobreEsasYElRestoQuedaSinCambios()
    {
        UsarEjemplo();
        var vista = await _importacion.PrevisualizarTablaAsync();
        var clave = vista.Filas[3].Clave;

        var resultado = await _importacion.AplicarAsync(new AplicarLoteRequest(vista.Lote, [clave]));

        Assert.Equal(new ResultadoAplicacionDto(1, 0, 7, 0, true), resultado);
        await using var db = CreateContext();
        Assert.Equal(clave, (await db.Candidaturas.SingleAsync()).ClaveImportacion);
    }

    [Fact]
    public async Task Aplicar_SinClaves_NoHaceNada()
    {
        UsarEjemplo();
        var vista = await _importacion.PrevisualizarTablaAsync();

        var resultado = await _importacion.AplicarAsync(new AplicarLoteRequest(vista.Lote, []));

        Assert.Equal(new ResultadoAplicacionDto(0, 0, 8, 0, true), resultado);
    }

    [Fact]
    public async Task Aplicar_EnlaceInvalido_SeImportaSinEnlaceYElBuenoSeNormaliza()
    {
        UsarFilas("| 2026-09-01 | Acme | Uno | Remoto | Enviada | mailto:rrhh@ejemplo.com |", "| 2026-09-02 | Acme | Dos | Remoto | Enviada | www.ejemplo.com/2 |");

        var vista = await _importacion.PrevisualizarTablaAsync();
        Assert.Equal([ReglasImportacion.AvisoEnlaceNoValido], vista.Filas[0].Avisos);
        Assert.Empty(vista.Filas[1].Avisos);
        await AplicarTodoAsync();

        Assert.Null((await FichaAsync("Uno")).Enlace);
        Assert.Equal("https://www.ejemplo.com/2", (await FichaAsync("Dos")).Enlace);
    }

    [Fact]
    public async Task Aplicar_FechaAproximada_SeQuedaEnLaNotaYEnElAviso()
    {
        UsarEjemplo();

        var vista = await _importacion.PrevisualizarTablaAsync();
        Assert.Equal([ReglasImportacion.AvisoFechaAproximada], vista.Filas[2].Avisos);
        await AplicarTodoAsync();

        var ficha = await FichaAsync("Desarrollador Full Stack");
        Assert.Equal("Fecha aproximada: antes del 12-08.", Assert.Single(ficha.Notas).Texto);
    }

    [Fact]
    public async Task Aplicar_ClaveQueNoEsDelLote_LanzaValidacionYNoGuardaNada()
    {
        UsarEjemplo();
        var vista = await _importacion.PrevisualizarTablaAsync();
        var claves = vista.Filas.Select(f => f.Clave).Append("otra|cosa|2026-01-01").ToList();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _importacion.AplicarAsync(new AplicarLoteRequest(vista.Lote, claves)));

        Assert.Equal("claves", ex.Campo);
        await using var db = CreateContext();
        Assert.Equal(0, await db.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task Aplicar_OtraVersionDelLote_LanzaValidacion()
    {
        var lote = new LoteImportacion(2, OrigenLote.Tabla, []);

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _importacion.AplicarAsync(new AplicarLoteRequest(lote, [])));

        Assert.Equal("version", ex.Campo);
    }

    [Fact]
    public async Task Aplicar_MasDeMilFilas_LanzaValidacion()
    {
        var fila = new FilaLote(1, new DateOnly(2026, 9, 1), false, "Acme", "Backend");
        var lote = new LoteImportacion(1, OrigenLote.Tabla, Enumerable.Repeat(fila, LimitesLote.Filas + 1).ToList());

        await Assert.ThrowsAsync<ValidacionException>(() => _importacion.AplicarAsync(new AplicarLoteRequest(lote, [])));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(9999, 12, 31)]
    public async Task Aplicar_FechaFueraDeRango_LanzaValidacion(int anio, int mes, int dia)
    {
        var fila = new FilaLote(1, new DateOnly(anio, mes, dia), false, "Acme", "Backend");

        await Assert.ThrowsAsync<ValidacionException>(() =>
            _importacion.AplicarAsync(new AplicarLoteRequest(new LoteImportacion(1, OrigenLote.Tabla, [fila]), [])));
    }

    [Fact]
    public async Task Aplicar_EmpresaSinNombreEnElLote_LanzaValidacionYNoGuardaNingunaFila()
    {
        var buena = new FilaLote(1, new DateOnly(2026, 9, 1), false, "Acme", "Backend");
        var mala = new FilaLote(2, new DateOnly(2026, 9, 2), false, ", S.L.", "Tester");
        var lote = new LoteImportacion(1, OrigenLote.Tabla, [buena, mala]);
        var claves = ReglasImportacion.Comparar(lote.Filas, []).Select(f => f.Clave).ToList();

        await Assert.ThrowsAsync<ValidacionException>(() => _importacion.AplicarAsync(new AplicarLoteRequest(lote, claves)));

        await using var db = CreateContext();
        Assert.Equal(0, await db.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task Aplicar_OtraAplicacionSeAdelantaAlGuardar_LanzaReglaConSuMensajeYNoGuardaNingunaFilaNiExporta()
    {
        UsarFilas("| 2026-09-01 | Acme | Uno | Remoto | Enviada | — |", "| 2026-09-02 | Acme | Dos | Remoto | Enviada | — |");
        var vista = await _importacion.PrevisualizarTablaAsync();

        // Justo antes de guardar, «la otra aplicación» crea la segunda fila con su misma clave.
        var claveDos = vista.Filas[1].Clave;
        var importacion = new ImportacionService(FactoryAntesDeGuardar(async () =>
        {
            await using var otra = CreateContext();
            var candidatura = Candidatura.Nueva(new Empresa { Nombre = "Otra", NombreNormalizado = "otra" }, "Dos", AhoraInicial, Origen.Registro);
            candidatura.ClaveImportacion = claveDos;
            otra.Candidaturas.Add(candidatura);
            await otra.SaveChangesAsync();
        }), _lector, Exportador, Reloj);

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            importacion.AplicarAsync(new AplicarLoteRequest(vista.Lote, vista.Filas.Select(f => f.Clave).ToList())));

        Assert.Equal(ImportacionService.MensajeYaImportadas, error.Message);
        await using var db = CreateContext();
        Assert.Equal(claveDos, (await db.Candidaturas.SingleAsync()).ClaveImportacion);
        Assert.Equal(0, Exportador.Llamadas);
    }

    [Fact]
    public async Task Aplicar_DosALaVezConCambioDeEstado_SoloUnaGuardaYNoDuplicaElCambioNiElSeguimiento()
    {
        // Detectada el 25/10 y, en el registro, ya enviada: al avanzar se programa el seguimiento.
        UsarFilas("| 2026-10-25 | Acme | Backend | Remoto | Detectada | — |");
        await AplicarTodoAsync();
        UsarFilas("| 2026-10-25 | Acme | Backend | Remoto | Enviada | — |");
        Reloj.Avanzar(TimeSpan.FromHours(1));
        var vista = await _importacion.PrevisualizarTablaAsync();
        var peticion = new AplicarLoteRequest(vista.Lote, [Assert.Single(vista.Filas, f => f.Resultado == ResultadoFila.AvanzaEstado).Clave]);

        // La «otra pestaña» aplica el mismo lote entre la lectura y el guardado de esta.
        var lenta = new ImportacionService(FactoryAntesDeGuardar(async () =>
        {
            var otra = await _importacion.AplicarAsync(peticion);
            Assert.Equal(1, otra.Avanzadas);
        }), _lector, Exportador, Reloj);

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => lenta.AplicarAsync(peticion));

        Assert.Equal(ImportacionService.MensajeCambiadasEnOtraPestana, error.Message);
        var ficha = await FichaAsync("Backend");
        Assert.Equal(EstadoCandidatura.Enviada, ficha.Estado);
        Assert.Single(ficha.Historial, h => h.A == EstadoCandidatura.Enviada);
        Assert.Single(ficha.Recordatorios);
    }

    [Fact]
    public async Task Aplicar_OtraPeticionCambiaElEstadoEntreCompararYAvanzar_Da409SinTocarNada()
    {
        // Detectada en la app; el registro dice Entrevista. Entre la comparación y la lectura para
        // avanzar, la ficha la pasa a Enviada: avanzar con lo comparado sería decidir sobre un estado viejo.
        UsarFilas("| 2026-10-25 | Acme | Backend | Remoto | Detectada | — |");
        await AplicarTodoAsync();
        UsarFilas("| 2026-10-25 | Acme | Backend | Remoto | Entrevista | — |");
        Reloj.Avanzar(TimeSpan.FromHours(1));
        var vista = await _importacion.PrevisualizarTablaAsync();
        var fila = Assert.Single(vista.Filas);
        Assert.Equal(ResultadoFila.AvanzaEstado, fila.Resultado);

        var lenta = new ImportacionService(FactoryAntesDeConsultar("Recordatorios", () =>
            _candidaturas.CambiarEstadoAsync(fila.CandidaturaId!.Value, new CambiarEstadoRequest(EstadoCandidatura.Enviada))),
            _lector, Exportador, Reloj);

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => lenta.AplicarAsync(new AplicarLoteRequest(vista.Lote, [fila.Clave])));

        Assert.Equal(ImportacionService.MensajeCambiadasEnOtraPestana, error.Message);
        var ficha = await FichaAsync("Backend");
        Assert.Equal(EstadoCandidatura.Enviada, ficha.Estado);
        Assert.DoesNotContain(ficha.Historial, h => h.A == EstadoCandidatura.Entrevista);
    }

    [Fact]
    public async Task Aplicar_OtraPeticionBorraLaQueCambiaDeEstado_Da409YNo500()
    {
        UsarFilas("| 2026-10-25 | Acme | Backend | Remoto | Enviada | — |");
        await AplicarTodoAsync();
        UsarFilas("| 2026-10-25 | Acme | Backend | Remoto | Entrevista | — |");
        var vista = await _importacion.PrevisualizarTablaAsync();
        var fila = Assert.Single(vista.Filas);

        var lenta = new ImportacionService(FactoryAntesDeConsultar("Recordatorios", () =>
            _candidaturas.BorrarAsync(fila.CandidaturaId!.Value)), _lector, Exportador, Reloj);

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => lenta.AplicarAsync(new AplicarLoteRequest(vista.Lote, [fila.Clave])));

        Assert.Equal(ImportacionService.MensajeCambiadasEnOtraPestana, error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Aplicar_MismaOfertaDosVecesConOtraFecha_SeCreaUnaSolaJuntasOPorSeparado(bool juntas)
    {
        string[] filas = ["| 2026-10-10 | Mu | Dev | Remoto | Enviada | — |", "| 2026-10-12 | Mu | Dev | Remoto | Entrevista | — |"];
        if (juntas)
        {
            UsarFilas(filas);
            Assert.Equal(new ResultadoAplicacionDto(1, 0, 0, 1, true), await AplicarTodoAsync());
        }
        else
        {
            foreach (var fila in filas)
            {
                UsarFilas(fila);
                await AplicarTodoAsync();
            }
        }

        UsarFilas(filas);
        var vista = await _importacion.PrevisualizarTablaAsync();
        Assert.Equal([ResultadoFila.YaExiste, ResultadoFila.Conflicto], vista.Filas.Select(f => f.Resultado));
        Assert.Equal(ReglasImportacion.MotivoParecida, vista.Filas[1].Motivo);
        Assert.Equal(EstadoCandidatura.Enviada, (await FichaAsync("Dev")).Estado);
        await using var db = CreateContext();
        Assert.Equal(vista.Filas[0].Clave, (await db.Candidaturas.SingleAsync()).ClaveImportacion);
    }

    [Fact]
    public async Task Aplicar_OtraPeticionCreaALaVezUnaEmpresaNueva_LaReutilizaYGuardaTodo()
    {
        UsarFilas(
            "| 2026-09-01 | Acme | Uno | Remoto | Enviada | — |",
            "| 2026-09-02 | ACME S.L. | Dos | Remoto | Enviada | — |",
            "| 2026-09-03 | Globex | Tres | Remoto | Enviada | — |");
        var vista = await _importacion.PrevisualizarTablaAsync();
        var importacion = new ImportacionService(FactoryAntesDeGuardar(async () =>
        {
            await using var otra = CreateContext();
            otra.Empresas.Add(new Empresa { Nombre = "Acme", NombreNormalizado = "acme" });
            await otra.SaveChangesAsync();
        }), _lector, Exportador, Reloj);

        var resultado = await importacion.AplicarAsync(new AplicarLoteRequest(vista.Lote, vista.Filas.Select(f => f.Clave).ToList()));

        Assert.Equal(3, resultado.Creadas);
        await using var db = CreateContext();
        var empresas = await db.Empresas.Include(e => e.Candidaturas).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(["acme", "globex"], empresas.Select(e => e.NombreNormalizado));
        Assert.Equal(["Uno", "Dos"], empresas[0].Candidaturas.OrderBy(c => c.Id).Select(c => c.Puesto));
        Assert.Equal("Tres", Assert.Single(empresas[1].Candidaturas).Puesto);
    }

    [Fact]
    public async Task Aplicar_DescartadaSinMotivoEnElLote_LanzaValidacionDelMotivo()
    {
        var fila = new FilaLote(3, new DateOnly(2026, 9, 1), false, "Acme", "Backend", Estado: EstadoCandidatura.Descartada);
        var lote = new LoteImportacion(1, OrigenLote.Tabla, [fila]);
        var claves = ReglasImportacion.Comparar(lote.Filas, []).Select(f => f.Clave).ToList();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _importacion.AplicarAsync(new AplicarLoteRequest(lote, claves)));

        Assert.Equal("motivoDescarte", ex.Campo);
        Assert.Contains("línea 3", ex.Message);
        await using var db = CreateContext();
        Assert.Equal(0, await db.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task Aplicar_SiempreExportaUnaVezDespuesDeGuardar()
    {
        UsarEjemplo();

        await AplicarTodoAsync();
        Assert.Equal(1, Exportador.Llamadas);

        await AplicarTodoAsync();
        Assert.Equal(2, Exportador.Llamadas);
    }

    [Fact]
    public async Task Previsualizar_NoExporta()
    {
        UsarEjemplo();

        await _importacion.PrevisualizarTablaAsync();

        Assert.Equal(0, Exportador.Llamadas);
    }

    [Fact]
    public async Task Aplicar_SiElRadarFalla_LaImportacionSigueValidaYLoDice()
    {
        UsarEjemplo();
        Exportador.Resultado = false;

        var resultado = await AplicarTodoAsync();

        Assert.Equal(new ResultadoAplicacionDto(8, 0, 0, 0, false), resultado);
        await using var db = CreateContext();
        Assert.Equal(8, await db.Candidaturas.CountAsync());
    }

    private sealed class LectorFalso : ILectorCurriculum
    {
        public string Texto { get; set; } = "";

        public Task<string> LeerRegistroAsync(CancellationToken cancelacion = default) => Task.FromResult(Texto);

        public Task<TextoLeido> LeerPerfilAsync(CancellationToken cancelacion = default) => throw new NotSupportedException();

        public IReadOnlyList<ArchivoDisponible> LeerDocumentosDisponibles() => throw new NotSupportedException();

        public DocumentoLeido? LeerDocumento(string rutaRelativa) => throw new NotSupportedException();

        public Task<InformesRastreo> LeerInformesRastreoAsync(CancellationToken cancelacion = default) => throw new NotSupportedException();
    }
}
