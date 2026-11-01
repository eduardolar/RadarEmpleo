using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="RegistroErroresBaseDatos"/>: las violaciones de un índice único (ya gestionadas por la
/// aplicación) no salen en el log como error, y cualquier otro error de la base de datos sí.
/// </summary>
public sealed class RegistroErroresBaseDatosTests : IDisposable
{
    private readonly SqliteConnection _conexion = new("DataSource=:memory:");
    private readonly Capturador _capturador = new();
    private readonly ILoggerFactory _registro;
    private readonly DbContextOptions<AppDbContext> _opciones;

    public RegistroErroresBaseDatosTests()
    {
        _conexion.Open();
        _registro = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(_capturador));
        var opciones = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conexion).UseLoggerFactory(_registro);
        RegistroErroresBaseDatos.Configurar(opciones, _registro);
        _opciones = (DbContextOptions<AppDbContext>)opciones.Options;
        using var db = new AppDbContext(_opciones);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _registro.Dispose();
        _conexion.Dispose();
    }

    /// <summary>Niveles con los que se registraron los dos eventos de error de EF.</summary>
    private List<(int Evento, LogLevel Nivel)> EventosDeError() => _capturador.Entradas
        .Where(e => e.Evento == RelationalEventId.CommandError.Id || e.Evento == CoreEventId.SaveChangesFailed.Id)
        .Select(e => (e.Evento, e.Nivel))
        .ToList();

    [Fact]
    public async Task GuardarEmpresaDuplicada_ViolacionDeIndiceUnico_SeRegistraEnDebugYNoComoError()
    {
        await using (var db = new AppDbContext(_opciones))
        {
            db.Empresas.Add(new Empresa { Nombre = "Acme", NombreNormalizado = "acme" });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(_opciones))
        {
            db.Empresas.Add(new Empresa { Nombre = "ACME", NombreNormalizado = "acme" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        var eventos = EventosDeError();
        Assert.Contains((RelationalEventId.CommandError.Id, LogLevel.Debug), eventos);
        Assert.Contains((CoreEventId.SaveChangesFailed.Id, LogLevel.Debug), eventos);
        Assert.DoesNotContain(_capturador.Entradas, e => e.Nivel >= LogLevel.Warning);
    }

    [Fact]
    public void GuardarEmpresaDuplicada_Sincrono_TampocoSaleComoError()
    {
        using (var db = new AppDbContext(_opciones))
        {
            db.Empresas.Add(new Empresa { Nombre = "Acme", NombreNormalizado = "acme" });
            db.SaveChanges();
        }

        using (var db = new AppDbContext(_opciones))
        {
            db.Empresas.Add(new Empresa { Nombre = "ACME", NombreNormalizado = "acme" });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        Assert.NotEmpty(EventosDeError());
        Assert.DoesNotContain(_capturador.Entradas, e => e.Nivel >= LogLevel.Warning);
    }

    [Fact]
    public async Task GuardarConClaveAjenaInexistente_OtroErrorDeBaseDeDatos_SigueSaliendoComoError()
    {
        await using (var db = new AppDbContext(_opciones))
        {
            db.Candidaturas.Add(new Candidatura { EmpresaId = 999, Puesto = "Backend" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        var errores = _capturador.Entradas.Where(e => e.Nivel == LogLevel.Error).ToList();
        Assert.Contains(errores, e => e.Evento == RelationalEventId.CommandError.Id
            && e.Categoria == DbLoggerCategory.Database.Command.Name
            && e.Mensaje.Contains("FOREIGN KEY constraint failed"));
        Assert.Contains(errores, e => e.Evento == CoreEventId.SaveChangesFailed.Id
            && e.Categoria == DbLoggerCategory.Update.Name
            && e.Excepcion is DbUpdateException);
    }

    [Fact]
    public void ConsultaSobreTablaQueNoExiste_OtroErrorDeBaseDeDatos_SigueSaliendoComoError()
    {
        using var db = new AppDbContext(_opciones);

        Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlRaw("SELECT * FROM NoExiste"));

        Assert.Contains(_capturador.Entradas, e => e.Nivel == LogLevel.Error
            && e.Evento == RelationalEventId.CommandError.Id
            && e.Mensaje.Contains("no such table"));
    }

    [Fact]
    public void EsViolacionUnica_SoloUniqueYPrimaryKey_AunqueVengaEnvuelta()
    {
        var unica = new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: Empresas.NombreNormalizado'.", 19, 2067);
        var primaria = new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: Empresas.Id'.", 19, 1555);
        var claveAjena = new SqliteException("SQLite Error 19: 'FOREIGN KEY constraint failed'.", 19, 787);
        var bloqueada = new SqliteException("SQLite Error 5: 'database is locked'.", 5, 5);

        Assert.True(ErroresSqlite.EsViolacionUnica(unica, out var detalle));
        Assert.Contains("Empresas.NombreNormalizado", detalle);
        Assert.True(ErroresSqlite.EsViolacionUnica(primaria, out _));
        Assert.True(ErroresSqlite.EsViolacionUnica(new DbUpdateException("x", unica), out _));
        Assert.False(ErroresSqlite.EsViolacionUnica(claveAjena, out _));
        Assert.False(ErroresSqlite.EsViolacionUnica(new DbUpdateException("x", bloqueada), out _));
        Assert.False(ErroresSqlite.EsViolacionUnica(null, out _));
    }

    /// <summary>Proveedor de log que guarda en memoria cada entrada.</summary>
    private sealed class Capturador : ILoggerProvider
    {
        public ConcurrentQueue<Entrada> Entradas { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Registro(this, categoryName);

        public void Dispose() { }

        private sealed class Registro(Capturador capturador, string categoria) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                capturador.Entradas.Enqueue(new Entrada(categoria, eventId.Id, logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record Entrada(string Categoria, int Evento, LogLevel Nivel, string Mensaje, Exception? Excepcion);
}
