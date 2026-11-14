using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Estadisticas;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="EstadisticasService.EstadisticasAsync"/> sobre SQLite en memoria, con el reloj fijo el viernes
/// 13/11/2026 a las 10:00 de Madrid (09:00Z). Datos inventados.
/// </summary>
public class EstadisticasServiceTests : ArnesSqliteTests
{
    private static readonly DateTime Ahora = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    private readonly EstadisticasService _estadisticas;

    public EstadisticasServiceTests()
    {
        Reloj.AhoraUtc = Ahora;
        _estadisticas = new EstadisticasService(Factory, Reloj);
    }

    /// <summary>Candidatura dada de alta hace 30 días y llevada por los pasos indicados (días antes de hoy).</summary>
    private async Task CandidaturaAsync(string empresa, params (EstadoCandidatura Estado, int HaceDias)[] pasos)
    {
        await using var db = CreateContext();
        var candidatura = Candidatura.Nueva(
            new Empresa { Nombre = empresa, NombreNormalizado = NombresEmpresa.Normalizar(empresa) },
            "Desarrollador .NET", Ahora.AddDays(-30), Origen.Manual);
        foreach (var (estado, haceDias) in pasos)
        {
            candidatura.CambiarEstado(estado, Ahora.AddDays(-haceDias), Origen.Manual,
                estado == EstadoCandidatura.Descartada ? MotivoDescarte.Zona : null,
                programarSeguimiento: false);
        }
        db.Candidaturas.Add(candidatura);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SinCandidaturas_TodoACero_SinInventarPorcentajesNiMedias()
    {
        var estadisticas = await _estadisticas.EstadisticasAsync();

        Assert.Equal(0, estadisticas.Total);
        Assert.All(estadisticas.PorEstado, e => Assert.Equal(0, e.Total));
        Assert.Equal(7, estadisticas.PorEstado.Count);
        Assert.Equal(8, estadisticas.EnviadasPorSemana.Count);
        Assert.All(estadisticas.EnviadasPorSemana, s => Assert.Equal(0, s.Total));
        Assert.Equal(new RespuestaDto(0, 0, null, null, 0), estadisticas.Respuesta);
        Assert.Empty(estadisticas.MotivosDescarte);
    }

    [Fact]
    public async Task ConVarias_CuadraConElTableroYConLasDefiniciones()
    {
        await CandidaturaAsync("Detectada Ejemplo");
        await CandidaturaAsync("Enviada Ejemplo", (EstadoCandidatura.Enviada, 2));
        await CandidaturaAsync("Entrevista Ejemplo", (EstadoCandidatura.Enviada, 12), (EstadoCandidatura.Entrevista, 4));
        // Respondió pero luego se descartó: cuenta como respuesta y su motivo sale en los descartes
        await CandidaturaAsync("Descartada Ejemplo",
            (EstadoCandidatura.Enviada, 20), (EstadoCandidatura.Entrevista, 15), (EstadoCandidatura.Descartada, 10));
        // Como una importada que ya estaba en Entrevista: sin paso a Enviada no cuenta para los días medios
        await CandidaturaAsync("Importada Ejemplo", (EstadoCandidatura.Entrevista, 1));

        var estadisticas = await _estadisticas.EstadisticasAsync();

        Assert.Equal(5, estadisticas.Total);
        Assert.Equal([1, 0, 1, 2, 0, 1, 0], estadisticas.PorEstado.Select(e => e.Total));
        // Viernes 13: semana del 9. Envíos el 11/11 (esta), el 1/11 (semana del 26/10) y el 24/10 (semana del 19/10)
        Assert.Equal(new DateOnly(2026, 11, 9), estadisticas.EnviadasPorSemana[^1].Lunes);
        Assert.Equal([0, 0, 0, 0, 1, 1, 0, 1], estadisticas.EnviadasPorSemana.Select(s => s.Total));
        // Con fecha de envío: la enviada, las dos que pasaron por Enviada y la importada a Entrevista
        Assert.Equal(new RespuestaDto(4, 3, 75, 6.5, 2), estadisticas.Respuesta);
        Assert.Equal([new TotalPorMotivoDto(MotivoDescarte.Zona, 1)], estadisticas.MotivosDescarte);
    }
}
