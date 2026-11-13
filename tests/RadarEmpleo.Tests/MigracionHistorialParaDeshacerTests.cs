using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary>
/// Migraciones <c>MotivoEnHistorial</c> y <c>SeguimientosEnHistorial</c> sobre una BD con datos «de antes»: el descarte vigente de las que siguen
/// descartadas se queda con su motivo; los demás apuntes, sin él. Datos inventados.
/// </summary>
public sealed class MigracionHistorialParaDeshacerTests : IDisposable
{
    private const string MigracionAnterior = "20261108161515_Rastreos";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public MigracionHistorialParaDeshacerTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Migrar_DescartadaVigente_SuDescarteSeQuedaConElMotivo()
    {
        await using (var db = new AppDbContext(_options))
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior);

        using (var comando = _connection.CreateCommand())
        {
            comando.CommandText = """
                INSERT INTO "Empresas" ("Id", "Nombre", "NombreNormalizado") VALUES (1, 'Acme', 'acme');

                INSERT INTO "Candidaturas" ("Id", "EmpresaId", "Puesto", "Modalidad", "Encaje", "Estado", "FechaDeteccion", "FechaEstado", "FechaUltimoCambio", "Origen", "MotivoDescarte", "MotivoDescarteTexto") VALUES
                    (1, 1, 'Descartada dos veces', 'NoIndicada', 'SinValorar', 'Descartada', '2026-10-20 09:00:00', '2026-10-25 09:00:00', '2026-10-25 09:00:00', 'Manual', 'Zona', 'Bilbao'),
                    (2, 1, 'Reabierta', 'NoIndicada', 'SinValorar', 'Detectada', '2026-10-20 09:00:00', '2026-10-23 09:00:00', '2026-10-23 09:00:00', 'Manual', NULL, NULL);

                INSERT INTO "CambiosEstado" ("Id", "CandidaturaId", "De", "A", "Fecha", "Origen") VALUES
                    (1, 1, NULL, 'Detectada', '2026-10-20 09:00:00', 'Manual'),
                    (2, 1, 'Detectada', 'Descartada', '2026-10-21 09:00:00', 'Manual'),
                    (3, 1, 'Descartada', 'Detectada', '2026-10-22 09:00:00', 'Manual'),
                    (4, 1, 'Detectada', 'Descartada', '2026-10-25 09:00:00', 'Manual'),
                    (5, 2, NULL, 'Detectada', '2026-10-20 09:00:00', 'Manual'),
                    (6, 2, 'Detectada', 'Descartada', '2026-10-21 09:00:00', 'Manual'),
                    (7, 2, 'Descartada', 'Detectada', '2026-10-23 09:00:00', 'Manual');
                """;
            comando.ExecuteNonQuery();
        }

        await using (var db = new AppDbContext(_options))
            await db.Database.MigrateAsync();

        await using var lectura = new AppDbContext(_options);
        var conMotivo = await lectura.CambiosEstado.AsNoTracking().Where(h => h.MotivoDescarte != null).ToListAsync();
        var unico = Assert.Single(conMotivo);
        Assert.Equal(4, unico.Id);
        Assert.Equal(MotivoDescarte.Zona, unico.MotivoDescarte);
        Assert.Equal("Bilbao", unico.MotivoDescarteTexto);
        // Los apuntes antiguos no dicen qué seguimiento crearon ni cerraron.
        Assert.All(await lectura.CambiosEstado.AsNoTracking().ToListAsync(), h =>
        {
            Assert.Null(h.SeguimientoCreadoId);
            Assert.Null(h.SeguimientoCerradoId);
        });
    }

    [Fact]
    public async Task BdConSoloElMotivo_SeguimientosEnHistorialSeAplicaSinTocarElMotivo()
    {
        // Como las BD de prueba que ya tenían la primera de las dos migraciones.
        await using (var db = new AppDbContext(_options))
            await db.GetService<IMigrator>().MigrateAsync("20261112140913_MotivoEnHistorial");
        using (var comando = _connection.CreateCommand())
        {
            comando.CommandText = """
                INSERT INTO "Empresas" ("Id", "Nombre", "NombreNormalizado") VALUES (1, 'Acme', 'acme');
                INSERT INTO "Candidaturas" ("Id", "EmpresaId", "Puesto", "Modalidad", "Encaje", "Estado", "FechaDeteccion", "FechaEstado", "FechaUltimoCambio", "Origen", "MotivoDescarte") VALUES
                    (1, 1, 'Descartada', 'NoIndicada', 'SinValorar', 'Descartada', '2026-10-20 09:00:00', '2026-10-21 09:00:00', '2026-10-21 09:00:00', 'Manual', 'Zona');
                INSERT INTO "CambiosEstado" ("Id", "CandidaturaId", "De", "A", "Fecha", "Origen", "MotivoDescarte") VALUES
                    (1, 1, 'Detectada', 'Descartada', '2026-10-21 09:00:00', 'Manual', 'Zona');
                """;
            comando.ExecuteNonQuery();
        }

        await using (var db = new AppDbContext(_options))
            await db.Database.MigrateAsync();

        await using var lectura = new AppDbContext(_options);
        var apunte = await lectura.CambiosEstado.AsNoTracking().SingleAsync();
        Assert.Equal(MotivoDescarte.Zona, apunte.MotivoDescarte);
        Assert.Null(apunte.SeguimientoCerradoId);
    }

    [Fact]
    public async Task BorrarUnSeguimiento_ElApunteSeQuedaSinElYSinFallar()
    {
        await using (var db = new AppDbContext(_options))
            await db.Database.MigrateAsync();
        using (var comando = _connection.CreateCommand())
        {
            comando.CommandText = """
                PRAGMA foreign_keys = ON;
                INSERT INTO "Empresas" ("Id", "Nombre", "NombreNormalizado") VALUES (1, 'Acme', 'acme');
                INSERT INTO "Candidaturas" ("Id", "EmpresaId", "Puesto", "Modalidad", "Encaje", "Estado", "FechaDeteccion", "FechaEstado", "FechaUltimoCambio", "Origen") VALUES
                    (1, 1, 'Enviada', 'NoIndicada', 'SinValorar', 'Enviada', '2026-10-20 09:00:00', '2026-10-21 09:00:00', '2026-10-21 09:00:00', 'Manual');
                INSERT INTO "Recordatorios" ("Id", "CandidaturaId", "Tipo", "Titulo", "FechaHora", "Hecho") VALUES
                    (1, 1, 'Seguimiento', 'Seguimiento', '2026-10-28 08:00:00', 0);
                INSERT INTO "CambiosEstado" ("Id", "CandidaturaId", "De", "A", "Fecha", "Origen", "SeguimientoCreadoId") VALUES
                    (1, 1, 'Detectada', 'Enviada', '2026-10-21 09:00:00', 'Manual', 1);
                DELETE FROM "Recordatorios" WHERE "Id" = 1;
                """;
            comando.ExecuteNonQuery();
        }

        await using var lectura = new AppDbContext(_options);
        Assert.Null((await lectura.CambiosEstado.AsNoTracking().SingleAsync()).SeguimientoCreadoId);
    }
}
