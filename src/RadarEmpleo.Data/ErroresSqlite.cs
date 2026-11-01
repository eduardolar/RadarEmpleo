using Microsoft.Data.Sqlite;

namespace RadarEmpleo.Data;

/// <summary>Reconoce errores concretos de SQLite, estén donde estén en la cadena de excepciones.</summary>
public static class ErroresSqlite
{
    // SQLITE_CONSTRAINT (19) y sus códigos extendidos para UNIQUE (2067) y PRIMARY KEY (1555).
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintUnique = 2067;
    private const int SqliteConstraintPrimaryKey = 1555;

    /// <summary>
    /// ¿Es <paramref name="error"/> (o alguna de sus excepciones internas) la violación de un índice
    /// único o de una clave primaria? Sirve tanto para la <see cref="SqliteException"/> suelta (lo que
    /// EF registra al fallar el comando) como para la <c>DbUpdateException</c> que la envuelve.
    /// <paramref name="detalle"/> trae el mensaje de SQLite («UNIQUE constraint failed: Tabla.Columna»).
    /// </summary>
    public static bool EsViolacionUnica(Exception? error, out string detalle)
    {
        for (var actual = error; actual is not null; actual = actual.InnerException)
        {
            if (actual is SqliteException
                {
                    SqliteErrorCode: SqliteConstraint,
                    SqliteExtendedErrorCode: SqliteConstraintUnique or SqliteConstraintPrimaryKey
                } sqlite)
            {
                detalle = sqlite.Message;
                return true;
            }
        }

        detalle = "";
        return false;
    }
}
