using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="RecordatoriosService"/> (crear, editar, hecho, posponer y borrar) sobre SQLite en memoria,
/// con el reloj fijo el viernes 13/11/2026 a las 10:00 de Madrid. Datos inventados.
/// </summary>
public class RecordatoriosServiceTests : ArnesSqliteTests
{
    private static readonly DateOnly Dia17 = new(2026, 11, 17);

    private readonly RecordatoriosService _recordatorios;
    private readonly CandidaturasService _candidaturas;

    public RecordatoriosServiceTests()
    {
        Reloj.AhoraUtc = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);
        _recordatorios = NuevoRecordatorios();
        _candidaturas = new CandidaturasService(Factory, Reloj, Exportador);
    }

    private static GuardarRecordatorioRequest Post(string titulo = "Publicar en LinkedIn: post de Angular",
        string? hora = null, string? notas = null) =>
        new(TipoRecordatorio.PublicacionLinkedIn, titulo, Dia17, hora, notas);

    private async Task<RecordatorioDto> CrearAsync() => (await _recordatorios.CrearAsync(Post()))!;

    private Task<CandidaturaDetalleDto> CrearCandidaturaAsync() =>
        _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Consultora Ejemplo", "Desarrollador .NET"));

    private async Task<Recordatorio> EnBdAsync(int id)
    {
        await using var db = CreateContext();
        return await db.Recordatorios.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    // ---------------------------------------------------------------- crear

    [Fact]
    public async Task Crear_Suelto_LoGuardaALasNueveDeMadridSinCandidatura()
    {
        var creado = (await _recordatorios.CrearAsync(Post(titulo: "  Publicar en LinkedIn  ", notas: "  ")))!;

        Assert.Null(creado.CandidaturaId);
        Assert.Equal(TipoRecordatorio.PublicacionLinkedIn, creado.Tipo);
        Assert.Equal("Publicar en LinkedIn", creado.Titulo);
        Assert.Equal(new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc), creado.FechaHora);
        Assert.False(creado.Hecho);
        Assert.Null(creado.FechaHecho);
        Assert.Null(creado.Notas);
        Assert.Equal(creado, await _recordatorios.ObtenerAsync(creado.Id));
    }

    [Fact]
    public async Task Crear_DeUnaCandidatura_QuedaEnSuFichaSinTocarLaCandidaturaNiElRadar()
    {
        var candidatura = await CrearCandidaturaAsync();
        var exportaciones = Exportador.Llamadas;
        Reloj.Avanzar(TimeSpan.FromMinutes(5));

        var creado = (await _recordatorios.CrearAsync(
            new GuardarRecordatorioRequest(TipoRecordatorio.Entrevista, "Entrevista en Consultora Ejemplo",
                new DateOnly(2026, 11, 11), "15:00", "Por Teams"), candidatura.Id))!;

        Assert.Equal(candidatura.Id, creado.CandidaturaId);
        Assert.Equal(new DateTime(2026, 11, 11, 14, 0, 0, DateTimeKind.Utc), creado.FechaHora);
        Assert.Equal("Por Teams", creado.Notas);
        var ficha = (await _candidaturas.ObtenerAsync(candidatura.Id))!;
        Assert.Equal(creado, Assert.Single(ficha.Recordatorios));
        Assert.Equal(candidatura.FechaUltimoCambio, ficha.FechaUltimoCambio);
        Assert.Equal(exportaciones, Exportador.Llamadas);
    }

    [Fact]
    public async Task Crear_DeUnaCandidaturaQueNoExiste_DevuelveNullYNoGuardaNada()
    {
        Assert.Null(await _recordatorios.CrearAsync(Post(), candidaturaId: 999));

        await using var db = CreateContext();
        Assert.Empty(await db.Recordatorios.ToListAsync());
    }

    [Theory]
    [InlineData(EstadoCandidatura.Descartada)]
    [InlineData(EstadoCandidatura.Cerrada)]
    public async Task Crear_DeUnaCandidaturaDescartadaOCerrada_LanzaReglaNegocioYNoGuardaNada(EstadoCandidatura estado)
    {
        var candidatura = await CrearCandidaturaAsync();
        await _candidaturas.CambiarEstadoAsync(candidatura.Id, new CambiarEstadoRequest(estado,
            estado == EstadoCandidatura.Descartada ? MotivoDescarte.Zona : null));

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() => _recordatorios.CrearAsync(
            new GuardarRecordatorioRequest(TipoRecordatorio.Plazo, "Plazo de Consultora Ejemplo", Dia17), candidatura.Id));

        Assert.Equal(RecordatoriosService.MensajeCandidaturaFinal, error.Message);
        await using var db = CreateContext();
        Assert.Empty(await db.Recordatorios.ToListAsync());
    }

    [Fact]
    public async Task Crear_ALaHoraQueNoExisteElDiaDelSaltoDeMarzo_LanzaValidacionDeLaHoraYNoGuardaNada()
    {
        var error = await Assert.ThrowsAsync<ValidacionException>(() => _recordatorios.CrearAsync(
            new GuardarRecordatorioRequest(TipoRecordatorio.Otro, "Renovar la demanda", new DateOnly(2027, 3, 28), "02:30")));

        Assert.Equal("hora", error.Campo);
        Assert.Equal(ReglasRecordatorio.MensajeHoraInexistente, error.Message);
        await using var db = CreateContext();
        Assert.Empty(await db.Recordatorios.ToListAsync());
    }

    [Fact]
    public async Task Crear_ConElTituloEnBlanco_LanzaValidacionDelTitulo()
    {
        var error = await Assert.ThrowsAsync<ValidacionException>(() => _recordatorios.CrearAsync(Post(titulo: "   ")));

        Assert.Equal("titulo", error.Campo);
    }

    [Fact]
    public async Task Crear_ConUnTipoQueNoExiste_LanzaValidacionDelTipo()
    {
        var error = await Assert.ThrowsAsync<ValidacionException>(() =>
            _recordatorios.CrearAsync(new GuardarRecordatorioRequest((TipoRecordatorio)99, "Algo", Dia17)));

        Assert.Equal("tipo", error.Campo);
    }

    [Fact]
    public async Task Crear_ConUnDiaDe1999_LanzaValidacionDelDia()
    {
        var error = await Assert.ThrowsAsync<ValidacionException>(() =>
            _recordatorios.CrearAsync(new GuardarRecordatorioRequest(TipoRecordatorio.Otro, "Algo", new DateOnly(1999, 1, 1))));

        Assert.Equal("dia", error.Campo);
    }

    [Fact]
    public async Task Crear_ConUnaHoraMala_LanzaValidacionDeLaHora()
    {
        var error = await Assert.ThrowsAsync<ValidacionException>(() => _recordatorios.CrearAsync(Post(hora: "25:00")));

        Assert.Equal("hora", error.Campo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Crear_UnaEntrevistaSinHora_LanzaValidacionDeLaHoraYNoGuardaNada(string? hora)
    {
        var error = await Assert.ThrowsAsync<ValidacionException>(() => _recordatorios.CrearAsync(
            new GuardarRecordatorioRequest(TipoRecordatorio.Entrevista, "Entrevista en Consultora Ejemplo", Dia17, hora)));

        Assert.Equal("hora", error.Campo);
        Assert.Equal("Pon la hora de la entrevista.", error.Message);
        await using var db = CreateContext();
        Assert.Empty(await db.Recordatorios.ToListAsync());
    }

    [Fact]
    public async Task Crear_SinTipoOSinDia_LanzaValidacion()
    {
        var sinTipo = await Assert.ThrowsAsync<ValidacionException>(() =>
            _recordatorios.CrearAsync(new GuardarRecordatorioRequest(null, "Algo", Dia17)));
        var sinDia = await Assert.ThrowsAsync<ValidacionException>(() =>
            _recordatorios.CrearAsync(new GuardarRecordatorioRequest(TipoRecordatorio.Otro, "Algo", null)));

        Assert.Equal("tipo", sinTipo.Campo);
        Assert.Equal("dia", sinDia.Campo);
    }

    // ---------------------------------------------------------------- editar

    [Fact]
    public async Task Editar_CambiaTipoTituloFechaYNotas_PeroNoHechoNiLaCandidatura()
    {
        var candidatura = await CrearCandidaturaAsync();
        var creado = (await _recordatorios.CrearAsync(Post(), candidatura.Id))!;
        await _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true));
        var hechoEn = Reloj.AhoraUtc;
        Reloj.Avanzar(TimeSpan.FromMinutes(5));

        var editado = (await _recordatorios.EditarAsync(creado.Id,
            new GuardarRecordatorioRequest(TipoRecordatorio.Plazo, "Plazo de Consultora Ejemplo",
                new DateOnly(2026, 11, 20), "18:30", "Cierra a las 19:00")))!;

        Assert.Equal(TipoRecordatorio.Plazo, editado.Tipo);
        Assert.Equal("Plazo de Consultora Ejemplo", editado.Titulo);
        Assert.Equal(new DateTime(2026, 11, 20, 17, 30, 0, DateTimeKind.Utc), editado.FechaHora);
        Assert.Equal("Cierra a las 19:00", editado.Notas);
        Assert.True(editado.Hecho);
        Assert.Equal(hechoEn, editado.FechaHecho);
        Assert.Equal(candidatura.Id, editado.CandidaturaId);
        Assert.Equal(editado, await _recordatorios.ObtenerAsync(creado.Id));
    }

    [Fact]
    public async Task Editar_PasarAEntrevistaSinHora_LanzaValidacionDeLaHoraYNoCambiaNada()
    {
        var creado = await CrearAsync();

        var error = await Assert.ThrowsAsync<ValidacionException>(() => _recordatorios.EditarAsync(creado.Id,
            new GuardarRecordatorioRequest(TipoRecordatorio.Entrevista, "Entrevista en Consultora Ejemplo", Dia17)));

        Assert.Equal("hora", error.Campo);
        Assert.Equal(creado, await _recordatorios.ObtenerAsync(creado.Id));
    }

    [Fact]
    public async Task Editar_UnaEntrevistaYaGuardadaALasNueve_SigueFuncionandoConSuHora()
    {
        // Como las apuntadas antes de que la hora fuera obligatoria: a las 09:00 de Madrid (08:00Z en noviembre)
        int id;
        await using (var db = CreateContext())
        {
            var antigua = new Recordatorio
            {
                Tipo = TipoRecordatorio.Entrevista,
                Titulo = "Entrevista en Consultora Ejemplo",
                FechaHora = new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc),
            };
            db.Recordatorios.Add(antigua);
            await db.SaveChangesAsync();
            id = antigua.Id;
        }

        // El diálogo manda la hora que ya tenía
        var editada = (await _recordatorios.EditarAsync(id,
            new GuardarRecordatorioRequest(TipoRecordatorio.Entrevista, "Entrevista con RR. HH.", Dia17, "09:00", "Por Teams")))!;

        Assert.Equal("Entrevista con RR. HH.", editada.Titulo);
        Assert.Equal("Por Teams", editada.Notas);
        Assert.Equal(new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc), editada.FechaHora);
        // Y lo demás (hecho, posponer) no pasa por esa validación
        Assert.True((await _recordatorios.MarcarHechoAsync(id, new MarcarHechoRequest(true)))!.Hecho);
    }

    [Fact]
    public async Task Editar_ALaHoraQueNoExisteElDiaDelSaltoDeMarzo_LanzaValidacionDeLaHoraYNoCambiaNada()
    {
        var creado = await CrearAsync();

        var error = await Assert.ThrowsAsync<ValidacionException>(() => _recordatorios.EditarAsync(creado.Id,
            new GuardarRecordatorioRequest(TipoRecordatorio.Otro, "Renovar la demanda", new DateOnly(2027, 3, 28), "02:30")));

        Assert.Equal("hora", error.Campo);
        Assert.Equal(creado, await _recordatorios.ObtenerAsync(creado.Id));
    }

    [Fact]
    public async Task Editar_QueNoExiste_DevuelveNull()
    {
        Assert.Null(await _recordatorios.EditarAsync(999, Post()));
    }

    [Fact]
    public async Task Editar_ConDatosMalos_LanzaValidacionYNoCambiaNada()
    {
        var creado = await CrearAsync();

        await Assert.ThrowsAsync<ValidacionException>(() => _recordatorios.EditarAsync(creado.Id, Post(titulo: "")));

        Assert.Equal(creado, await _recordatorios.ObtenerAsync(creado.Id));
    }

    // ---------------------------------------------------------------- hecho y pendiente

    [Fact]
    public async Task MarcarHecho_LoCierraConLaFechaDeAhora_YRepetirloNoLaCambia()
    {
        var creado = await CrearAsync();
        var primera = Reloj.AhoraUtc;

        var hecho = (await _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true)))!;
        Reloj.Avanzar(TimeSpan.FromMinutes(5));
        var repetido = (await _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true)))!;

        Assert.True(hecho.Hecho);
        Assert.Equal(primera, hecho.FechaHecho);
        Assert.Equal(primera, repetido.FechaHecho);
        Assert.Equal(primera, (await EnBdAsync(creado.Id)).FechaHecho);
    }

    [Fact]
    public async Task MarcarHecho_AFalse_VuelveAEstarPendienteSinFecha()
    {
        var creado = await CrearAsync();
        await _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true));

        var pendiente = (await _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(false)))!;

        Assert.False(pendiente.Hecho);
        Assert.Null(pendiente.FechaHecho);
        Assert.False((await EnBdAsync(creado.Id)).Hecho);
    }

    [Fact]
    public async Task MarcarHecho_QueNoExisteOSinValor()
    {
        Assert.Null(await _recordatorios.MarcarHechoAsync(999, new MarcarHechoRequest(true)));
        var error = await Assert.ThrowsAsync<ValidacionException>(() =>
            _recordatorios.MarcarHechoAsync(999, new MarcarHechoRequest(null)));
        Assert.Equal("hecho", error.Campo);
    }

    // ---------------------------------------------------------------- posponer

    [Fact]
    public async Task Posponer_LoPasaAlDiaIndicadoALaMismaHoraDeMadrid()
    {
        var creado = (await _recordatorios.CrearAsync(Post(hora: "15:00")))!;

        var pospuesto = (await _recordatorios.PosponerAsync(creado.Id, new PosponerRecordatorioRequest(new DateOnly(2026, 11, 18))))!;

        Assert.Equal(new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc), pospuesto.FechaHora);
        Assert.Equal(pospuesto.FechaHora, (await EnBdAsync(creado.Id)).FechaHora);
    }

    [Fact]
    public async Task Posponer_AlDiaDelSaltoDeMarzo_UnaHoraQueNoExiste_PasaALasTres()
    {
        var creado = (await _recordatorios.CrearAsync(
            new GuardarRecordatorioRequest(TipoRecordatorio.Otro, "Renovar la demanda", new DateOnly(2027, 3, 27), "02:30")))!;

        var pospuesto = (await _recordatorios.PosponerAsync(creado.Id, new PosponerRecordatorioRequest(new DateOnly(2027, 3, 28))))!;

        Assert.Equal(new DateTime(2027, 3, 28, 1, 0, 0, DateTimeKind.Utc), pospuesto.FechaHora); // 03:00 de verano
        Assert.Equal(pospuesto.FechaHora, (await EnBdAsync(creado.Id)).FechaHora);
    }

    [Fact]
    public async Task Posponer_AlgoHecho_LanzaReglaNegocioYNoLoMueve()
    {
        var creado = await CrearAsync();
        await _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true));

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            _recordatorios.PosponerAsync(creado.Id, new PosponerRecordatorioRequest(new DateOnly(2026, 11, 18))));

        Assert.Equal("Ya está hecho. Márcalo como pendiente para moverlo.", error.Message);
        Assert.Equal(creado.FechaHora, (await EnBdAsync(creado.Id)).FechaHora);
    }

    [Fact]
    public async Task Posponer_QueNoExisteSinDiaOConUnDiaImposible()
    {
        Assert.Null(await _recordatorios.PosponerAsync(999, new PosponerRecordatorioRequest(new DateOnly(2026, 11, 18))));
        var sinDia = await Assert.ThrowsAsync<ValidacionException>(() =>
            _recordatorios.PosponerAsync(999, new PosponerRecordatorioRequest(null)));
        var imposible = await Assert.ThrowsAsync<ValidacionException>(() =>
            _recordatorios.PosponerAsync(999, new PosponerRecordatorioRequest(new DateOnly(1999, 1, 1))));
        Assert.Equal("dia", sinDia.Campo);
        Assert.Equal("dia", imposible.Campo);
    }

    // ---------------------------------------------------------------- borrar

    [Fact]
    public async Task Borrar_ExisteYLuegoYaNo()
    {
        var creado = await CrearAsync();

        Assert.True(await _recordatorios.BorrarAsync(creado.Id));
        Assert.False(await _recordatorios.BorrarAsync(creado.Id));
        Assert.Null(await _recordatorios.ObtenerAsync(creado.Id));
    }

    [Fact]
    public async Task Borrar_ElSeguimientoQueApuntaElHistorial_DeshacerElEnvioDespuesNoFalla()
    {
        var candidatura = await CrearCandidaturaAsync();
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var enviada = (await _candidaturas.CambiarEstadoAsync(candidatura.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada)))!;
        var seguimiento = Assert.Single(enviada.Recordatorios);

        Assert.True(await _recordatorios.BorrarAsync(seguimiento.Id));
        Reloj.Avanzar(TimeSpan.FromSeconds(30));
        var deshecha = (await _candidaturas.DeshacerCambioEstadoAsync(candidatura.Id,
            new DeshacerCambioEstadoRequest(enviada.Historial[^1].Id)))!;

        Assert.Equal(EstadoCandidatura.Detectada, deshecha.Estado);
        Assert.Empty(deshecha.Recordatorios);
    }

    [Fact]
    public async Task Borrar_OtraPeticionLoBorraEntreMedias_DevuelveFalse()
    {
        var creado = await CrearAsync();
        var lenta = NuevoRecordatorios(FactoryAntesDeGuardar(() => _recordatorios.BorrarAsync(creado.Id)));

        Assert.False(await lenta.BorrarAsync(creado.Id));
    }

    // ---------------------------------------------------------------- concurrencia

    [Fact]
    public async Task Posponer_OtraPeticionLoMarcaHechoEntreMedias_ChocaConElMensajeDelRecordatorio()
    {
        var creado = await CrearAsync();
        var lenta = NuevoRecordatorios(FactoryAntesDeGuardar(() => _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true))));

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            lenta.PosponerAsync(creado.Id, new PosponerRecordatorioRequest(new DateOnly(2026, 11, 18))));

        Assert.Equal(RecordatoriosService.MensajeCambiadoEnOtraPestana, error.Message);
        var enBd = await EnBdAsync(creado.Id);
        Assert.True(enBd.Hecho);
        Assert.Equal(creado.FechaHora, enBd.FechaHora);
    }

    [Fact]
    public async Task MarcarHecho_OtraPeticionLoPosponeEntreMedias_Choca()
    {
        var creado = await CrearAsync();
        var lenta = NuevoRecordatorios(FactoryAntesDeGuardar(() => _recordatorios.PosponerAsync(creado.Id, new PosponerRecordatorioRequest(new DateOnly(2026, 11, 18)))));

        await Assert.ThrowsAsync<ReglaNegocioException>(() => lenta.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true)));

        Assert.False((await EnBdAsync(creado.Id)).Hecho);
    }

    [Fact]
    public async Task MarcarHecho_OtraPeticionLoMarcaHechoEntreMedias_DevuelveElHechoSinChocar()
    {
        var creado = await CrearAsync();
        var lenta = NuevoRecordatorios(FactoryAntesDeGuardar(() => _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true))));
        Reloj.Avanzar(TimeSpan.FromMinutes(1)); // la que llega antes es la que manda

        var devuelto = (await lenta.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true)))!;

        Assert.True(devuelto.Hecho);
        var enBd = await EnBdAsync(creado.Id);
        Assert.Equal(enBd.FechaHecho, devuelto.FechaHecho);
    }

    [Fact]
    public async Task MarcarPendiente_OtraPeticionLoDejaPendienteEntreMedias_DevuelveElPendienteSinChocar()
    {
        var creado = await CrearAsync();
        await _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true));
        var lenta = NuevoRecordatorios(FactoryAntesDeGuardar(() => _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(false))));

        var devuelto = (await lenta.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(false)))!;

        Assert.False(devuelto.Hecho);
        Assert.Null(devuelto.FechaHecho);
    }

    [Fact]
    public async Task Editar_OtraPeticionLoBorraEntreMedias_DevuelveNull()
    {
        var creado = await CrearAsync();
        var lenta = NuevoRecordatorios(FactoryAntesDeGuardar(() => _recordatorios.BorrarAsync(creado.Id)));

        Assert.Null(await lenta.EditarAsync(creado.Id, Post(titulo: "Otro título", hora: "12:00")));
    }

    [Fact]
    public async Task Borrar_OtraPeticionLoCambiaEntreMedias_ChocaYNoLoBorra()
    {
        var creado = await CrearAsync();
        var lenta = NuevoRecordatorios(FactoryAntesDeGuardar(() => _recordatorios.MarcarHechoAsync(creado.Id, new MarcarHechoRequest(true))));

        await Assert.ThrowsAsync<ReglaNegocioException>(() => lenta.BorrarAsync(creado.Id));

        Assert.NotNull(await _recordatorios.ObtenerAsync(creado.Id));
    }
}
