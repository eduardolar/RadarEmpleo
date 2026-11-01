using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadarEmpleo.Data;

namespace RadarEmpleo.Tests;

/// <summary>
/// Migración <c>FechaEstado</c> sobre una BD con datos «de antes»: se lleva una SQLite en memoria hasta
/// <c>Inicial</c>, se siembran filas a mano con SQL (como las habría guardado EF) y se aplica el resto.
/// Datos inventados.
/// </summary>
public sealed class MigracionFechaEstadoTests : IDisposable
{
    private const string MigracionInicial = "20261027121342_Inicial";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public MigracionFechaEstadoTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
    }

    public void Dispose() => _connection.Dispose();

    private void Ejecutar(string sql)
    {
        using var comando = _connection.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    [Fact]
    public async Task Migrar_FilasAntiguas_RellenaFechaEstadoConElUltimoCambioTrasElAltaOLaDeteccion()
    {
        await using (var db = new AppDbContext(_options))
            await db.GetService<IMigrator>().MigrateAsync(MigracionInicial);

        Ejecutar("""
            INSERT INTO "Empresas" ("Id", "Nombre", "NombreNormalizado") VALUES (1, 'Acme', 'acme');

            INSERT INTO "Candidaturas" ("Id", "EmpresaId", "Puesto", "Modalidad", "Encaje", "Estado", "FechaDeteccion", "FechaUltimoCambio", "Origen") VALUES
                (1, 1, 'Solo alta', 'NoIndicada', 'SinValorar', 'Detectada', '2026-10-20 09:00:00', '2026-10-29 12:00:00', 'Manual'),
                (2, 1, 'Enviada y editada', 'NoIndicada', 'SinValorar', 'Enviada', '2026-10-20 09:00:00', '2026-10-29 12:00:00', 'Manual'),
                (3, 1, 'Sin historial', 'NoIndicada', 'SinValorar', 'Detectada', '2026-10-18 07:30:00', '2026-10-29 12:00:00', 'Registro');

            INSERT INTO "CambiosEstado" ("Id", "CandidaturaId", "De", "A", "Fecha", "Origen") VALUES
                -- Importada días después de detectarla: solo tiene el alta, así que cuenta la detección.
                (1, 1, NULL, 'Detectada', '2026-10-25 11:00:00', 'Registro'),
                -- Enviada, reabierta y reenviada. El apunte más reciente tiene el id más bajo (manda la
                -- fecha, no el id) y solo se distingue del anterior por las fracciones de segundo.
                (2, 2, 'Detectada', 'Enviada', '2026-10-22 08:15:30.1234567', 'Manual'),
                (3, 2, NULL, 'Detectada', '2026-10-20 09:00:00', 'Manual'),
                (4, 2, 'Detectada', 'Enviada', '2026-10-21 08:15:30', 'Manual'),
                (5, 2, 'Enviada', 'Detectada', '2026-10-22 08:15:30', 'Manual');
            """);

        await using (var db = new AppDbContext(_options))
            await db.Database.MigrateAsync();

        await using var lectura = new AppDbContext(_options);
        var fechas = await lectura.Candidaturas.AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new { c.FechaEstado, c.FechaUltimoCambio })
            .ToListAsync();

        Assert.Equal(new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc), fechas[0].FechaEstado);
        Assert.Equal(new DateTime(2026, 10, 22, 8, 15, 30, DateTimeKind.Utc).AddTicks(1234567), fechas[1].FechaEstado);
        Assert.Equal(new DateTime(2026, 10, 18, 7, 30, 0, DateTimeKind.Utc), fechas[2].FechaEstado);
        Assert.All(fechas, f => Assert.Equal(DateTimeKind.Utc, f.FechaEstado.Kind));
        // La última modificación no se toca.
        Assert.All(fechas, f => Assert.Equal(new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc), f.FechaUltimoCambio));
    }
}
