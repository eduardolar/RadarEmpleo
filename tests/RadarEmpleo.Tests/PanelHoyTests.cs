using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="RecordatoriosService.PanelHoyAsync"/> sobre SQLite en memoria, con el reloj fijo el viernes
/// 13/11/2026 a las 10:00 de Madrid (09:00Z). Datos inventados.
/// </summary>
public class PanelHoyTests : ArnesSqliteTests
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    private readonly RecordatoriosService _recordatorios;

    public PanelHoyTests()
    {
        Reloj.AhoraUtc = Ahora;
        _recordatorios = NuevoRecordatorios();
    }

    private static DateTime Utc(int mes, int dia, int hora, int minuto = 0) =>
        new(2026, mes, dia, hora, minuto, 0, DateTimeKind.Utc);

    private async Task<int> SembrarAsync(string titulo, DateTime fechaHora, Candidatura? candidatura = null,
        DateTime? hechoEn = null, TipoRecordatorio tipo = TipoRecordatorio.Otro)
    {
        await using var db = CreateContext();
        var recordatorio = new Recordatorio { Titulo = titulo, FechaHora = fechaHora, Tipo = tipo, CandidaturaId = candidatura?.Id };
        if (hechoEn is { } fecha) recordatorio.MarcarHecho(fecha);
        db.Recordatorios.Add(recordatorio);
        await db.SaveChangesAsync();
        return recordatorio.Id;
    }

    /// <summary>Candidatura llevada por los estados indicados; al enviar no programa seguimiento salvo que se pida.</summary>
    private async Task<Candidatura> CandidaturaAsync(string empresa, DateTime fecha, bool conSeguimiento, params EstadoCandidatura[] estados)
    {
        await using var db = CreateContext();
        var candidatura = Candidatura.Nueva(
            new Empresa { Nombre = empresa, NombreNormalizado = NombresEmpresa.Normalizar(empresa) },
            "Desarrollador .NET", fecha, Origen.Manual);
        foreach (var estado in estados)
        {
            candidatura.CambiarEstado(estado, fecha, Origen.Manual,
                estado == EstadoCandidatura.Descartada ? MotivoDescarte.Zona : null,
                programarSeguimiento: conSeguimiento);
        }
        db.Candidaturas.Add(candidatura);
        await db.SaveChangesAsync();
        return candidatura;
    }

    private Task<Candidatura> EnviadaAsync(string empresa, DateTime envio, bool conSeguimiento = false) =>
        CandidaturaAsync(empresa, envio, conSeguimiento, EstadoCandidatura.Enviada);

    private static IEnumerable<string> Titulos(IEnumerable<RecordatorioAgendaDto> lista) => lista.Select(r => r.Titulo);

    [Fact]
    public async Task SinNada_TodoVacioYHoyEsElDiaDeMadrid()
    {
        var panel = await _recordatorios.PanelHoyAsync();

        Assert.Equal(new DateOnly(2026, 11, 13), panel.Hoy);
        Assert.Empty(panel.ParaHoy);
        Assert.Empty(panel.Proximos);
        Assert.Empty(panel.MasAdelante);
        Assert.Empty(panel.HechosHoy);
        Assert.Empty(panel.SinRespuesta);
        Assert.Equal(0, panel.SinRespuestaAntiguas);
    }

    [Fact]
    public async Task BordesDelDia_CadaRecordatorioCaeEnSuLista()
    {
        await SembrarAsync("hoy 23:30", Utc(11, 13, 22, 30));
        await SembrarAsync("vencido", Utc(11, 10, 8));
        await SembrarAsync("mañana 00:00", Utc(11, 13, 23));
        await SembrarAsync("día 20 a las 23:59", Utc(11, 20, 22, 59));
        await SembrarAsync("día 21 a las 00:00", Utc(11, 20, 23));
        await SembrarAsync("diciembre", Utc(12, 1, 8));

        var panel = await _recordatorios.PanelHoyAsync();

        Assert.Equal(["vencido", "hoy 23:30"], Titulos(panel.ParaHoy));
        Assert.Equal(["mañana 00:00", "día 20 a las 23:59"], Titulos(panel.Proximos));
        Assert.Equal(["día 21 a las 00:00", "diciembre"], Titulos(panel.MasAdelante));
    }

    [Fact]
    public async Task Hechos_NoSalenEnLasPendientes_YSoloLosDeHoyEnHechosHoy()
    {
        await SembrarAsync("hecho hace un rato", Utc(11, 13, 8), hechoEn: Ahora.AddHours(-1));
        await SembrarAsync("hecho ahora", Utc(11, 20, 8), hechoEn: Ahora);
        await SembrarAsync("hecho ayer", Utc(11, 12, 8), hechoEn: Utc(11, 12, 22, 59));
        await SembrarAsync("hecho hoy a las 00:00", Utc(11, 12, 8), hechoEn: Utc(11, 12, 23));
        await SembrarAsync("pendiente", Utc(11, 13, 8));
        var sinFecha = await SembrarAsync("cerrado antes de la F6", Utc(11, 13, 8), hechoEn: Ahora);
        await using (var db = CreateContext())
        {
            // Como los que cerró la migración: hechos, pero sin fecha.
            (await db.Recordatorios.FindAsync(sinFecha))!.FechaHecho = null;
            await db.SaveChangesAsync();
        }

        var panel = await _recordatorios.PanelHoyAsync();

        Assert.Equal(["pendiente"], Titulos(panel.ParaHoy));
        Assert.Empty(panel.Proximos);
        Assert.Equal(["hecho ahora", "hecho hace un rato", "hecho hoy a las 00:00"], Titulos(panel.HechosHoy));
        Assert.All(panel.HechosHoy, r => Assert.True(r.Hecho));
    }

    [Fact]
    public async Task CandidaturaDescartadaOCerrada_SusRecordatoriosNoSalen_YLosDeUnaPreparadaSi()
    {
        var descartada = await CandidaturaAsync("Descartes S.L.", Ahora.AddDays(-2), false, EstadoCandidatura.Descartada);
        var cerrada = await CandidaturaAsync("Cierres S.L.", Ahora.AddDays(-2), false, EstadoCandidatura.Cerrada);
        var preparada = await CandidaturaAsync("Consultora Ejemplo", Ahora.AddDays(-2), false, EstadoCandidatura.Preparada);
        await SembrarAsync("plazo de la descartada", Utc(11, 13, 8), descartada, tipo: TipoRecordatorio.Plazo);
        await SembrarAsync("entrevista de la cerrada", Utc(11, 15, 8), cerrada, tipo: TipoRecordatorio.Entrevista);
        await SembrarAsync("hecho hoy de la descartada", Utc(11, 13, 8), descartada, hechoEn: Ahora);
        await SembrarAsync("plazo de la preparada", Utc(11, 13, 8), preparada, tipo: TipoRecordatorio.Plazo);
        await SembrarAsync("suelto", Utc(11, 13, 8, 30));

        var panel = await _recordatorios.PanelHoyAsync();

        Assert.Equal(["plazo de la preparada", "suelto"], Titulos(panel.ParaHoy));
        Assert.Empty(panel.Proximos);
        Assert.Empty(panel.HechosHoy);
        var ligado = panel.ParaHoy[0];
        Assert.Equal(preparada.Id, ligado.CandidaturaId);
        Assert.Equal(new CandidaturaBreveDto(preparada.Id, "Consultora Ejemplo", "Desarrollador .NET", EstadoCandidatura.Preparada, null), ligado.Candidatura);
        Assert.Equal(TipoRecordatorio.Plazo, ligado.Tipo);
        Assert.Null(panel.ParaHoy[1].Candidatura);
    }

    [Fact]
    public async Task CandidaturaReabierta_SusRecordatoriosVuelvenSolos()
    {
        var reabierta = await CandidaturaAsync("Vueltas S.L.", Ahora.AddDays(-2), false,
            EstadoCandidatura.Descartada, EstadoCandidatura.Detectada);
        await SembrarAsync("plazo", Utc(11, 13, 8), reabierta, tipo: TipoRecordatorio.Plazo);

        Assert.Equal(["plazo"], Titulos((await _recordatorios.PanelHoyAsync()).ParaHoy));
    }

    [Fact]
    public async Task SinRespuesta_SoloLasEnviadasDeSieteATreintaDiasSinSeguimientoPendiente()
    {
        var hace8 = await EnviadaAsync("Hace ocho", Utc(11, 5, 9));
        var hace7 = await EnviadaAsync("Hace siete", Utc(11, 6, 22, 30)); // 23:30 de Madrid del día 6
        var hace30 = await EnviadaAsync("Hace treinta", Utc(10, 14, 9));
        await EnviadaAsync("Hace seis", Utc(11, 7, 9));
        await EnviadaAsync("Hace treinta y uno", Utc(10, 13, 9));
        await EnviadaAsync("De agosto", Utc(8, 20, 9));
        await EnviadaAsync("Con seguimiento pendiente", Utc(11, 5, 9), conSeguimiento: true);
        await CandidaturaAsync("En entrevista", Utc(11, 5, 9), false, EstadoCandidatura.Enviada, EstadoCandidatura.Entrevista);
        await CandidaturaAsync("Descartada tras enviar", Utc(11, 5, 9), false, EstadoCandidatura.Enviada, EstadoCandidatura.Descartada);

        var panel = await _recordatorios.PanelHoyAsync();

        Assert.Equal(["Hace siete", "Hace ocho", "Hace treinta"], panel.SinRespuesta.Select(c => c.Empresa));
        Assert.Equal([7, 8, 30], panel.SinRespuesta.Select(c => c.DiasDesdeEnvio));
        Assert.Equal([hace7.Id, hace8.Id, hace30.Id], panel.SinRespuesta.Select(c => c.Id));
        Assert.Equal("Desarrollador .NET", panel.SinRespuesta[0].Puesto);
        Assert.Equal(Utc(11, 6, 22, 30), panel.SinRespuesta[0].FechaEnvio);
        Assert.Equal(2, panel.SinRespuestaAntiguas);
    }

    [Fact]
    public async Task SinRespuesta_ConElSeguimientoYaHecho_VuelveASalir()
    {
        var enviada = await EnviadaAsync("Consultora Ejemplo", Utc(11, 5, 9), conSeguimiento: true);
        await using (var db = CreateContext())
        {
            db.Recordatorios.Single(r => r.CandidaturaId == enviada.Id).MarcarHecho(Ahora);
            await db.SaveChangesAsync();
        }

        var panel = await _recordatorios.PanelHoyAsync();

        Assert.Equal(enviada.Id, Assert.Single(panel.SinRespuesta).Id);
        Assert.Single(panel.HechosHoy);
    }

    [Fact]
    public async Task HechosHoy_ElSeguimientoQueSeCerroAlPasarAEntrevista_LoDice_YElMarcadoAManoNo()
    {
        var candidaturas = new CandidaturasService(Factory, Reloj, Exportador);
        var creada = await candidaturas.CrearAsync(new GuardarCandidaturaRequest("Consultora Ejemplo", "Desarrollador .NET"));
        await candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada));
        var aMano = await SembrarAsync("Seguimiento a mano", Utc(11, 13, 8), tipo: TipoRecordatorio.Seguimiento);
        await _recordatorios.MarcarHechoAsync(aMano, new MarcarHechoRequest(true));
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Entrevista));

        var hechos = (await _recordatorios.PanelHoyAsync()).HechosHoy;

        Assert.Equal(2, hechos.Count);
        Assert.Equal(EstadoCandidatura.Entrevista, hechos.Single(r => r.CandidaturaId == creada.Id).CerradoAlPasarA);
        Assert.Null(hechos.Single(r => r.Id == aMano).CerradoAlPasarA);
    }

    [Fact]
    public async Task HechosHoy_SiSeDejoPendienteYSeVolvioAMarcarAMano_YaNoDiceQueSeCerroSolo()
    {
        var candidaturas = new CandidaturasService(Factory, Reloj, Exportador);
        var creada = await candidaturas.CrearAsync(new GuardarCandidaturaRequest("Consultora Ejemplo", "Desarrollador .NET"));
        var enviada = (await candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Enviada)))!;
        var seguimiento = Assert.Single(enviada.Recordatorios).Id;
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await candidaturas.CambiarEstadoAsync(creada.Id, new CambiarEstadoRequest(EstadoCandidatura.Oferta));
        Assert.Equal(EstadoCandidatura.Oferta, Assert.Single((await _recordatorios.PanelHoyAsync()).HechosHoy).CerradoAlPasarA);

        await _recordatorios.MarcarHechoAsync(seguimiento, new MarcarHechoRequest(false));
        Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await _recordatorios.MarcarHechoAsync(seguimiento, new MarcarHechoRequest(true));

        Assert.Null(Assert.Single((await _recordatorios.PanelHoyAsync()).HechosHoy).CerradoAlPasarA);
    }

    [Fact]
    public async Task DiaDeCambioDeHora_LosCortesSonLosDeMadrid()
    {
        // 25/10/2026 a las 00:30Z: en Madrid son las 02:30 (aún de verano) y el día tiene 25 horas.
        Reloj.AhoraUtc = Utc(10, 25, 0, 30);
        await SembrarAsync("ayer 23:59", Utc(10, 24, 21, 59));
        await SembrarAsync("hoy 00:00", Utc(10, 24, 22));
        await SembrarAsync("hoy 23:59", Utc(10, 25, 22, 59));
        await SembrarAsync("mañana 00:00", Utc(10, 25, 23));
        await SembrarAsync("día 1 a las 23:59", Utc(11, 1, 22, 59));
        await SembrarAsync("día 2 a las 00:00", Utc(11, 1, 23));
        await SembrarAsync("hecho hoy a las 23:30", Utc(10, 25, 8), hechoEn: Utc(10, 25, 22, 30));

        var panel = await _recordatorios.PanelHoyAsync();

        Assert.Equal(new DateOnly(2026, 10, 25), panel.Hoy);
        Assert.Equal(["ayer 23:59", "hoy 00:00", "hoy 23:59"], Titulos(panel.ParaHoy));
        Assert.Equal(["mañana 00:00", "día 1 a las 23:59"], Titulos(panel.Proximos));
        Assert.Equal(["día 2 a las 00:00"], Titulos(panel.MasAdelante));
        Assert.Equal(["hecho hoy a las 23:30"], Titulos(panel.HechosHoy));
    }
}
