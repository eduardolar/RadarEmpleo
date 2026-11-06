using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="CandidaturasService"/> sobre SQLite en memoria (<see cref="ArnesSqliteTests"/>) con el
/// reloj fijo del arnés. Datos inventados.
/// </summary>
public class CandidaturasServiceTests : ArnesSqliteTests
{
    private readonly CandidaturasService _candidaturas;

    public CandidaturasServiceTests()
    {
        _candidaturas = new CandidaturasService(Factory, Reloj, Exportador); // ctor real bajo prueba
    }

    [Fact]
    public async Task CrearEditarCambiarEstadoYBorrar_CadaUno_ExportaElRadar()
    {
        var creada = await CrearAsync();
        Assert.Equal(1, Exportador.Llamadas);

        await _candidaturas.EditarAsync(creada.Id, new GuardarCandidaturaRequest("Acme", "Backend"));
        Assert.Equal(2, Exportador.Llamadas);

        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));
        Assert.Equal(3, Exportador.Llamadas);

        await _candidaturas.BorrarAsync(creada.Id);
        Assert.Equal(4, Exportador.Llamadas);
    }

    [Fact]
    public async Task OperacionesQueNoCambianElRadar_NoExportan()
    {
        var creada = await CrearAsync();
        var llamadas = Exportador.Llamadas;

        var nota = await _candidaturas.AnadirNotaAsync(creada.Id, new CrearNotaRequest("Una nota"));
        await _candidaturas.BorrarNotaAsync(nota!.Id);
        await _candidaturas.ListarAsync();
        await _candidaturas.ObtenerAsync(creada.Id);
        await _candidaturas.BorrarAsync(999);
        await _candidaturas.EditarAsync(999, new GuardarCandidaturaRequest("Acme", "Backend"));
        await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Detectada)));

        Assert.Equal(llamadas, Exportador.Llamadas);
    }

    [Fact]
    public async Task CrearAsync_SiElRadarNoSePuedeEscribir_LaCandidaturaQuedaGuardada()
    {
        Exportador.Resultado = false;

        var creada = await CrearAsync();

        Assert.NotNull(await _candidaturas.ObtenerAsync(creada.Id));
    }

    private Task<CandidaturaDetalleDto> CrearAsync(string empresa = "Acme Software S.L.", string puesto = "Desarrollador .NET",
        string? ubicacion = null, DateTime? fechaDeteccion = null) =>
        _candidaturas.CrearAsync(new GuardarCandidaturaRequest(empresa, puesto, Ubicacion: ubicacion, FechaDeteccion: fechaDeteccion));

    [Fact]
    public async Task CrearAsync_DatosValidos_QuedaDetectadaConElAltaEnElHistorial()
    {
        var detalle = await CrearAsync();

        Assert.Equal(EstadoCandidatura.Detectada, detalle.Estado);
        Assert.Equal("Acme Software S.L.", detalle.Empresa);
        Assert.Equal(AhoraInicial, detalle.FechaDeteccion);
        Assert.Equal(DateTimeKind.Utc, detalle.FechaDeteccion.Kind);
        Assert.Equal(DateTimeKind.Utc, detalle.FechaUltimoCambio.Kind);
        Assert.Equal(AhoraInicial, detalle.FechaEstado);
        Assert.Equal(DateTimeKind.Utc, detalle.FechaEstado.Kind);
        var alta = Assert.Single(detalle.Historial);
        Assert.Null(alta.De);
        Assert.Contains(EstadoCandidatura.Enviada, detalle.EstadosPosibles);

        // Releída de la BD (otro contexto): las fechas siguen siendo UTC.
        var releida = (await _candidaturas.ObtenerAsync(detalle.Id))!;
        Assert.Equal(DateTimeKind.Utc, releida.FechaDeteccion.Kind);
        Assert.Equal(DateTimeKind.Utc, releida.FechaUltimoCambio.Kind);
        Assert.Equal(AhoraInicial, releida.FechaEstado);
        Assert.Equal(DateTimeKind.Utc, releida.FechaEstado.Kind);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(releida.Historial).Fecha.Kind);
    }

    [Fact]
    public async Task CrearAsync_DosDeLaMismaEmpresaEscritaDistinto_ReutilizaLaEmpresa()
    {
        var primera = await CrearAsync(empresa: "Acme Software S.L.", puesto: "Backend .NET");
        var segunda = await CrearAsync(empresa: "ACME  software", puesto: "Full Stack Angular");

        Assert.Equal(primera.EmpresaId, segunda.EmpresaId);
        await using var db = CreateContext();
        Assert.Equal(1, await db.Empresas.CountAsync());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(", S.L.")]
    [InlineData("...")]
    [InlineData("(S.A.)")]
    public async Task CrearAsync_EmpresaVaciaOSoloFormaJuridicaOPuntuacion_LanzaValidacion(string empresa)
    {
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => CrearAsync(empresa: empresa));

        Assert.Equal("empresa", ex.Campo);
        await using var db = CreateContext();
        Assert.Equal(0, await db.Empresas.CountAsync());
    }

    [Fact]
    public async Task CrearAsync_PuestoVacio_LanzaValidacion()
    {
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => CrearAsync(puesto: "  "));
        Assert.Equal("puesto", ex.Campo);
    }

    [Fact]
    public async Task CrearAsync_ModalidadQueNoExiste_LanzaValidacion()
    {
        await Assert.ThrowsAsync<ValidacionException>(() =>
            _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme", "Backend", Modalidad: (Modalidad)42)));
    }

    [Theory]
    [InlineData("www.ejemplo.com/ofertas/1", "https://www.ejemplo.com/ofertas/1")]
    [InlineData("  https://ejemplo.com/ofertas/1  ", "https://ejemplo.com/ofertas/1")]
    [InlineData("   ", null)]
    public async Task CrearAsync_Enlace_SeGuardaNormalizado(string enlace, string? esperado)
    {
        var detalle = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme", "Backend", Enlace: enlace));

        Assert.Equal(esperado, detalle.Enlace);
    }

    [Theory]
    [InlineData("mailto:rrhh@ejemplo.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("no es un enlace")]
    public async Task CrearAsync_EnlaceQueNoEsWeb_LanzaValidacionYNoGuardaNada(string enlace)
    {
        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Empresa Nueva", "Backend", Enlace: enlace)));

        Assert.Equal("enlace", ex.Campo);
        await using var db = CreateContext();
        Assert.Equal(0, await db.Candidaturas.CountAsync());
        Assert.Equal(0, await db.Empresas.CountAsync());
    }

    [Fact]
    public async Task EditarAsync_EnlaceQueNoEsWeb_LanzaValidacionYDejaElAnterior()
    {
        var creada = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme", "Backend", Enlace: "ejemplo.com/1"));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _candidaturas.EditarAsync(creada.Id, new GuardarCandidaturaRequest("Acme", "Backend", Enlace: "mailto:a@ejemplo.com")));

        Assert.Equal("enlace", ex.Campo);
        Assert.Equal("https://ejemplo.com/1", (await _candidaturas.ObtenerAsync(creada.Id))!.Enlace);
    }

    [Fact]
    public async Task CambiarEstadoAsync_AEnviada_GuardaHistorialFechaEnvioYSeguimientoASieteDias()
    {
        var creada = await CrearAsync();
        Reloj.Avanzar(TimeSpan.FromHours(3));
        var envio = Reloj.AhoraUtc;

        var detalle = await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada, Comentario: "Por InfoJobs"));

        Assert.NotNull(detalle);
        Assert.Equal(EstadoCandidatura.Enviada, detalle!.Estado);
        Assert.Equal(envio, detalle.FechaEnvio);
        Assert.Equal(DateTimeKind.Utc, detalle.FechaEnvio!.Value.Kind);

        // Releído de la BD (no de la entidad en memoria): historial y recordatorio persistidos.
        var releido = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(2, releido.Historial.Count);
        var cambio = releido.Historial[1];
        Assert.Equal(EstadoCandidatura.Detectada, cambio.De);
        Assert.Equal(EstadoCandidatura.Enviada, cambio.A);
        Assert.Equal("Por InfoJobs", cambio.Comentario);
        Assert.Equal(envio, cambio.Fecha);
        Assert.Equal(DateTimeKind.Utc, cambio.Fecha.Kind);

        Assert.Equal(envio, releido.FechaEnvio);
        Assert.Equal(DateTimeKind.Utc, releido.FechaEnvio!.Value.Kind);

        // Enviada el 27/10/2026 a las 13:00 UTC (14:00 en Madrid) → seguimiento el 3/11 a las 09:00 de Madrid.
        var seguimiento = Assert.Single(releido.Recordatorios);
        Assert.Equal(TipoRecordatorio.Seguimiento, seguimiento.Tipo);
        Assert.Equal(new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc), seguimiento.FechaHora);
        Assert.Equal(DateTimeKind.Utc, seguimiento.FechaHora.Kind);
        Assert.Equal(creada.Id, seguimiento.CandidaturaId);
    }

    [Theory]
    // Invierno: envío el 15/01 a las 16:00 UTC → 22/01 a las 09:00 de Madrid = 08:00 UTC.
    [InlineData(2026, 1, 15, 16, 2026, 1, 22, 8)]
    // Verano: envío el 15/07 a las 16:00 UTC → 22/07 a las 09:00 de Madrid = 07:00 UTC.
    [InlineData(2026, 7, 15, 16, 2026, 7, 22, 7)]
    public async Task CambiarEstadoAsync_AEnviadaEnInviernoYEnVerano_SeguimientoALas9DeMadrid(
        int a, int m, int d, int h, int ea, int em, int ed, int eh)
    {
        Reloj.AhoraUtc = new DateTime(a, m, d, h, 0, 0, DateTimeKind.Utc);
        var creada = await CrearAsync();

        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));

        var seguimiento = Assert.Single((await _candidaturas.ObtenerAsync(creada.Id))!.Recordatorios);
        Assert.Equal(new DateTime(ea, em, ed, eh, 0, 0, DateTimeKind.Utc), seguimiento.FechaHora);
        Assert.Equal(DateTimeKind.Utc, seguimiento.FechaHora.Kind);
    }

    [Theory]
    [InlineData(EstadoCandidatura.Descartada)]
    [InlineData(EstadoCandidatura.Cerrada)]
    public async Task CambiarEstadoAsync_EnviadaYLuegoDescartadaOCerrada_ElSeguimientoQuedaHecho(EstadoCandidatura final)
    {
        var creada = await CrearAsync();
        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));
        Reloj.Avanzar(TimeSpan.FromDays(2));

        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(final, MotivoDescarte.Experiencia));

        var releido = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.True(Assert.Single(releido.Recordatorios).Hecho);
    }

    [Fact]
    public async Task CambiarEstadoAsync_EnviadaDescartadaReabiertaYEnviada_UnSoloSeguimientoPendienteYFechaEnvioNueva()
    {
        var creada = await CrearAsync();
        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));
        Reloj.Avanzar(TimeSpan.FromDays(1));
        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Descartada, MotivoDescarte.Zona));
        Reloj.Avanzar(TimeSpan.FromDays(1));
        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Detectada, Comentario: "Reabierta"));
        Reloj.Avanzar(TimeSpan.FromDays(1));
        var reenvio = Reloj.AhoraUtc;

        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));

        var releido = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(reenvio, releido.FechaEnvio);
        Assert.Null(releido.MotivoDescarte);
        Assert.Equal(2, releido.Recordatorios.Count);
        var pendiente = Assert.Single(releido.Recordatorios, r => !r.Hecho);
        Assert.Equal(ReglasEstado.FechaSeguimiento(reenvio), pendiente.FechaHora);
    }

    [Fact]
    public async Task CambiarEstadoAsync_AEntrevistaSinPasarPorEnviada_GuardaFechaEnvioSinSeguimiento()
    {
        var creada = await CrearAsync();
        Reloj.Avanzar(TimeSpan.FromHours(4));

        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Entrevista));

        var releido = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(Reloj.AhoraUtc, releido.FechaEnvio);
        Assert.Equal(DateTimeKind.Utc, releido.FechaEnvio!.Value.Kind);
        Assert.Empty(releido.Recordatorios);
    }

    [Fact]
    public async Task CambiarEstadoAsync_SinEstado_LanzaValidacion()
    {
        var creada = await CrearAsync();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(null)));
        Assert.Equal("El estado es obligatorio.", ex.Message);
    }

    [Fact]
    public async Task CambiarEstadoAsync_Retroceso_LanzaReglaNegocioYNoGuardaNada()
    {
        var creada = await CrearAsync();
        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Entrevista));

        await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Preparada)));

        var releido = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(EstadoCandidatura.Entrevista, releido.Estado);
        Assert.Equal(2, releido.Historial.Count);
    }

    [Fact]
    public async Task CambiarEstadoAsync_DescartadaSinMotivo_LanzaReglaNegocio()
    {
        var creada = await CrearAsync();

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Descartada)));
        Assert.Contains("motivo", ex.Message);
    }

    [Fact]
    public async Task CambiarEstadoAsync_DescartadaConMotivo_GuardaElMotivo()
    {
        var creada = await CrearAsync();

        var detalle = await _candidaturas.CambiarEstadoAsync(creada.Id,
            new CambiarEstadoRequest(EstadoCandidatura.Descartada, MotivoDescarte.Experiencia, "Piden 5 años"));

        Assert.Equal(EstadoCandidatura.Descartada, detalle!.Estado);
        Assert.Equal(MotivoDescarte.Experiencia, detalle.MotivoDescarte);
        Assert.Equal("Piden 5 años", detalle.MotivoDescarteTexto);
    }

    [Fact]
    public async Task CambiarEstadoAsync_CandidaturaInexistente_DevuelveNull()
    {
        Assert.Null(await _candidaturas.CambiarEstadoAsync(999, new CambiarEstadoRequest(EstadoCandidatura.Enviada)));
    }

    [Fact]
    public async Task ListarAsync_FiltroPorEstado_SoloDevuelveLasDeEseEstado()
    {
        var a = await CrearAsync(puesto: "Backend");
        await CrearAsync(puesto: "Frontend");
        await _candidaturas.CambiarEstadoAsync(a.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));

        var enviadas = await _candidaturas.ListarAsync(new FiltroCandidaturas(Estado: EstadoCandidatura.Enviada));

        var unica = Assert.Single(enviadas);
        Assert.Equal("Backend", unica.Puesto);
    }

    [Fact]
    public async Task ListarAsync_FiltroPorTextoSinTildes_EncuentraPorEmpresaYUbicacion()
    {
        await CrearAsync(empresa: "Consultoría Pérez", puesto: "Programador");
        await CrearAsync(empresa: "Acme", puesto: "Analista", ubicacion: "Málaga");
        await CrearAsync(empresa: "Otra", puesto: "Tester");

        var porEmpresa = await _candidaturas.ListarAsync(new FiltroCandidaturas(Texto: "consultoria"));
        var porUbicacion = await _candidaturas.ListarAsync(new FiltroCandidaturas(Texto: "MALAGA"));

        Assert.Equal("Programador", Assert.Single(porEmpresa).Puesto);
        Assert.Equal("Analista", Assert.Single(porUbicacion).Puesto);
    }

    [Fact]
    public async Task ListarAsync_FiltroDesde_ExcluyeLasDetectadasAntes()
    {
        await CrearAsync(puesto: "Antigua", fechaDeteccion: AhoraInicial.AddDays(-30));
        await CrearAsync(puesto: "Reciente", fechaDeteccion: AhoraInicial.AddDays(-2));

        var lista = await _candidaturas.ListarAsync(new FiltroCandidaturas(Desde: AhoraInicial.AddDays(-7)));

        Assert.Equal("Reciente", Assert.Single(lista).Puesto);
    }

    [Fact]
    public async Task ListarAsync_FiltroDesdeSoloFecha_EmpiezaALaMedianocheDeMadrid()
    {
        // 27/10/2026 en Madrid (invierno, UTC+1) empieza el 26/10 a las 23:00 UTC.
        await CrearAsync(puesto: "Del 26 en Madrid", fechaDeteccion: new DateTime(2026, 10, 26, 22, 30, 0, DateTimeKind.Utc));
        await CrearAsync(puesto: "Del 27 en Madrid", fechaDeteccion: new DateTime(2026, 10, 26, 23, 30, 0, DateTimeKind.Utc));

        var lista = await _candidaturas.ListarAsync(new FiltroCandidaturas(Desde: new DateTime(2026, 10, 27)));

        Assert.Equal("Del 27 en Madrid", Assert.Single(lista).Puesto);
    }

    [Fact]
    public async Task ListarAsync_FiltroDesdeConHoraUtc_SeRespetaTalCual()
    {
        await CrearAsync(puesto: "Antes", fechaDeteccion: new DateTime(2026, 10, 26, 23, 30, 0, DateTimeKind.Utc));
        await CrearAsync(puesto: "Después", fechaDeteccion: new DateTime(2026, 10, 27, 0, 30, 0, DateTimeKind.Utc));

        var lista = await _candidaturas.ListarAsync(new FiltroCandidaturas(Desde: new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal("Después", Assert.Single(lista).Puesto);
    }

    [Fact]
    public async Task ListarAsync_EstadoQueNoExiste_LanzaValidacion()
    {
        await Assert.ThrowsAsync<ValidacionException>(() =>
            _candidaturas.ListarAsync(new FiltroCandidaturas(Estado: (EstadoCandidatura)99)));
    }

    [Fact]
    public async Task ClaveImportacion_DosNullYUnDuplicado_LosNullSeAceptanYElDuplicadoNo()
    {
        await using (var db = CreateContext())
        {
            var empresa = new Empresa { Nombre = "Acme", NombreNormalizado = "acme" };
            db.Candidaturas.Add(Candidatura.Nueva(empresa, "Manual 1", AhoraInicial, Origen.Manual));
            db.Candidaturas.Add(Candidatura.Nueva(empresa, "Manual 2", AhoraInicial, Origen.Manual));
            var importada = Candidatura.Nueva(empresa, "Importada", AhoraInicial, Origen.Registro);
            importada.ClaveImportacion = "acme|backend|2026-10-27";
            db.Candidaturas.Add(importada);
            await db.SaveChangesAsync(); // dos NULL en el índice único: sin problema
        }

        await using (var db = CreateContext())
        {
            var empresa = await db.Empresas.SingleAsync();
            var repetida = Candidatura.Nueva(empresa, "Importada otra vez", AhoraInicial, Origen.Registro);
            repetida.ClaveImportacion = "acme|backend|2026-10-27";
            db.Candidaturas.Add(repetida);

            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(ErroresBaseDatos.EsViolacionUnica(error, out var detalle));
            Assert.Contains("Candidaturas.ClaveImportacion", detalle);
        }

        await using var lectura = CreateContext();
        Assert.Equal(3, await lectura.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task EditarAsync_CambiaEmpresaYDatos_ResuelveLaEmpresaYGuarda()
    {
        var creada = await CrearAsync(empresa: "Acme");

        var editada = await _candidaturas.EditarAsync(creada.Id,
            new GuardarCandidaturaRequest("Globex, S.A.", "Desarrollador Angular", Modalidad: Modalidad.Remoto, Encaje: Encaje.Alto));

        Assert.NotNull(editada);
        Assert.Equal("Globex, S.A.", editada!.Empresa);
        Assert.NotEqual(creada.EmpresaId, editada.EmpresaId);
        Assert.Equal(Modalidad.Remoto, editada.Modalidad);
        Assert.Equal(Encaje.Alto, editada.Encaje);
        Assert.Equal(EstadoCandidatura.Detectada, editada.Estado); // editar no toca el estado
        Assert.Single(editada.Historial);                              // la ficha devuelta va completa
        Assert.Equal(DateTimeKind.Utc, editada.FechaUltimoCambio.Kind);
    }

    [Fact]
    public async Task CrearAsync_ConFechaDeteccionAnterior_FechaEstadoEsLaDeteccion()
    {
        var detectada = AhoraInicial.AddDays(-6);

        var creada = await CrearAsync(fechaDeteccion: detectada);

        Assert.Equal(detectada, creada.FechaEstado);
        Assert.Equal(AhoraInicial, creada.FechaUltimoCambio);
        var releida = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(detectada, releida.FechaEstado);
        Assert.Equal(DateTimeKind.Utc, releida.FechaEstado.Kind);
    }

    [Fact]
    public async Task CambiarEstadoAsync_TrasUnosDias_ActualizaFechaEstado()
    {
        var creada = await CrearAsync();
        Reloj.Avanzar(TimeSpan.FromDays(3));

        var enviada = await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));

        Assert.Equal(Reloj.AhoraUtc, enviada!.FechaEstado);
        var releida = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(Reloj.AhoraUtc, releida.FechaEstado);
        Assert.Equal(Reloj.AhoraUtc, releida.FechaUltimoCambio);
    }

    [Fact]
    public async Task EditarAsync_TrasUnosDias_NoCambiaFechaEstadoPeroSiFechaUltimoCambio()
    {
        var creada = await CrearAsync();
        Reloj.Avanzar(TimeSpan.FromDays(1));
        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));
        var entradaEnEnviada = Reloj.AhoraUtc;
        Reloj.Avanzar(TimeSpan.FromDays(4));

        var editada = await _candidaturas.EditarAsync(creada.Id,
            new GuardarCandidaturaRequest("Acme Software S.L.", "Desarrollador .NET", Salario: "35.000 €"));

        Assert.Equal(entradaEnEnviada, editada!.FechaEstado);
        Assert.Equal(Reloj.AhoraUtc, editada.FechaUltimoCambio);

        // Igual releída y en el listado (lo que pinta el tablero).
        var releida = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(entradaEnEnviada, releida.FechaEstado);
        Assert.Equal(Reloj.AhoraUtc, releida.FechaUltimoCambio);
        var fila = Assert.Single(await _candidaturas.ListarAsync());
        Assert.Equal(entradaEnEnviada, fila.FechaEstado);
        Assert.Equal(DateTimeKind.Utc, fila.FechaEstado.Kind);
        Assert.Equal(Reloj.AhoraUtc, fila.FechaUltimoCambio);
    }

    [Fact]
    public async Task EditarAsync_Inexistente_DevuelveNull()
    {
        Assert.Null(await _candidaturas.EditarAsync(999, new GuardarCandidaturaRequest("Acme", "Backend")));
    }

    [Fact]
    public async Task BorrarAsync_ConNotasYRecordatorio_BorraEnCascadaYDejaLaEmpresa()
    {
        var creada = await CrearAsync();
        await _candidaturas.AnadirNotaAsync(creada.Id, new CrearNotaRequest("Llamar el lunes"));
        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));

        Assert.True(await _candidaturas.BorrarAsync(creada.Id));

        await using var db = CreateContext();
        Assert.Equal(0, await db.Candidaturas.CountAsync());
        Assert.Equal(0, await db.Notas.CountAsync());
        Assert.Equal(0, await db.CambiosEstado.CountAsync());
        Assert.Equal(0, await db.Recordatorios.CountAsync());
        Assert.Equal(1, await db.Empresas.CountAsync());
    }

    [Fact]
    public async Task AnadirNotaYBorrarNota_FlujoCompleto_LaNotaApareceYDesaparece()
    {
        var creada = await CrearAsync();

        var nota = await _candidaturas.AnadirNotaAsync(creada.Id, new CrearNotaRequest("  Me contestó Laura  "));
        Assert.NotNull(nota);
        Assert.Equal("Me contestó Laura", nota!.Texto);
        Assert.Equal(AhoraInicial, nota.Fecha);
        Assert.Single((await _candidaturas.ObtenerAsync(creada.Id))!.Notas);

        Assert.True(await _candidaturas.BorrarNotaAsync(nota.Id));
        Assert.Empty((await _candidaturas.ObtenerAsync(creada.Id))!.Notas);
        Assert.False(await _candidaturas.BorrarNotaAsync(nota.Id));
    }

    [Fact]
    public async Task AnadirNotaAsync_CandidaturaInexistente_DevuelveNull()
    {
        Assert.Null(await _candidaturas.AnadirNotaAsync(999, new CrearNotaRequest("Hola")));
    }

    [Fact]
    public async Task EdicionesYCambiosDeEstadoSeguidos_ConElControlDeConcurrencia_NoChocan()
    {
        var creada = await CrearAsync();

        // Mismo instante a propósito: el control compara con lo leído, no con la hora.
        await _candidaturas.EditarAsync(creada.Id, new GuardarCandidaturaRequest("Acme", "Backend"));
        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await _candidaturas.EditarAsync(creada.Id, new GuardarCandidaturaRequest("Acme", "Backend senior"));
        await _candidaturas.AnadirNotaAsync(creada.Id, new CrearNotaRequest("Llamada"));
        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Entrevista));

        var ficha = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(EstadoCandidatura.Entrevista, ficha.Estado);
        Assert.Equal("Backend senior", ficha.Puesto);
        Assert.True(await _candidaturas.BorrarAsync(creada.Id));
    }

    [Fact]
    public async Task CambiarEstadoAsync_OtraPeticionLaCambiaEntreMedias_ChocaYNoDuplicaElCambioNiElSeguimiento()
    {
        var creada = await CrearAsync();
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var lenta = new CandidaturasService(FactoryAntesDeGuardar(() =>
            _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada))), Reloj, Exportador);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            lenta.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada)));

        var ficha = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Single(ficha.Historial, h => h.A == EstadoCandidatura.Enviada);
        Assert.Single(ficha.Recordatorios);
    }

    [Fact]
    public async Task BorrarAsync_OtraPeticionLaBorraEntreMedias_DevuelveFalseComoSiNoExistiera()
    {
        var creada = await CrearAsync();
        var lenta = new CandidaturasService(FactoryAntesDeGuardar(() => _candidaturas.BorrarAsync(creada.Id)), Reloj, Exportador);

        Assert.False(await lenta.BorrarAsync(creada.Id));

        Assert.Null(await _candidaturas.ObtenerAsync(creada.Id));
    }

    [Fact]
    public async Task BorrarAsync_OtraPeticionLaCambiaEntreMedias_SigueChocandoYNoLaBorra()
    {
        var creada = await CrearAsync();
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var lenta = new CandidaturasService(FactoryAntesDeGuardar(() =>
            _candidaturas.EditarAsync(creada.Id, new GuardarCandidaturaRequest("Acme", "Cambiada"))), Reloj, Exportador);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => lenta.BorrarAsync(creada.Id));

        Assert.Equal("Cambiada", (await _candidaturas.ObtenerAsync(creada.Id))!.Puesto);
    }

    [Fact]
    public async Task BorrarNotaAsync_OtraPeticionLaBorraEntreMedias_DevuelveFalse()
    {
        var creada = await CrearAsync();
        var nota = (await _candidaturas.AnadirNotaAsync(creada.Id, new CrearNotaRequest("Una nota")))!;
        var lenta = new CandidaturasService(FactoryAntesDeGuardar(() => _candidaturas.BorrarNotaAsync(nota.Id)), Reloj, Exportador);

        Assert.False(await lenta.BorrarNotaAsync(nota.Id));

        Assert.Empty((await _candidaturas.ObtenerAsync(creada.Id))!.Notas);
    }

    [Fact]
    public async Task EditarAsync_OtraPeticionLaEditaEntreMedias_ChocaYSeQuedaLaPrimera()
    {
        var creada = await CrearAsync();
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var lenta = new CandidaturasService(FactoryAntesDeGuardar(() =>
            _candidaturas.EditarAsync(creada.Id, new GuardarCandidaturaRequest("Acme", "Primera"))), Reloj, Exportador);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            lenta.EditarAsync(creada.Id, new GuardarCandidaturaRequest("Acme", "Segunda")));

        Assert.Equal("Primera", (await _candidaturas.ObtenerAsync(creada.Id))!.Puesto);
    }
}
