using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;

namespace RadarEmpleo.Services;

/// <summary>Reconoce errores concretos de SQLite dentro de las excepciones de EF Core.</summary>
public static class ErroresBaseDatos
{
    /// <summary>
    /// ¿Es <paramref name="error"/> una violación de un índice único (o de clave primaria)?
    /// <paramref name="detalle"/> trae el mensaje de SQLite («UNIQUE constraint failed: Tabla.Columna»).
    /// El reconocimiento en sí está en <see cref="ErroresSqlite"/>, que también usa el filtro del log.
    /// </summary>
    public static bool EsViolacionUnica(DbUpdateException error, out string detalle) =>
        ErroresSqlite.EsViolacionUnica(error.InnerException, out detalle);
}
