using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Estadisticas;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="EstadisticasService.ResumenHoyAsync"/> sobre SQLite en memoria, con el reloj fijo el viernes
/// 13/11/2026 a las 10:00 de Madrid (09:00Z) salvo que el test lo mueva. Esa semana va del lunes 9 al
/// domingo 15. Datos inventados.
/// </summary>
public class ResumenHoyTests : ArnesSqliteTests
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    private readonly EstadisticasService _estadisticas;

    public ResumenHoyTests()
    {
        Reloj.AhoraUtc = Ahora;
        _estadisticas = new EstadisticasService(Factory, Reloj);
    }

    private static DateTime Utc(int mes, int dia, int hora, int minuto = 0) =>
        new(2026, mes, dia, hora, minuto, 0, DateTimeKind.Utc);

    private async Task SembrarAsync(DateTime fechaHora, TipoRecordatorio tipo = TipoRecordatorio.Otro,
        Candidatura? candidatura = null, bool hecho = false)
    {
        await using var db = CreateContext();
        var recordatorio = new Recordatorio { Titulo = "Recordatorio", FechaHora = fechaHora, Tipo = tipo, CandidaturaId = candidatura?.Id };
        if (hecho) recordatorio.MarcarHecho(Reloj.AhoraUtc);
        db.Recordatorios.Add(recordatorio);
        await db.SaveChangesAsync();
    }

    /// <summary>Candidatura llevada por los estados indicados, sin seguimientos automáticos.</summary>
    private async Task<Candidatura> CandidaturaAsync(string empresa, params EstadoCandidatura[] estados)
    {
        await using var db = CreateContext();
        var fecha = Ahora.AddDays(-3);
        var candidatura = Candidatura.Nueva(
            new Empresa { Nombre = empresa, NombreNormalizado = NombresEmpresa.Normalizar(empresa) },
            "Desarrollador .NET", fecha, Origen.Manual);
        foreach (var estado in estados)
        {
            candidatura.CambiarEstado(estado, fecha, Origen.Manual,
                estado == EstadoCandidatura.Descartada ? MotivoDescarte.Zona : null,
                programarSeguimiento: false);
        }
        db.Candidaturas.Add(candidatura);
        await db.SaveChangesAsync();
        return candidatura;
    }

    private static IEnumerable<int> Pendientes(ResumenHoyDto resumen) => resumen.Semana.Select(d => d.Recordatorios);

    // ---------------------------------------------------------------- vacío

    [Fact]
    public async Task SinNada_TodoACeroYLaSemanaVaDeLunesADomingo()
    {
        var resumen = await _estadisticas.ResumenHoyAsync();

        Assert.Equal(0, resumen.CandidaturasActivas);
        Assert.Equal(0, resumen.EntrevistasSemana);
        Assert.Equal(new TasaRespuestaDto(0, 0, null), resumen.Respuesta);
        Assert.Equal(
            Enumerable.Range(9, 7).Select(dia => new DiaSemanaDto(new DateOnly(2026, 11, dia), 0)),
            resumen.Semana);
    }

    // ---------------------------------------------------------------- la semana

    [Fact]
    public async Task Semana_CuentaLosPendientesDeCadaDia_YNoLosHechosNiLosDeOtraSemana()
    {
        await SembrarAsync(Utc(11, 9, 8));
        await SembrarAsync(Utc(11, 13, 8));
        await SembrarAsync(Utc(11, 13, 15), TipoRecordatorio.Entrevista);
        await SembrarAsync(Utc(11, 13, 16), hecho: true);
        await SembrarAsync(Utc(11, 3, 8)); // vencido de la semana pasada: sale en «Para hoy», no en esta semana
        await SembrarAsync(Utc(11, 17, 8)); // de la semana que viene

        var resumen = await _estadisticas.ResumenHoyAsync();

        Assert.Equal([1, 0, 0, 0, 2, 0, 0], Pendientes(resumen));
    }

    [Fact]
    public async Task Semana_ConCambioDeMes_VaDel30DeNoviembreAl6DeDiciembre()
    {
        Reloj.AhoraUtc = Utc(12, 2, 9); // miércoles 2 de diciembre
        await SembrarAsync(Utc(11, 29, 22, 30)); // domingo 29 a las 23:30 de Madrid: la semana anterior
        await SembrarAsync(Utc(11, 29, 23)); // lunes 30 a las 00:00 de Madrid
        await SembrarAsync(Utc(12, 1, 8));
        await SembrarAsync(Utc(12, 6, 22, 30), TipoRecordatorio.Entrevista); // domingo 6 a las 23:30 de Madrid
        await SembrarAsync(Utc(12, 6, 23), TipoRecordatorio.Entrevista); // lunes 7 a las 00:00 de Madrid: la siguiente

        var resumen = await _estadisticas.ResumenHoyAsync();

        Assert.Equal(
            [new(2026, 11, 30), new(2026, 12, 1), new(2026, 12, 2), new(2026, 12, 3), new(2026, 12, 4), new(2026, 12, 5), new(2026, 12, 6)],
            resumen.Semana.Select(d => d.Dia));
        Assert.Equal([1, 1, 0, 0, 0, 0, 1], Pendientes(resumen));
        Assert.Equal(1, resumen.EntrevistasSemana);
    }

    [Fact]
    public async Task Semana_ElDiaEsElDeMadrid_UnDomingoPorLaNocheEnUtcYaEsLunes()
    {
        Reloj.AhoraUtc = Utc(11, 15, 23, 30); // lunes 16 a las 00:30 de Madrid
        await SembrarAsync(Utc(11, 15, 22, 59), TipoRecordatorio.Entrevista); // domingo 15 a las 23:59 de Madrid: la semana anterior
        await SembrarAsync(Utc(11, 15, 23, 15), TipoRecordatorio.Entrevista); // lunes 16 a las 00:15 de Madrid

        var resumen = await _estadisticas.ResumenHoyAsync();

        Assert.Equal(new DateOnly(2026, 11, 16), resumen.Semana[0].Dia);
        Assert.Equal(new DateOnly(2026, 11, 22), resumen.Semana[6].Dia);
        Assert.Equal([1, 0, 0, 0, 0, 0, 0], Pendientes(resumen));
        Assert.Equal(1, resumen.EntrevistasSemana);
    }

    [Fact]
    public async Task Semana_ConCambioDeHora_LosCortesSonLosDeMadrid()
    {
        // Domingo 25/10/2026, el día en que se atrasa el reloj: el lunes 19 empieza a las 22:00Z del 18
        // (verano) y el domingo 25 acaba a las 23:00Z (invierno)
        Reloj.AhoraUtc = Utc(10, 25, 0, 30);
        await SembrarAsync(Utc(10, 18, 21, 59)); // domingo 18 a las 23:59 de Madrid: la semana anterior
        await SembrarAsync(Utc(10, 18, 22)); // lunes 19 a las 00:00 de Madrid
        await SembrarAsync(Utc(10, 25, 22, 30)); // domingo 25 a las 23:30 de Madrid
        await SembrarAsync(Utc(10, 25, 23)); // lunes 26 a las 00:00 de Madrid: la siguiente

        var resumen = await _estadisticas.ResumenHoyAsync();

        Assert.Equal(new DateOnly(2026, 10, 19), resumen.Semana[0].Dia);
        Assert.Equal([1, 0, 0, 0, 0, 0, 1], Pendientes(resumen));
    }

    // ---------------------------------------------------------------- entrevistas

    [Fact]
    public async Task Entrevistas_SoloLasPendientesDeTipoEntrevistaDeEstaSemana_TambienLasVencidas()
    {
        await SembrarAsync(Utc(11, 9, 9), TipoRecordatorio.Entrevista, hecho: true);
        await SembrarAsync(Utc(11, 10, 9), TipoRecordatorio.Entrevista);
        await SembrarAsync(Utc(11, 14, 9), TipoRecordatorio.Entrevista);
        await SembrarAsync(Utc(11, 14, 9), TipoRecordatorio.Seguimiento);
        await SembrarAsync(Utc(11, 18, 9), TipoRecordatorio.Entrevista);

        // La del lunes ya hecha no cuenta; la del martes, sin marcar, sí: es lo que filtra la cifra en Hoy
        Assert.Equal(2, (await _estadisticas.ResumenHoyAsync()).EntrevistasSemana);
    }

    // ---------------------------------------------------------------- descartadas y cerradas

    [Fact]
    public async Task Activas_NoCuentanLasDescartadasNiLasCerradas()
    {
        await CandidaturaAsync("Detectadas S.L.");
        await CandidaturaAsync("Preparadas S.L.", EstadoCandidatura.Preparada);
        await CandidaturaAsync("Enviadas S.L.", EstadoCandidatura.Enviada);
        await CandidaturaAsync("Entrevistas S.L.", EstadoCandidatura.Enviada, EstadoCandidatura.Entrevista);
        await CandidaturaAsync("Ofertas S.L.", EstadoCandidatura.Enviada, EstadoCandidatura.Entrevista, EstadoCandidatura.Oferta);
        await CandidaturaAsync("Descartes S.L.", EstadoCandidatura.Descartada);
        await CandidaturaAsync("Cierres S.L.", EstadoCandidatura.Cerrada);

        Assert.Equal(5, (await _estadisticas.ResumenHoyAsync()).CandidaturasActivas);
    }

    [Fact]
    public async Task RecordatoriosDeDescartadasOCerradas_NoCuentan_YLosDeUnaReabiertaVuelven()
    {
        var descartada = await CandidaturaAsync("Descartes S.L.", EstadoCandidatura.Descartada);
        var cerrada = await CandidaturaAsync("Cierres S.L.", EstadoCandidatura.Cerrada);
        var reabierta = await CandidaturaAsync("Vueltas S.L.", EstadoCandidatura.Descartada, EstadoCandidatura.Detectada);
        await SembrarAsync(Utc(11, 13, 9), TipoRecordatorio.Entrevista, descartada);
        await SembrarAsync(Utc(11, 14, 9), TipoRecordatorio.Entrevista, cerrada);
        await SembrarAsync(Utc(11, 15, 9), TipoRecordatorio.Entrevista, reabierta);
        await SembrarAsync(Utc(11, 15, 10)); // suelto

        var resumen = await _estadisticas.ResumenHoyAsync();

        Assert.Equal(1, resumen.EntrevistasSemana);
        Assert.Equal([0, 0, 0, 0, 0, 0, 2], Pendientes(resumen));
        Assert.Equal(1, resumen.CandidaturasActivas);
    }

    // ---------------------------------------------------------------- tasa de respuesta

    [Fact]
    public async Task Respuesta_SinEnviadas_ElPorcentajeEsNull()
    {
        await CandidaturaAsync("Detectadas S.L.");
        await CandidaturaAsync("Preparadas S.L.", EstadoCandidatura.Preparada);
        await CandidaturaAsync("Descartes S.L.", EstadoCandidatura.Descartada);

        Assert.Equal(new TasaRespuestaDto(0, 0, null), (await _estadisticas.ResumenHoyAsync()).Respuesta);
    }

    [Fact]
    public async Task Respuesta_CuatroEnviadasYUnaConEntrevistaQueLuegoSeDescarto_UnaDeCuatro()
    {
        await CandidaturaAsync("Enviadas S.L.", EstadoCandidatura.Enviada);
        await CandidaturaAsync("Otra enviada S.L.", EstadoCandidatura.Enviada);
        await CandidaturaAsync("Enviada y descartada S.L.", EstadoCandidatura.Enviada, EstadoCandidatura.Descartada);
        await CandidaturaAsync("Entrevistas S.L.", EstadoCandidatura.Enviada, EstadoCandidatura.Entrevista, EstadoCandidatura.Descartada);
        await CandidaturaAsync("Sin enviar S.L.", EstadoCandidatura.Preparada);

        Assert.Equal(new TasaRespuestaDto(4, 1, 25), (await _estadisticas.ResumenHoyAsync()).Respuesta);
    }

    [Fact]
    public async Task Respuesta_EntrevistaYOfertaDeLaMismaCandidatura_CuentaUnaVez()
    {
        await CandidaturaAsync("Ofertas S.L.", EstadoCandidatura.Enviada, EstadoCandidatura.Entrevista, EstadoCandidatura.Oferta);
        await CandidaturaAsync("Directa a entrevista S.L.", EstadoCandidatura.Preparada, EstadoCandidatura.Entrevista);
        await CandidaturaAsync("Enviadas S.L.", EstadoCandidatura.Enviada);

        Assert.Equal(new TasaRespuestaDto(3, 2, 67), (await _estadisticas.ResumenHoyAsync()).Respuesta);
    }

    // ---------------------------------------------------------------- dentro del panel

    [Fact]
    public async Task PanelHoy_LlevaElMismoResumen()
    {
        await CandidaturaAsync("Enviadas S.L.", EstadoCandidatura.Enviada);
        await SembrarAsync(Utc(11, 14, 9), TipoRecordatorio.Entrevista);

        var panel = await NuevoRecordatorios().PanelHoyAsync();

        var resumen = await _estadisticas.ResumenHoyAsync();
        Assert.Equal(1, panel.Resumen.CandidaturasActivas);
        Assert.Equal(1, panel.Resumen.EntrevistasSemana);
        Assert.Equal(resumen.Respuesta, panel.Resumen.Respuesta);
        Assert.Equal(resumen.Semana, panel.Resumen.Semana);
        Assert.Equal(resumen.SiguienteDia, panel.Resumen.SiguienteDia);
    }

    // ---------------------------------------------------------------- lo siguiente, pasada la semana

    [Fact]
    public async Task SiguienteDia_SinNadaPasadaLaSemana_EsNull()
    {
        await SembrarAsync(Utc(11, 3, 8)); // vencido
        await SembrarAsync(Utc(11, 15, 22, 59)); // domingo 15 a las 23:59 de Madrid: todavía esta semana

        Assert.Null((await _estadisticas.ResumenHoyAsync()).SiguienteDia);
    }

    [Fact]
    public async Task SiguienteDia_EsElDiaDeMadridDelPrimerPendientePasadaLaSemana()
    {
        await SembrarAsync(Utc(12, 2, 9));
        await SembrarAsync(Utc(11, 23, 23, 30)); // martes 24 a las 00:30 de Madrid
        await SembrarAsync(Utc(11, 14, 9)); // el sábado de esta semana no cuenta

        Assert.Equal(new DateOnly(2026, 11, 24), (await _estadisticas.ResumenHoyAsync()).SiguienteDia);
    }

    [Fact]
    public async Task SiguienteDia_ElLunesALasCeroDeMadridYaEsDeLaSemanaSiguiente()
    {
        await SembrarAsync(Utc(11, 15, 23)); // lunes 16 a las 00:00 de Madrid

        Assert.Equal(new DateOnly(2026, 11, 16), (await _estadisticas.ResumenHoyAsync()).SiguienteDia);
    }

    [Fact]
    public async Task SiguienteDia_NoCuentaLosHechosNiLosDeDescartadasOCerradas()
    {
        var descartada = await CandidaturaAsync("Descartadas S.L.", EstadoCandidatura.Descartada);
        var cerrada = await CandidaturaAsync("Cerradas S.L.", EstadoCandidatura.Enviada, EstadoCandidatura.Entrevista, EstadoCandidatura.Oferta, EstadoCandidatura.Cerrada);
        var activa = await CandidaturaAsync("Activas S.L.", EstadoCandidatura.Enviada);
        await SembrarAsync(Utc(11, 17, 9), hecho: true);
        await SembrarAsync(Utc(11, 18, 9), TipoRecordatorio.Seguimiento, descartada);
        await SembrarAsync(Utc(11, 19, 9), TipoRecordatorio.Seguimiento, cerrada);
        await SembrarAsync(Utc(11, 20, 9), TipoRecordatorio.Seguimiento, activa);

        Assert.Equal(new DateOnly(2026, 11, 20), (await _estadisticas.ResumenHoyAsync()).SiguienteDia);
    }
}
