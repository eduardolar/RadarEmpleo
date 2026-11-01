using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RadarEmpleo.Data;

/// <summary>
/// Decide con qué nivel salen en el log los errores de la base de datos: las violaciones de un índice
/// único, que la aplicación ya gestiona, no salen como error; todo lo demás, sí.
/// <para>
/// Por qué: dos altas simultáneas de la misma empresa nueva chocan con <c>Empresas.NombreNormalizado</c>
/// y la segunda se reintenta reutilizando la empresa (cualquier otra violación de un índice único acaba
/// en un 409 con un mensaje claro). Sin esto, cada choque deja en el log dos <c>fail:</c> de EF Core
/// («Failed executing DbCommand» y «An exception occurred in the database while saving changes», con la
/// traza) aunque la petición haya ido bien.
/// </para>
/// <para>
/// Cómo: EF registra esos dos eventos (<see cref="RelationalEventId.CommandError"/> y
/// <see cref="CoreEventId.SaveChangesFailed"/>) con un nivel fijo por evento, y el de
/// <c>CommandError</c> ni siquiera lleva la excepción, así que un filtro de log no puede distinguir una
/// violación de índice único de otro error. Por eso <see cref="Configurar"/> baja los dos eventos a
/// <see cref="LogLevel.Debug"/> (siguen ahí, con todo el detalle, si se activa ese nivel) y este
/// interceptor, que sí recibe la excepción, los vuelve a registrar como <see cref="LogLevel.Error"/> en
/// las mismas categorías y con los mismos EventId cuando la causa NO es una violación de índice único.
/// </para>
/// </summary>
public sealed class RegistroErroresBaseDatos : DbCommandInterceptor, ISaveChangesInterceptor
{
    private readonly ILogger _comandos;
    private readonly ILogger _guardado;

    public RegistroErroresBaseDatos(ILoggerFactory registro)
    {
        _comandos = registro.CreateLogger(DbLoggerCategory.Database.Command.Name);
        _guardado = registro.CreateLogger(DbLoggerCategory.Update.Name);
    }

    /// <summary>Aplica la configuración a las opciones del contexto (ver el resumen de la clase).</summary>
    public static DbContextOptionsBuilder Configurar(DbContextOptionsBuilder opciones, ILoggerFactory registro) =>
        opciones
            .ConfigureWarnings(w => w.Log(
                (RelationalEventId.CommandError, LogLevel.Debug),
                (CoreEventId.SaveChangesFailed, LogLevel.Debug)))
            .AddInterceptors(new RegistroErroresBaseDatos(registro));

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        RegistrarComando(command, eventData);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RegistrarComando(command, eventData);
        return Task.CompletedTask;
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => RegistrarGuardado(eventData);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        RegistrarGuardado(eventData);
        return Task.CompletedTask;
    }

    /// <summary>Como el «Failed executing DbCommand» de EF: el SQL y el error, sin la traza.</summary>
    private void RegistrarComando(DbCommand comando, CommandErrorEventData datos)
    {
        if (ErroresSqlite.EsViolacionUnica(datos.Exception, out _)) return;
        _comandos.LogError(RelationalEventId.CommandError,
            "Ha fallado un comando de la base de datos ({Duracion} ms): {Error}{NuevaLinea}{Comando}",
            (long)datos.Duration.TotalMilliseconds, datos.Exception.Message, Environment.NewLine, comando.CommandText);
    }

    /// <summary>Como el «An exception occurred in the database while saving changes» de EF, con la traza.</summary>
    private void RegistrarGuardado(DbContextErrorEventData datos)
    {
        if (ErroresSqlite.EsViolacionUnica(datos.Exception, out _)) return;
        _guardado.LogError(CoreEventId.SaveChangesFailed, datos.Exception,
            "Error de la base de datos al guardar los cambios de {Contexto}.", datos.Context?.GetType().Name);
    }
}
