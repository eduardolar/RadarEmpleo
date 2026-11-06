using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RadarEmpleo.Data;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Curriculum;

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

/// <summary>Exportador para tests: no escribe nada, cuenta las llamadas y devuelve lo que diga el test.</summary>
public sealed class ExportadorFalso : IExportadorRadar
{
    public int Llamadas { get; private set; }

    public bool Resultado { get; set; } = true;

    public Task<bool> ExportarAsync()
    {
        Llamadas++;
        return Task.FromResult(Resultado);
    }
}

/// <summary>
/// Ejecuta una acción justo antes del primer guardado, una sola vez: sirve para simular que otra
/// petición guarda entre la lectura y el guardado del servicio bajo prueba.
/// </summary>
public sealed class AntesDeGuardar(Func<Task> accion) : SaveChangesInterceptor
{
    private Func<Task>? _pendiente = accion;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var pendiente = _pendiente;
        _pendiente = null;
        if (pendiente is not null) await pendiente();
        return result;
    }
}

/// <summary>
/// Ejecuta una acción justo antes de la primera consulta cuyo SQL contenga el texto dado, una sola vez:
/// simula que otra petición guarda entre dos lecturas del servicio bajo prueba.
/// </summary>
public sealed class AntesDeConsultar(string contiene, Func<Task> accion) : DbCommandInterceptor
{
    private Func<Task>? _pendiente = accion;

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (_pendiente is { } pendiente && command.CommandText.Contains(contiene, StringComparison.Ordinal))
        {
            _pendiente = null;
            await pendiente();
        }
        return result;
    }
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

    /// <summary>Exportador falso que reciben los servicios: los tests de servicio nunca escriben en disco.</summary>
    protected ExportadorFalso Exportador { get; } = new();

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

    /// <summary>Factory sobre la misma BD cuyo primer guardado ejecuta antes <paramref name="accion"/> (ver <see cref="AntesDeGuardar"/>).</summary>
    protected TestDbContextFactory FactoryAntesDeGuardar(Func<Task> accion) =>
        new(new DbContextOptionsBuilder<AppDbContext>(Options).AddInterceptors(new AntesDeGuardar(accion)).Options);

    /// <summary>Factory sobre la misma BD que ejecuta <paramref name="accion"/> antes de la primera consulta con ese SQL (ver <see cref="AntesDeConsultar"/>).</summary>
    protected TestDbContextFactory FactoryAntesDeConsultar(string contiene, Func<Task> accion) =>
        new(new DbContextOptionsBuilder<AppDbContext>(Options).AddInterceptors(new AntesDeConsultar(contiene, accion)).Options);

    /// <summary>Cierra la conexión compartida, lo que descarta la base de datos en memoria.</summary>
    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
