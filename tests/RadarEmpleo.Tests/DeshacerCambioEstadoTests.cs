using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests;

/// <summary>
/// Deshacer un cambio de estado (<see cref="CandidaturasService.DeshacerCambioEstadoAsync"/> y
/// <see cref="Candidatura.DeshacerUltimoCambio"/>) sobre SQLite en memoria con el reloj fijo. Datos inventados.
/// </summary>
public class DeshacerCambioEstadoTests : ArnesSqliteTests
{
    private readonly CandidaturasService _candidaturas;

    public DeshacerCambioEstadoTests()
    {
        _candidaturas = new CandidaturasService(Factory, Reloj, Exportador);
    }

    private Task<CandidaturaDetalleDto> CrearAsync(DateTime? fechaDeteccion = null) =>
        _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme Software S.L.", "Desarrollador .NET",
            FechaDeteccion: fechaDeteccion));

    /// <summary>Mueve un minuto después del anterior y devuelve el id del apunte que ha creado (el del aviso).</summary>
    private async Task<int> CambiarAsync(int id, EstadoCandidatura estado, MotivoDescarte? motivo = null, string? texto = null)
    {
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var detalle = await _candidaturas.CambiarEstadoAsync(id, new CambiarEstadoRequest(estado, motivo, texto));
        return detalle!.Historial[^1].Id;
    }

    private async Task<CandidaturaDetalleDto> DeshacerAsync(int id, int cambioId)
    {
        Reloj.Avanzar(TimeSpan.FromSeconds(30));
        return (await _candidaturas.DeshacerCambioEstadoAsync(id, new DeshacerCambioEstadoRequest(cambioId)))!;
    }

    private Task<CandidaturaDetalleDto?> DeshacerSinAvanzarAsync(int id, int cambioId) =>
        _candidaturas.DeshacerCambioEstadoAsync(id, new DeshacerCambioEstadoRequest(cambioId));

    private async Task<List<Recordatorio>> RecordatoriosAsync()
    {
        await using var db = CreateContext();
        return await db.Recordatorios.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }

    private async Task EditarRecordatorioAsync(int id, Action<Recordatorio> cambio)
    {
        await using var db = CreateContext();
        cambio(await db.Recordatorios.SingleAsync(r => r.Id == id));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task EnviadaAPreparada_VuelveAPreparadaSinFechaEnvioNiSeguimiento()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, EstadoCandidatura.Preparada);
        var fechaPreparada = Reloj.AhoraUtc;
        var envio = await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);

        var detalle = await DeshacerAsync(creada.Id, envio);

        Assert.Equal(EstadoCandidatura.Preparada, detalle.Estado);
        Assert.Null(detalle.FechaEnvio);
        Assert.Equal(fechaPreparada, detalle.FechaEstado);
        Assert.Equal(Reloj.AhoraUtc, detalle.FechaUltimoCambio);
        Assert.Null((await _candidaturas.ObtenerAsync(creada.Id))!.FechaEnvio);
        Assert.Empty(await RecordatoriosAsync());
    }

    [Fact]
    public async Task ReenvioTrasReabrir_FechaEnvioVuelveALaDelPrimerEnvio()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);
        var primerEnvio = Reloj.AhoraUtc;
        await CambiarAsync(creada.Id, EstadoCandidatura.Cerrada);
        await CambiarAsync(creada.Id, EstadoCandidatura.Detectada);
        var reenvio = await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);

        var detalle = await DeshacerAsync(creada.Id, reenvio);

        Assert.Equal(EstadoCandidatura.Detectada, detalle.Estado);
        Assert.Equal(primerEnvio, detalle.FechaEnvio);
        // El seguimiento del primer envío sigue hecho (lo cerró «Cerrada»); el del reenvío se ha quitado.
        Assert.True(Assert.Single(detalle.Recordatorios).Hecho);
    }

    [Fact]
    public async Task Descartar_BorraElMotivoYReabreElSeguimiento()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);
        var envio = Reloj.AhoraUtc;
        var descarte = await CambiarAsync(creada.Id, EstadoCandidatura.Descartada, MotivoDescarte.Ingles, "Piden C1");

        var detalle = await DeshacerAsync(creada.Id, descarte);

        Assert.Equal(EstadoCandidatura.Enviada, detalle.Estado);
        Assert.Null(detalle.MotivoDescarte);
        Assert.Null(detalle.MotivoDescarteTexto);
        Assert.Equal(envio, detalle.FechaEstado);
        Assert.Equal(envio, detalle.FechaEnvio);
        Assert.False(Assert.Single(detalle.Recordatorios).Hecho);
    }

    [Fact]
    public async Task Reabrir_VuelveADescartadaConSuMotivo()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, EstadoCandidatura.Descartada, MotivoDescarte.Zona, "Solo presencial en Bilbao");
        var descarte = Reloj.AhoraUtc;
        var reabrir = await CambiarAsync(creada.Id, EstadoCandidatura.Detectada);

        var detalle = await DeshacerAsync(creada.Id, reabrir);

        Assert.Equal(EstadoCandidatura.Descartada, detalle.Estado);
        Assert.Equal(MotivoDescarte.Zona, detalle.MotivoDescarte);
        Assert.Equal("Solo presencial en Bilbao", detalle.MotivoDescarteTexto);
        Assert.Equal(descarte, detalle.FechaEstado);
    }

    [Fact]
    public async Task HastaElAlta_FechaEstadoVuelveALaDeteccion()
    {
        var deteccion = AhoraInicial.AddDays(-3);
        var creada = await CrearAsync(deteccion);
        var preparada = await CambiarAsync(creada.Id, EstadoCandidatura.Preparada);

        var detalle = await DeshacerAsync(creada.Id, preparada);

        Assert.Equal(EstadoCandidatura.Detectada, detalle.Estado);
        Assert.Equal(deteccion, detalle.FechaEstado);
    }

    [Fact]
    public async Task DosCambiosDeshechosUnoTrasOtro_VuelveAlPrincipio()
    {
        var creada = await CrearAsync();
        var preparada = await CambiarAsync(creada.Id, EstadoCandidatura.Preparada);
        var enviada = await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);

        await DeshacerAsync(creada.Id, enviada);
        var detalle = await DeshacerAsync(creada.Id, preparada);

        Assert.Equal(EstadoCandidatura.Detectada, detalle.Estado);
        Assert.Null(Assert.Single(detalle.Historial).De);
    }

    [Fact]
    public async Task ElMismoCambioDosVeces_LaSegundaDa409YNoDeshaceElAnterior()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, EstadoCandidatura.Preparada);
        var enviada = await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);
        await DeshacerAsync(creada.Id, enviada);

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => DeshacerSinAvanzarAsync(creada.Id, enviada));

        Assert.Equal("Ese cambio ya no es el último.", ex.Message);
        Assert.Equal(EstadoCandidatura.Preparada, (await _candidaturas.ObtenerAsync(creada.Id))!.Estado);
    }

    [Fact]
    public async Task DosPestanas_ElDeshacerDeLaPrimeraTrasMoverLaSegunda_Da409SinTocarNada()
    {
        var creada = await CrearAsync();
        var deLaPestanaA = await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);
        await CambiarAsync(creada.Id, EstadoCandidatura.Entrevista);
        var llamadas = Exportador.Llamadas;

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => DeshacerSinAvanzarAsync(creada.Id, deLaPestanaA));

        Assert.Equal("Ese cambio ya no es el último.", ex.Message);
        var ficha = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(EstadoCandidatura.Entrevista, ficha.Estado);
        Assert.Equal(3, ficha.Historial.Count);
        Assert.Single(ficha.Recordatorios, r => !r.Hecho);
        Assert.Equal(llamadas, Exportador.Llamadas);
    }

    [Fact]
    public async Task DosDeshacerALaVez_SoloDeshaceUnPaso()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, EstadoCandidatura.Preparada);
        var enviada = await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);
        Reloj.Avanzar(TimeSpan.FromSeconds(10));

        var resultados = await Task.WhenAll(
            Intentar(() => DeshacerSinAvanzarAsync(creada.Id, enviada)),
            Intentar(() => DeshacerSinAvanzarAsync(creada.Id, enviada)));

        Assert.Single(resultados, r => r is null);
        Assert.Single(resultados, r => r is ReglaNegocioException or DbUpdateConcurrencyException);
        var ficha = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(EstadoCandidatura.Preparada, ficha.Estado);
        Assert.Equal(2, ficha.Historial.Count);
    }

    private static async Task<Exception?> Intentar(Func<Task> accion)
    {
        try
        {
            await Task.Run(accion);
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    [Fact]
    public async Task LaFilaDelHistorialDesaparece()
    {
        var creada = await CrearAsync();
        var entrevista = await CambiarAsync(creada.Id, EstadoCandidatura.Entrevista);

        var detalle = await DeshacerAsync(creada.Id, entrevista);

        Assert.DoesNotContain(detalle.Historial, h => h.A == EstadoCandidatura.Entrevista);
        await using var db = CreateContext();
        var fila = Assert.Single(await db.CambiosEstado.Where(h => h.CandidaturaId == creada.Id).ToListAsync());
        Assert.Null(fila.De);
        Assert.Null(detalle.FechaEnvio);
    }

    [Fact]
    public async Task SinCambios_LanzaReglaNegocioYNoTocaNada()
    {
        var creada = await CrearAsync();
        var llamadas = Exportador.Llamadas;

        var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() =>
            DeshacerSinAvanzarAsync(creada.Id, creada.Historial[0].Id));

        Assert.Equal("No hay ningún cambio de estado que deshacer.", ex.Message);
        Assert.Single((await _candidaturas.ObtenerAsync(creada.Id))!.Historial);
        Assert.Equal(llamadas, Exportador.Llamadas);
    }

    [Fact]
    public async Task SinCambioId_LanzaValidacion()
    {
        var creada = await CrearAsync();

        var ex = await Assert.ThrowsAsync<ValidacionException>(() =>
            _candidaturas.DeshacerCambioEstadoAsync(creada.Id, new DeshacerCambioEstadoRequest(null)));

        Assert.Equal("cambioId", ex.Campo);
    }

    [Theory]
    [InlineData(10 * 60, true)]
    [InlineData(10 * 60 - 1, false)]
    public async Task VentanaDeDiezMinutos(int segundos, bool caducado)
    {
        var creada = await CrearAsync();
        var preparada = await CambiarAsync(creada.Id, EstadoCandidatura.Preparada);
        Reloj.Avanzar(TimeSpan.FromSeconds(segundos));

        if (caducado)
        {
            var ex = await Assert.ThrowsAsync<ReglaNegocioException>(() => DeshacerSinAvanzarAsync(creada.Id, preparada));
            Assert.Equal("Ya no se puede deshacer este cambio.", ex.Message);
            Assert.Equal(EstadoCandidatura.Preparada, (await _candidaturas.ObtenerAsync(creada.Id))!.Estado);
        }
        else
        {
            var detalle = await DeshacerSinAvanzarAsync(creada.Id, preparada);
            Assert.Equal(EstadoCandidatura.Detectada, detalle!.Estado);
        }
    }

    [Fact]
    public async Task Inexistente_DevuelveNull()
    {
        Assert.Null(await DeshacerSinAvanzarAsync(999, 1));
    }

    [Fact]
    public async Task ExportaElRadar()
    {
        var creada = await CrearAsync();
        var preparada = await CambiarAsync(creada.Id, EstadoCandidatura.Preparada);
        var llamadas = Exportador.Llamadas;

        await DeshacerAsync(creada.Id, preparada);

        Assert.Equal(llamadas + 1, Exportador.Llamadas);
    }

    [Fact]
    public async Task OtraPeticionLaCambiaEntreMedias_ChocaYNoDeshaceNada()
    {
        var creada = await CrearAsync();
        var preparada = await CambiarAsync(creada.Id, EstadoCandidatura.Preparada);
        Reloj.Avanzar(TimeSpan.FromSeconds(10));
        var lenta = new CandidaturasService(FactoryAntesDeGuardar(() =>
            _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada))), Reloj, Exportador);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            lenta.DeshacerCambioEstadoAsync(creada.Id, new DeshacerCambioEstadoRequest(preparada)));

        var ficha = (await _candidaturas.ObtenerAsync(creada.Id))!;
        Assert.Equal(EstadoCandidatura.Enviada, ficha.Estado);
        Assert.Equal(3, ficha.Historial.Count);
    }

    // ---------------------------------------------------------------- seguimientos, por id y no por hora

    [Fact]
    public async Task Seguimiento_ElHistorialApuntaElQueCreaYElQueCierra()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);
        await CambiarAsync(creada.Id, EstadoCandidatura.Cerrada);

        await using var db = CreateContext();
        var historial = await db.CambiosEstado.AsNoTracking().OrderBy(h => h.Id).ToListAsync();
        var seguimiento = Assert.Single(await RecordatoriosAsync());
        Assert.Equal(seguimiento.Id, historial[1].SeguimientoCreadoId);
        Assert.Equal(seguimiento.Id, historial[2].SeguimientoCerradoId);
        Assert.Null(historial[2].SeguimientoCreadoId);
    }

    [Fact]
    public async Task Seguimiento_MarcadoHechoAMano_NoSeReabreAlDeshacerElDescarte()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);
        var seguimiento = Assert.Single(await RecordatoriosAsync());
        await EditarRecordatorioAsync(seguimiento.Id, r => r.Hecho = true);
        var descarte = await CambiarAsync(creada.Id, EstadoCandidatura.Descartada, MotivoDescarte.Salario);

        await DeshacerAsync(creada.Id, descarte);

        Assert.True(Assert.Single(await RecordatoriosAsync()).Hecho);
    }

    [Fact]
    public async Task Seguimiento_DosEnviosElMismoDia_SoloReabreElDelCambioDeshecho()
    {
        var creada = await CrearAsync();
        await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);
        await CambiarAsync(creada.Id, EstadoCandidatura.Cerrada);
        await CambiarAsync(creada.Id, EstadoCandidatura.Detectada);
        await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);
        var cierre = await CambiarAsync(creada.Id, EstadoCandidatura.Cerrada);
        var antes = await RecordatoriosAsync();
        Assert.Equal(2, antes.Count);
        Assert.Equal(antes[0].FechaHora, antes[1].FechaHora); // misma hora: por hora no se distinguirían

        await DeshacerAsync(creada.Id, cierre);

        var despues = await RecordatoriosAsync();
        Assert.True(despues[0].Hecho);
        Assert.False(despues[1].Hecho);
    }

    [Fact]
    public async Task Seguimiento_ConLaHoraCambiada_DeshacerElEnvioLoQuitaIgual()
    {
        var creada = await CrearAsync();
        var envio = await CambiarAsync(creada.Id, EstadoCandidatura.Enviada);
        var seguimiento = Assert.Single(await RecordatoriosAsync());
        await EditarRecordatorioAsync(seguimiento.Id, r => r.FechaHora = r.FechaHora.AddDays(2).AddHours(3));

        await DeshacerAsync(creada.Id, envio);

        Assert.Empty(await RecordatoriosAsync());
    }

    // ---------------------------------------------------------------- dominio sin BD

    [Fact]
    public void Dominio_ElDescarteGuardaSuMotivoEnElHistorial()
    {
        var c = Candidatura.Nueva(new Empresa { Nombre = "Acme", NombreNormalizado = "acme" }, "Backend", AhoraInicial, Origen.Manual);

        var cambio = c.CambiarEstado(EstadoCandidatura.Descartada, AhoraInicial, Origen.Manual, MotivoDescarte.Salario, "Muy bajo");

        Assert.Equal(MotivoDescarte.Salario, cambio.MotivoDescarte);
        Assert.Equal("Muy bajo", cambio.MotivoDescarteTexto);
    }

    [Fact]
    public void Dominio_ReabrirUnDescarteSinMotivoGuardado_NoSeDeshace()
    {
        var c = Candidatura.Nueva(new Empresa { Nombre = "Acme", NombreNormalizado = "acme" }, "Backend", AhoraInicial, Origen.Manual);
        c.CambiarEstado(EstadoCandidatura.Descartada, AhoraInicial, Origen.Manual, MotivoDescarte.Otro);
        // Como un descarte de antes de guardar el motivo en el historial.
        c.Historial.Last().MotivoDescarte = null;
        var reabrir = c.CambiarEstado(EstadoCandidatura.Detectada, AhoraInicial.AddMinutes(1), Origen.Manual);

        var ex = Assert.Throws<ReglaNegocioException>(() => c.DeshacerUltimoCambio(reabrir.Id, AhoraInicial.AddMinutes(2)));

        Assert.Contains("motivo", ex.Message);
        Assert.Equal(EstadoCandidatura.Detectada, c.Estado);
        Assert.Equal(3, c.Historial.Count);
    }
}
