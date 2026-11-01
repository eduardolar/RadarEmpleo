using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RadarEmpleo.Data;

namespace RadarEmpleo.Api.Configuracion;

/// <summary>Comprobación de salud para <c>/health</c>: que se pueda abrir la base de datos.</summary>
public sealed class ComprobacionBaseDatos(IDbContextFactory<AppDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Base de datos accesible.")
            : HealthCheckResult.Unhealthy("No se puede abrir la base de datos.");
    }
}
