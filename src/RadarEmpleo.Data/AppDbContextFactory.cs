using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RadarEmpleo.Data;

/// <summary>
/// Permite a las herramientas de EF Core (<c>dotnet ef migrations ...</c>) crear el contexto en
/// tiempo de diseño sin arrancar la API. La base de datos de diseño es un archivo local
/// desechable (ignorado por git); la real la configura la API.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=radar-diseno.db")
            .Options;
        return new AppDbContext(options);
    }
}
