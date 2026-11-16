using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary>
/// Migración <c>Agentes</c> sobre una BD con rastreos «de antes»: la clave foránea nueva reconstruye la tabla
/// Rastreos y no se puede perder ningún rastreo, ninguna oferta ni su enlace con la candidatura. Datos inventados.
/// </summary>
public sealed class MigracionAgentesTests : IDisposable
{
    private const string MigracionAnterior = "20261113162459_RecordatoriosHoy";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public MigracionAgentesTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
    }

    public void Dispose() => _connection.Dispose();

    private async Task MigrarConRastreosAsync()
    {
        await using (var db = new AppDbContext(_options))
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior);

        using (var comando = _connection.CreateCommand())
        {
            comando.CommandText = """
                INSERT INTO "Empresas" ("Id", "Nombre", "NombreNormalizado") VALUES (1, 'Acme', 'acme');
                INSERT INTO "Candidaturas" ("Id", "EmpresaId", "Puesto", "Modalidad", "Encaje", "Estado", "FechaDeteccion", "FechaEstado", "FechaUltimoCambio", "Origen") VALUES
                    (7, 1, 'Desarrollador .NET', 'Remoto', 'Alto', 'Detectada', '2026-11-09 09:00:00', '2026-11-09 09:00:00', '2026-11-09 09:00:00', 'Rastreo');

                INSERT INTO "Rastreos" ("Id", "Clave", "Origen", "Titulo", "Fecha", "RutaInforme", "Hash", "TextoInforme", "FechaEscaneo", "EjecucionAgenteId") VALUES
                    (3, 'rastreos/rastreo_2026-11-09.md', 'TareaLunes', 'Rastreo del 9', '2026-11-09', 'rastreos/rastreo_2026-11-09.md', 'abc', '- oferta 1' || char(10) || '- oferta 2', '2026-11-09 10:00:00', NULL),
                    (4, 'registro:rastreo 03/11', 'Registro', 'Rastreo 03/11', '2026-11-03', 'ofertas-enviadas.md', 'def', 'texto', '2026-11-03 10:00:00', 99);

                INSERT INTO "OfertasRastreo" ("Id", "RastreoId", "Linea", "TextoOriginal", "Empresa", "Puesto", "Enlace", "Encaje", "Pega", "Estado", "CandidaturaId", "FechaEstado") VALUES
                    (10, 3, 5, '- oferta 1', 'Acme', 'Desarrollador .NET', 'https://ejemplo.com/1', 'Alto', 'Inglés', 'Convertida', 7, '2026-11-09 11:00:00'),
                    (11, 3, 6, '- oferta 2', 'Otra', 'Programador', 'https://ejemplo.com/2', 'Medio', NULL, 'Pendiente', NULL, '2026-11-09 10:00:00'),
                    (12, 4, 2, '- oferta 3', 'Tercera', 'Soporte', 'https://ejemplo.com/3', 'No', NULL, 'Ignorada', NULL, '2026-11-04 10:00:00');
                """;
            comando.ExecuteNonQuery();
        }

        await using (var db = new AppDbContext(_options))
            await db.Database.MigrateAsync();
    }

    [Fact]
    public async Task Migrar_NoPierdeNingunRastreoNiOfertaNiSuCandidatura()
    {
        await MigrarConRastreosAsync();

        await using var db = new AppDbContext(_options);
        var rastreos = await db.Rastreos.AsNoTracking().Include(r => r.Ofertas).OrderBy(r => r.Id).ToListAsync();

        Assert.Equal([3, 4], rastreos.Select(r => r.Id));
        var lunes = rastreos[0];
        Assert.Equal("rastreos/rastreo_2026-11-09.md", lunes.Clave);
        Assert.Equal(OrigenRastreo.TareaLunes, lunes.Origen);
        Assert.Equal(new DateOnly(2026, 11, 9), lunes.Fecha);
        Assert.Equal("abc", lunes.Hash);
        Assert.Equal("- oferta 1\n- oferta 2", lunes.TextoInforme);
        Assert.Null(lunes.EjecucionAgenteId);

        var ofertas = await db.OfertasRastreo.AsNoTracking().OrderBy(o => o.Id).ToListAsync();
        Assert.Equal([10, 11, 12], ofertas.Select(o => o.Id));
        Assert.Equal([3, 3, 4], ofertas.Select(o => o.RastreoId));
        Assert.Equal(7, ofertas[0].CandidaturaId);
        Assert.Equal(EstadoOfertaRastreo.Convertida, ofertas[0].Estado);
        Assert.Equal("Inglés", ofertas[0].Pega);
        Assert.Null(ofertas[1].CandidaturaId);
        Assert.Equal(EstadoOfertaRastreo.Ignorada, ofertas[2].Estado);

        // Un EjecucionAgenteId que no apuntaba a nada queda en null (no había ejecuciones).
        Assert.Null(rastreos[1].EjecucionAgenteId);

        // Las claves foráneas siguen en pie después de la reconstrucción.
        using var comprobar = _connection.CreateCommand();
        comprobar.CommandText = "PRAGMA foreign_key_check;";
        using var lector = await comprobar.ExecuteReaderAsync();
        Assert.False(await lector.ReadAsync());
    }

    [Fact]
    public async Task Migrar_LaEjecucionSeEnlazaYAlBorrarlaElRastreoQuedaSinElla()
    {
        await MigrarConRastreosAsync();

        int ejecucionId;
        await using (var db = new AppDbContext(_options))
        {
            var ejecucion = EjecucionAgente.Nueva(TareaAgente.Rastreo, "{}", 3m, 1800, null, new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc));
            db.EjecucionesAgente.Add(ejecucion);
            await db.SaveChangesAsync();
            ejecucionId = ejecucion.Id;

            var rastreo = await db.Rastreos.SingleAsync(r => r.Id == 3);
            rastreo.EjecucionAgenteId = ejecucionId;
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(_options))
        {
            Assert.Equal(ejecucionId, (await db.Rastreos.AsNoTracking().SingleAsync(r => r.Id == 3)).EjecucionAgenteId);
            await db.EjecucionesAgente.Where(e => e.Id == ejecucionId).ExecuteDeleteAsync();
        }

        await using var lectura = new AppDbContext(_options);
        var rastreoDespues = await lectura.Rastreos.AsNoTracking().Include(r => r.Ofertas).SingleAsync(r => r.Id == 3);
        Assert.Null(rastreoDespues.EjecucionAgenteId);
        Assert.Equal(2, rastreoDespues.Ofertas.Count);
    }

    [Fact]
    public async Task Migrar_UnaEjecucionGuardaSusImportesYEstados()
    {
        await MigrarConRastreosAsync();

        await using (var db = new AppDbContext(_options))
        {
            var e = EjecucionAgente.Nueva(TareaAgente.AnalizarEncaje, """{"candidaturaId":7}""", 0.50m, 300, null,
                new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc), 7);
            e.Empezar(new DateTime(2026, 11, 16, 8, 1, 0, DateTimeKind.Utc), "aaa", "ppp");
            e.Terminar(new DateTime(2026, 11, 16, 8, 2, 0, DateTimeKind.Utc), 0.1834m, 4, "s", "Encaje medio.", "{}", "aaa", "ppp");
            db.EjecucionesAgente.Add(e);
            await db.SaveChangesAsync();
        }

        await using var lectura = new AppDbContext(_options);
        var guardada = await lectura.EjecucionesAgente.AsNoTracking().SingleAsync();
        Assert.Equal(EstadoEjecucion.Ok, guardada.Estado);
        Assert.Equal(0.1834m, guardada.CosteUsd);
        Assert.Equal(0.50m, guardada.PresupuestoUsd);
        Assert.Equal(DateTimeKind.Utc, guardada.Inicio!.Value.Kind);

        // Borrar la candidatura deja la ejecución, sin ella.
        await lectura.Candidaturas.Where(c => c.Id == 7).ExecuteDeleteAsync();
        Assert.Null((await new AppDbContext(_options).EjecucionesAgente.AsNoTracking().SingleAsync()).CandidaturaId);
    }
}
