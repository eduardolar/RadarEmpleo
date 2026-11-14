using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadarEmpleo.Data;

namespace RadarEmpleo.Tests;

/// <summary>
/// Migración <c>RecordatoriosHoy</c> sobre una BD con datos «de antes»: solo se dan por hechos los
/// seguimientos pendientes de las candidaturas que ya están en Entrevista u Oferta. Datos inventados.
/// </summary>
public sealed class MigracionRecordatoriosHoyTests : IDisposable
{
    private const string MigracionAnterior = "20261113083513_SeguimientosEnHistorial";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public MigracionRecordatoriosHoyTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Migrar_SoloCierraLosSeguimientosPendientesDeEntrevistaYOferta()
    {
        await using (var db = new AppDbContext(_options))
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior);

        using (var comando = _connection.CreateCommand())
        {
            comando.CommandText = """
                INSERT INTO "Empresas" ("Id", "Nombre", "NombreNormalizado") VALUES (1, 'Acme', 'acme');

                INSERT INTO "Candidaturas" ("Id", "EmpresaId", "Puesto", "Modalidad", "Encaje", "Estado", "FechaDeteccion", "FechaEstado", "FechaUltimoCambio", "Origen") VALUES
                    (1, 1, 'En entrevista', 'NoIndicada', 'SinValorar', 'Entrevista', '2026-10-20 09:00:00', '2026-10-25 09:00:00', '2026-10-25 09:00:00', 'Manual'),
                    (2, 1, 'Con oferta', 'NoIndicada', 'SinValorar', 'Oferta', '2026-10-20 09:00:00', '2026-10-25 09:00:00', '2026-10-25 09:00:00', 'Manual'),
                    (3, 1, 'Enviada', 'NoIndicada', 'SinValorar', 'Enviada', '2026-10-20 09:00:00', '2026-10-25 09:00:00', '2026-10-25 09:00:00', 'Manual');

                INSERT INTO "Recordatorios" ("Id", "CandidaturaId", "Tipo", "Titulo", "FechaHora", "Hecho") VALUES
                    (1, 1, 'Seguimiento', 'Seguimiento de la entrevista', '2026-10-28 08:00:00', 0),
                    (2, 2, 'Seguimiento', 'Seguimiento de la oferta', '2026-10-28 08:00:00', 0),
                    (3, 3, 'Seguimiento', 'Seguimiento de la enviada', '2026-10-28 08:00:00', 0),
                    (4, 1, 'Plazo', 'Plazo de la entrevista', '2026-10-28 08:00:00', 0),
                    (5, NULL, 'Seguimiento', 'Suelto', '2026-10-28 08:00:00', 0),
                    (6, NULL, 'PublicacionLinkedIn', 'Publicar en LinkedIn', '2026-11-03 08:00:00', 0),
                    (7, 3, 'Otro', 'Ya hecho', '2026-10-21 08:00:00', 1);
                """;
            comando.ExecuteNonQuery();
        }

        await using (var db = new AppDbContext(_options))
            await db.Database.MigrateAsync();

        await using var lectura = new AppDbContext(_options);
        var recordatorios = await lectura.Recordatorios.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.Equal([1, 2, 7], recordatorios.Where(r => r.Hecho).Select(r => r.Id));
        // No se sabe cuándo se cerraron: la fecha no se inventa.
        Assert.All(recordatorios, r => Assert.Null(r.FechaHecho));
    }
}
