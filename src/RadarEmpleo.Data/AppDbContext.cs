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
    public DbSet<Documento> Documentos => Set<Documento>();
    public DbSet<Rastreo> Rastreos => Set<Rastreo>();
    public DbSet<OfertaRastreo> OfertasRastreo => Set<OfertaRastreo>();

    /// <summary>Índice único parcial de un documento vigente por tipo y candidatura.</summary>
    public const string IndiceVigentePorTipo = "IX_Documentos_CandidaturaId_Tipo_Vigente";

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
        b.Entity<CambioEstado>().Property(x => x.MotivoDescarte).HasConversion<string>();
        b.Entity<Nota>().Property(x => x.Origen).HasConversion<string>();
        b.Entity<Recordatorio>().Property(x => x.Tipo).HasConversion<string>();
        b.Entity<Documento>().Property(x => x.Tipo).HasConversion<string>();
        b.Entity<Documento>().Property(x => x.Origen).HasConversion<string>();

        // --- Empresa: nombre normalizado único (no duplicar «Inditec, S.A.» e «inditec») ---
        b.Entity<Empresa>().HasIndex(e => e.NombreNormalizado).IsUnique();

        // --- Empresa 1-N Candidatura (no se borra una empresa con candidaturas) ---
        b.Entity<Candidatura>()
            .HasOne(c => c.Empresa).WithMany(e => e.Candidaturas)
            .HasForeignKey(c => c.EmpresaId).OnDelete(DeleteBehavior.Restrict);

        // --- Clave de importación única (nullable: SQLite admite varios NULL en un índice único) ---
        b.Entity<Candidatura>().HasIndex(c => c.ClaveImportacion).IsUnique();
        b.Entity<Candidatura>().HasIndex(c => c.Estado);

        // --- Concurrencia optimista: todo cambio (de datos o de estado) actualiza FechaUltimoCambio, así
        // que de dos peticiones que cambian a la vez la misma candidatura solo guarda la primera (la
        // segunda duplicaría el cambio de estado y el seguimiento) ---
        b.Entity<Candidatura>().Property(c => c.FechaUltimoCambio).IsConcurrencyToken();

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

        // --- El historial apunta qué seguimiento creó o cerró cada cambio (para deshacerlo con exactitud).
        // Si el recordatorio desaparece, el apunte se queda sin él ---
        b.Entity<CambioEstado>()
            .HasOne(x => x.SeguimientoCreado).WithMany()
            .HasForeignKey(x => x.SeguimientoCreadoId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<CambioEstado>()
            .HasOne(x => x.SeguimientoCerrado).WithMany()
            .HasForeignKey(x => x.SeguimientoCerradoId).OnDelete(DeleteBehavior.SetNull);

        // --- Candidatura 1-N Documento, en cascada (solo borra filas: el archivo no se toca nunca) ---
        b.Entity<Documento>()
            .HasOne(d => d.Candidatura).WithMany(c => c.Documentos)
            .HasForeignKey(d => d.CandidaturaId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Documento>().HasIndex(d => new { d.CandidaturaId, d.RutaRelativa }).IsUnique();
        // Un solo vigente por tipo: lo garantiza Candidatura.VincularDocumento y, si dos peticiones
        // vinculan a la vez, también la BD (índice parcial: solo cuenta las filas vigentes).
        b.Entity<Documento>().HasIndex(d => new { d.CandidaturaId, d.Tipo }).IsUnique()
            .HasFilter("\"Vigente\" = 1").HasDatabaseName(IndiceVigentePorTipo);
        // PreparacionId sin clave foránea: la tabla Preparacion llega en la F8, con SetNull.
        b.Entity<Documento>().HasIndex(d => d.PreparacionId);
        b.Entity<Documento>().Property(d => d.NombreArchivo).HasMaxLength(Documento.LargoNombreArchivo);
        b.Entity<Documento>().Property(d => d.RutaRelativa).HasMaxLength(Documento.LargoRutaRelativa);
        b.Entity<Documento>().Property(d => d.HashSha256).HasMaxLength(Documento.LargoHash);

        // --- Rastreos: uno por informe (Clave única) y cada oferta una sola vez en toda la tabla (Enlace único) ---
        b.Entity<Rastreo>().Property(r => r.Origen).HasConversion<string>();
        b.Entity<Rastreo>().HasIndex(r => r.Clave).IsUnique();
        b.Entity<Rastreo>().HasIndex(r => r.Fecha);
        // EjecucionAgenteId sin clave foránea: la tabla EjecucionAgente llega en la F7.
        b.Entity<Rastreo>().HasIndex(r => r.EjecucionAgenteId);
        b.Entity<Rastreo>().Property(r => r.Clave).HasMaxLength(Rastreo.LargoClave);
        b.Entity<Rastreo>().Property(r => r.Titulo).HasMaxLength(Rastreo.LargoTitulo);
        b.Entity<Rastreo>().Property(r => r.RutaInforme).HasMaxLength(Rastreo.LargoRutaInforme);
        b.Entity<Rastreo>().Property(r => r.Hash).HasMaxLength(Rastreo.LargoHash);

        b.Entity<OfertaRastreo>().Property(o => o.Encaje).HasConversion<string>();
        b.Entity<OfertaRastreo>().Property(o => o.Estado).HasConversion<string>();
        b.Entity<OfertaRastreo>()
            .HasOne(o => o.Rastreo).WithMany(r => r.Ofertas)
            .HasForeignKey(o => o.RastreoId).OnDelete(DeleteBehavior.Cascade);
        // Borrar la candidatura no borra la oferta: solo la desenlaza.
        b.Entity<OfertaRastreo>()
            .HasOne(o => o.Candidatura).WithMany()
            .HasForeignKey(o => o.CandidaturaId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        b.Entity<OfertaRastreo>().HasIndex(o => o.Enlace).IsUnique();
        b.Entity<OfertaRastreo>().Property(o => o.Empresa).HasMaxLength(OfertaRastreo.LargoEmpresa);
        b.Entity<OfertaRastreo>().Property(o => o.Puesto).HasMaxLength(OfertaRastreo.LargoPuesto);
        b.Entity<OfertaRastreo>().Property(o => o.Enlace).HasMaxLength(OfertaRastreo.LargoEnlace);
        b.Entity<OfertaRastreo>().Property(o => o.Pega).HasMaxLength(OfertaRastreo.LargoPega);
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
