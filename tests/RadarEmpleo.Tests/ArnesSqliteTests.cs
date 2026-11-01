using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Services;

namespace RadarEmpleo.Tests;

/// <summary>
/// Factory de contextos para tests: crea contextos sobre unas opciones fijas (la conexión SQLite
/// en memoria compartida del arnés). Es la misma abstracción que reciben los servicios en producción.
/// </summary>
public sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

    public AppDbContext CreateDbContext() => new AppDbContext(_options);
}

/// <summary>Reloj fijo para tests: «ahora» es lo que diga el test (y se puede adelantar).</summary>
public sealed class RelojFijo : IClock
{
    public RelojFijo(DateTime ahoraUtc) => AhoraUtc = ahoraUtc;

    public DateTime AhoraUtc { get; set; }

    /// <summary>Adelanta el reloj.</summary>
    public void Avanzar(TimeSpan tiempo) => AhoraUtc += tiempo;
}

/// <summary>
/// Arnés base para tests de servicios sobre SQLite EN MEMORIA (copiado de Siessta). QUÉ DA: una única
/// conexión <c>DataSource=:memory:</c> abierta y compartida (mientras siga abierta, la BD en memoria vive
/// y todos los contextos ven el mismo esquema y datos), el esquema ya creado con <c>EnsureCreated</c>,
/// la <see cref="Factory"/> (<see cref="TestDbContextFactory"/>, la misma abstracción que reciben los
/// servicios en producción), un <see cref="Reloj"/> fijo y <see cref="CreateContext"/> para siembras y
/// asserts con un contexto limpio.
///
/// CÓMO USARLO: heredar (<c>class MisTests : ArnesSqliteTests</c>) y construir en el ctor los servicios
/// bajo prueba con <c>new MiServicio(Factory, Reloj)</c>; xUnit crea una instancia POR TEST, así que cada
/// test estrena su propia BD aislada, y al terminar llama a <see cref="Dispose"/>, que cierra la conexión
/// y descarta la BD.
/// </summary>
public abstract class ArnesSqliteTests : IDisposable
{
    /// <summary>«Ahora» por defecto de los tests: 27/10/2026 10:00 UTC.</summary>
    protected static readonly DateTime AhoraInicial = new(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;

    /// <summary>Opciones del contexto sobre la conexión compartida (por si un test las necesita).</summary>
    protected DbContextOptions<AppDbContext> Options { get; }

    /// <summary>Factory con la que construir los servicios bajo prueba (ctor real).</summary>
    protected TestDbContextFactory Factory { get; }

    /// <summary>Reloj fijo que reciben los servicios.</summary>
    protected RelojFijo Reloj { get; } = new(AhoraInicial);

    protected ArnesSqliteTests()
    {
        // Una única conexión abierta y compartida mantiene viva la BD en memoria mientras el test vive.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var db = new AppDbContext(Options))
            db.Database.EnsureCreated();

        Factory = new TestDbContextFactory(Options);
    }

    /// <summary>Crea un contexto nuevo sobre la conexión compartida (para siembras y asserts).</summary>
    protected AppDbContext CreateContext() => new AppDbContext(Options);

    /// <summary>Cierra la conexión compartida, lo que descarta la base de datos en memoria.</summary>
    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
