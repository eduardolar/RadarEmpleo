using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Data;

/// <summary>
/// Contexto de EF Core sobre SQLite. Los servicios NO lo inyectan directamente: reciben un
/// <c>IDbContextFactory&lt;AppDbContext&gt;</c> y crean un contexto corto por operación.
/// Convenciones: enums como texto legible (<c>HasConversion&lt;string&gt;()</c>) y todas las
/// fechas en UTC (ver <see cref="ConversorUtc"/>).
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Candidatura> Candidaturas => Set<Candidatura>();
    public DbSet<CambioEstado> CambiosEstado => Set<CambioEstado>();
    public DbSet<Nota> Notas => Set<Nota>();
    public DbSet<Recordatorio> Recordatorios => Set<Recordatorio>();

    /// <summary>
    /// SQLite guarda las fechas como texto sin zona: al leerlas EF las devuelve con
    /// <see cref="DateTimeKind.Unspecified"/>. Esta convención las marca como UTC al leer (y pasa a
    /// UTC cualquier fecha local al escribir), para que la API las serialice con la «Z» final.
    /// Se aplica también a las <c>DateTime?</c>.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<ConversorUtc>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // --- Enums guardados como texto legible ---
        b.Entity<Candidatura>().Property(x => x.Estado).HasConversion<string>();
        b.Entity<Candidatura>().Property(x => x.Modalidad).HasConversion<string>();
        b.Entity<Candidatura>().Property(x => x.Encaje).HasConversion<string>();
        b.Entity<Candidatura>().Property(x => x.MotivoDescarte).HasConversion<string>();
        b.Entity<Candidatura>().Property(x => x.Origen).HasConversion<string>();
        b.Entity<CambioEstado>().Property(x => x.De).HasConversion<string>();
        b.Entity<CambioEstado>().Property(x => x.A).HasConversion<string>();
        b.Entity<CambioEstado>().Property(x => x.Origen).HasConversion<string>();
        b.Entity<Nota>().Property(x => x.Origen).HasConversion<string>();
        b.Entity<Recordatorio>().Property(x => x.Tipo).HasConversion<string>();

        // --- Empresa: nombre normalizado único (no duplicar «Inditec, S.A.» e «inditec») ---
        b.Entity<Empresa>().HasIndex(e => e.NombreNormalizado).IsUnique();

        // --- Empresa 1-N Candidatura (no se borra una empresa con candidaturas) ---
        b.Entity<Candidatura>()
            .HasOne(c => c.Empresa).WithMany(e => e.Candidaturas)
            .HasForeignKey(c => c.EmpresaId).OnDelete(DeleteBehavior.Restrict);

        // --- Clave de importación única (nullable: SQLite admite varios NULL en un índice único) ---
        b.Entity<Candidatura>().HasIndex(c => c.ClaveImportacion).IsUnique();
        b.Entity<Candidatura>().HasIndex(c => c.Estado);

        // --- Candidatura 1-N historial, notas y recordatorios, en cascada ---
        b.Entity<CambioEstado>()
            .HasOne(x => x.Candidatura).WithMany(c => c.Historial)
            .HasForeignKey(x => x.CandidaturaId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Nota>()
            .HasOne(x => x.Candidatura).WithMany(c => c.Notas)
            .HasForeignKey(x => x.CandidaturaId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Recordatorio>()
            .HasOne(x => x.Candidatura).WithMany(c => c.Recordatorios)
            .HasForeignKey(x => x.CandidaturaId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Recordatorio>().HasIndex(r => r.FechaHora);
    }
}

/// <summary>
/// Conversor de fechas para SQLite: al escribir pasa a UTC con <see cref="Fechas.AUtc"/> (las
/// <see cref="DateTimeKind.Unspecified"/> se dan por UTC) y al leer marca la fecha como
/// <see cref="DateTimeKind.Utc"/>.
/// </summary>
public sealed class ConversorUtc : ValueConverter<DateTime, DateTime>
{
    public ConversorUtc()
        : base(
            v => Fechas.AUtc(v),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
