using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;

namespace RadarEmpleo.Tests;

/// <summary>
/// Migración <c>Documentos</c> aplicada de verdad (no con EnsureCreated): crea los índices únicos, también
/// el parcial que deja un solo documento vigente por tipo y candidatura. Datos inventados.
/// </summary>
public sealed class MigracionDocumentosTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public MigracionDocumentosTests()
    {
        _connection.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        db.Database.Migrate();
        Ejecutar("""
            INSERT INTO "Empresas" ("Id", "Nombre", "NombreNormalizado") VALUES (1, 'Acme', 'acme');
            INSERT INTO "Candidaturas" ("Id", "EmpresaId", "Puesto", "Modalidad", "Encaje", "Estado", "FechaDeteccion", "FechaEstado", "FechaUltimoCambio", "Origen")
                VALUES (1, 1, 'Backend', 'NoIndicada', 'SinValorar', 'Detectada', '2026-11-01 09:00:00', '2026-11-01 09:00:00', '2026-11-01 09:00:00', 'Manual');
            """);
    }

    public void Dispose() => _connection.Dispose();

    private void Ejecutar(string sql)
    {
        using var comando = _connection.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    private void Documento(string ruta, string tipo, int vigente) => Ejecutar($"""
        INSERT INTO "Documentos" ("CandidaturaId", "Tipo", "NombreArchivo", "RutaRelativa", "Encontrado", "Origen", "Vigente", "FechaAlta")
            VALUES (1, '{tipo}', 'x.pdf', '{ruta}', 1, 'Manual', {vigente}, '2026-11-01 09:00:00');
        """);

    [Fact]
    public void Migrar_CreaElIndiceParcialDeUnVigentePorTipo()
    {
        using var comando = _connection.CreateCommand();
        comando.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $nombre";
        comando.Parameters.AddWithValue("$nombre", AppDbContext.IndiceVigentePorTipo);

        var sql = comando.ExecuteScalar() as string;

        Assert.NotNull(sql);
        Assert.Contains("UNIQUE", sql);
        Assert.Contains("WHERE \"Vigente\" = 1", sql);
    }

    [Fact]
    public void Migrar_DosVigentesDelMismoTipo_LaBdLoRechaza()
    {
        Documento("a.pdf", "CV", 1);

        var error = Assert.Throws<SqliteException>(() => Documento("b.pdf", "CV", 1));
        Assert.True(ErroresSqlite.EsViolacionUnica(error, out _));
    }

    [Fact]
    public void Migrar_UnVigentePorTipoYAnterioresSinLimite_LaBdLoAcepta()
    {
        Documento("a.pdf", "CV", 1);
        Documento("b.pdf", "CV", 0);
        Documento("c.pdf", "CV", 0);
        Documento("d.pdf", "Carta", 1);
    }

    [Fact]
    public void Migrar_LaMismaRutaDosVeces_LaBdLoRechaza()
    {
        Documento("a.pdf", "CV", 1);

        Assert.Throws<SqliteException>(() => Documento("a.pdf", "Carta", 0));
    }
}
