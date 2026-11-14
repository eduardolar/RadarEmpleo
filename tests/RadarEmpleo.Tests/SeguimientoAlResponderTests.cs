using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests;

/// <summary>
/// El seguimiento al pasar a Entrevista u Oferta (<see cref="Candidatura.CambiarEstado"/>) y su vuelta con
/// «Deshacer», en el dominio y con el servicio sobre SQLite en memoria. Datos inventados.
/// </summary>
public class SeguimientoAlResponderTests : ArnesSqliteTests
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    private readonly CandidaturasService _candidaturas;

    public SeguimientoAlResponderTests()
    {
        Reloj.AhoraUtc = Ahora;
        _candidaturas = new CandidaturasService(Factory, Reloj, Exportador);
    }

    private static Candidatura Nueva() =>
        Candidatura.Nueva(new Empresa { Nombre = "Acme Software", NombreNormalizado = "acme software" },
            "Desarrollador .NET", Ahora, Origen.Manual);

    private static Recordatorio Pendiente(TipoRecordatorio tipo) =>
        new() { Tipo = tipo, Titulo = tipo.ToString(), FechaHora = Ahora.AddDays(3) };

    [Theory]
    [InlineData(EstadoCandidatura.Entrevista)]
    [InlineData(EstadoCandidatura.Oferta)]
    public void CambiarEstado_DeEnviadaAEntrevistaUOferta_CierraElSeguimientoYLoApunta(EstadoCandidatura destino)
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Manual);
        var seguimiento = Assert.Single(c.Recordatorios);
        var respuesta = Ahora.AddDays(2);

        var cambio = c.CambiarEstado(destino, respuesta, Origen.Manual);

        Assert.True(seguimiento.Hecho);
        Assert.Equal(respuesta, seguimiento.FechaHecho);
        Assert.Same(seguimiento, cambio.SeguimientoCerrado);
        Assert.Null(cambio.SeguimientoCreado);
        Assert.Single(c.Recordatorios); // no se programa ningún seguimiento nuevo
        Assert.Equal(Ahora, c.FechaEnvio);
    }

    [Fact]
    public void CambiarEstado_DeEntrevistaAOfertaSinNadaPendiente_NoTocaNada()
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Manual);
        c.CambiarEstado(EstadoCandidatura.Entrevista, Ahora.AddDays(1), Origen.Manual);
        var seguimiento = Assert.Single(c.Recordatorios);

        var cambio = c.CambiarEstado(EstadoCandidatura.Oferta, Ahora.AddDays(5), Origen.Manual);

        Assert.Null(cambio.SeguimientoCerrado);
        Assert.Equal(Ahora.AddDays(1), seguimiento.FechaHecho);
    }

    [Fact]
    public void CambiarEstado_DePreparadaAEntrevista_RellenaFechaEnvioSinCrearNiCerrarNada()
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Preparada, Ahora, Origen.Manual);

        var cambio = c.CambiarEstado(EstadoCandidatura.Entrevista, Ahora.AddDays(1), Origen.Manual);

        Assert.Equal(Ahora.AddDays(1), c.FechaEnvio);
        Assert.Empty(c.Recordatorios);
        Assert.Null(cambio.SeguimientoCreado);
        Assert.Null(cambio.SeguimientoCerrado);
    }

    [Fact]
    public void CambiarEstado_AEntrevista_NoCierraUnaEntrevistaNiUnPlazoPendientes()
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Manual);
        var plazo = Pendiente(TipoRecordatorio.Plazo);
        c.Recordatorios.Add(plazo);

        c.CambiarEstado(EstadoCandidatura.Entrevista, Ahora.AddDays(1), Origen.Manual);

        Assert.False(plazo.Hecho);
    }

    [Fact]
    public void CambiarEstado_Descartar_CierraElSeguimientoPeroNoLaEntrevistaNiElPlazo()
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Manual);
        var seguimiento = Assert.Single(c.Recordatorios);
        var entrevista = Pendiente(TipoRecordatorio.Entrevista);
        var plazo = Pendiente(TipoRecordatorio.Plazo);
        c.Recordatorios.Add(entrevista);
        c.Recordatorios.Add(plazo);

        c.CambiarEstado(EstadoCandidatura.Descartada, Ahora.AddDays(1), Origen.Manual, MotivoDescarte.Zona);

        Assert.True(seguimiento.Hecho);
        Assert.Equal(Ahora.AddDays(1), seguimiento.FechaHecho);
        Assert.False(entrevista.Hecho);
        Assert.False(plazo.Hecho);
    }

    [Fact]
    public async Task Deshacer_ElPasoAEntrevista_ElSeguimientoVuelveAEstarPendienteSinFechaDeHecho()
    {
        var creada = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme Software S.L.", "Desarrollador .NET"));
        await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));
        var envio = Reloj.AhoraUtc;
        Reloj.Avanzar(TimeSpan.FromDays(2));
        var enEntrevista = (await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Entrevista)))!;
        var cerrado = Assert.Single(enEntrevista.Recordatorios);
        Assert.True(cerrado.Hecho);
        Assert.Equal(Reloj.AhoraUtc, cerrado.FechaHecho);

        Reloj.Avanzar(TimeSpan.FromSeconds(30));
        var deshecha = (await _candidaturas.DeshacerCambioEstadoAsync(creada.Id,
            new DeshacerCambioEstadoRequest(enEntrevista.Historial[^1].Id)))!;

        Assert.Equal(EstadoCandidatura.Enviada, deshecha.Estado);
        Assert.Equal(envio, deshecha.FechaEnvio);
        await using var db = CreateContext();
        var seguimiento = await db.Recordatorios.AsNoTracking().SingleAsync();
        Assert.False(seguimiento.Hecho);
        Assert.Null(seguimiento.FechaHecho);
    }

    [Fact]
    public async Task Deshacer_EntrevistaSinPasarPorEnviada_FechaEnvioVuelveANull()
    {
        var creada = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme Software S.L.", "Desarrollador .NET"));
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        var enEntrevista = (await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Entrevista)))!;

        Reloj.Avanzar(TimeSpan.FromSeconds(30));
        var deshecha = (await _candidaturas.DeshacerCambioEstadoAsync(creada.Id,
            new DeshacerCambioEstadoRequest(enEntrevista.Historial[^1].Id)))!;

        Assert.Null(deshecha.FechaEnvio);
        Assert.Empty(deshecha.Recordatorios);
    }

    [Fact]
    public async Task CambiarEstado_OtraPeticionMarcaElSeguimientoEntreMedias_ChocaYNoCambiaElEstado()
    {
        var creada = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Acme Software S.L.", "Desarrollador .NET"));
        var enviada = (await _candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada)))!;
        var seguimientoId = Assert.Single(enviada.Recordatorios).Id;
        var recordatorios = NuevoRecordatorios();
        var lenta = new CandidaturasService(
            FactoryAntesDeGuardar(() => recordatorios.MarcarHechoAsync(seguimientoId, new MarcarHechoRequest(true))),
            Reloj, Exportador);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            lenta.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Entrevista)));

        Assert.Equal(EstadoCandidatura.Enviada, (await _candidaturas.ObtenerAsync(creada.Id))!.Estado);
    }
}
