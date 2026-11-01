using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;

namespace RadarEmpleo.Api.Errores;

/// <summary>
/// Traduce las excepciones conocidas a ProblemDetails con el mensaje en español en <c>detail</c>,
/// listo para enseñarlo tal cual en el front:
/// <list type="bullet">
/// <item><see cref="ValidacionException"/> → 400 (datos inválidos), con el campo en <c>errors</c>.</item>
/// <item><see cref="ReglaNegocioException"/> → 409 (la operación no se permite en el estado actual).</item>
/// <item>Violación de un índice único en la BD (<see cref="DbUpdateException"/> de SQLite) → 409.</item>
/// </list>
/// Cualquier otra excepción sigue su camino y acaba en un 500 genérico (sin detalles internos).
/// </summary>
public sealed class ManejadorExcepciones(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problema = exception switch
        {
            ValidacionException validacion => RespuestasValidacion.Problema(
                validacion.Campo is { } campo
                    ? new Dictionary<string, string[]> { [campo] = [validacion.Message] }
                    : new Dictionary<string, string[]>(),
                validacion.Message),

            ReglaNegocioException regla => Conflicto(regla.Message),

            DbUpdateException bd when ErroresBaseDatos.EsViolacionUnica(bd, out _) =>
                Conflicto("Ya existe un registro con esos mismos datos. Revisa si lo has dado de alta antes."),

            _ => null
        };
        if (problema is null) return false;

        http.Response.StatusCode = problema.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = problema
        });
    }

    private static ProblemDetails Conflicto(string mensaje) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "No se puede hacer esa operación",
        Detail = mensaje,
        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10"
    };
}
