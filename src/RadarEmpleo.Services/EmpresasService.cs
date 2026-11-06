using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Services;

/// <summary>Empresas: autocompletado y resolución «crear o reutilizar» por nombre normalizado.</summary>
public class EmpresasService
{
    /// <summary>Máximo de sugerencias que devuelve el autocompletado.</summary>
    public const int MaxSugerencias = 20;

    private readonly IDbContextFactory<AppDbContext> _factory;
    public EmpresasService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    /// <summary>
    /// Empresas cuyo nombre normalizado contiene <paramref name="texto"/> (sin distinguir mayúsculas
    /// ni tildes), ordenadas por nombre. Sin texto devuelve las primeras por orden alfabético.
    /// </summary>
    public async Task<List<EmpresaDto>> BuscarAsync(string? texto)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var busqueda = NombresEmpresa.Normalizar(texto);

        var consulta = db.Empresas.AsNoTracking();
        if (busqueda.Length > 0)
            consulta = consulta.Where(e => e.NombreNormalizado.Contains(busqueda));

        return await consulta
            .OrderBy(e => e.NombreNormalizado)
            .Take(MaxSugerencias)
            .Select(e => new EmpresaDto(e.Id, e.Nombre, e.Web, e.Candidaturas.Count))
            .ToListAsync();
    }

    /// <summary>
    /// Devuelve la empresa (ya seguida por <paramref name="db"/>) con el mismo nombre normalizado
    /// o crea una nueva con el nombre tal cual se escribió (recortado). Mira primero las empresas que
    /// el contexto ya sigue (incluidas las recién añadidas y aún sin guardar) y después la BD. No
    /// guarda: lo hace quien llama, en la misma transacción que la candidatura, y si otra petición
    /// crea la misma empresa entre medias, quien llama reintenta con <see cref="ReutilizarDuplicadasAsync"/>.
    /// </summary>
    /// <exception cref="ValidacionException">El nombre está vacío o se queda sin letras ni números al
    /// normalizarlo («, S.L.», «...»).</exception>
    internal static async Task<Empresa> ObtenerOCrearAsync(AppDbContext db, string? nombre)
    {
        var normalizado = NombresEmpresa.Normalizar(nombre);
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ValidacionException("La empresa es obligatoria.", "empresa");
        if (!normalizado.Any(char.IsLetterOrDigit))
            throw new ValidacionException("El nombre de la empresa no es válido: tiene que llevar alguna letra o número además de la forma jurídica.", "empresa");

        var existente = db.Empresas.Local.FirstOrDefault(e => e.NombreNormalizado == normalizado)
            ?? await db.Empresas.FirstOrDefaultAsync(e => e.NombreNormalizado == normalizado);
        if (existente is not null) return existente;

        var nueva = new Empresa { Nombre = nombre.Trim(), NombreNormalizado = normalizado };
        db.Empresas.Add(nueva);
        return nueva;
    }

    /// <summary>
    /// ¿Es <paramref name="error"/> la violación del índice único de <c>Empresas.NombreNormalizado</c>?
    /// Pasa cuando dos altas simultáneas crean a la vez la misma empresa nueva.
    /// </summary>
    internal static bool EsEmpresaDuplicada(DbUpdateException error) =>
        ErroresBaseDatos.EsViolacionUnica(error, out var detalle)
        && detalle.Contains("Empresas.NombreNormalizado", StringComparison.Ordinal);

    /// <summary>
    /// Guarda y, si otra petición ha creado a la vez una de las empresas nuevas (salta el índice único de
    /// <c>Empresas.NombreNormalizado</c>), reintenta UNA vez apuntando a la empresa ya creada.
    /// </summary>
    internal static async Task GuardarReutilizandoEmpresasAsync(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException error) when (EsEmpresaDuplicada(error))
        {
            if (!await ReutilizarDuplicadasAsync(db)) throw;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Tras un <see cref="EsEmpresaDuplicada"/>: cada empresa que <paramref name="db"/> iba a crear y que
    /// otra petición acaba de guardar con el mismo nombre normalizado deja de crearse (se quita del
    /// contexto), y las candidaturas que la usaban pasan a apuntar a la ya guardada. Sirve para un alta
    /// suelta y para una importación con varias candidaturas y empresas nuevas. Devuelve false si no hay
    /// nada que reintentar.
    /// </summary>
    internal static async Task<bool> ReutilizarDuplicadasAsync(AppDbContext db)
    {
        var nuevas = db.ChangeTracker.Entries<Empresa>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();
        if (nuevas.Count == 0) return false;

        var nombres = nuevas.Select(e => e.NombreNormalizado).ToList();
        var guardadas = await db.Empresas
            .Where(e => nombres.Contains(e.NombreNormalizado))
            .ToDictionaryAsync(e => e.NombreNormalizado, StringComparer.Ordinal);
        var candidaturas = db.ChangeTracker.Entries<Candidatura>().Select(e => e.Entity).ToList();

        var reutilizada = false;
        foreach (var empresa in nuevas)
        {
            if (!guardadas.TryGetValue(empresa.NombreNormalizado, out var existente)) continue;

            // Primero se reasigna (y EF mueve las candidaturas de una empresa a otra) y después se quita la
            // duplicada: al revés, EF entendería que las candidaturas se quedan sin empresa.
            foreach (var candidatura in candidaturas.Where(c => ReferenceEquals(c.Empresa, empresa)))
            {
                candidatura.Empresa = existente;
                candidatura.EmpresaId = existente.Id;
            }
            db.ChangeTracker.DetectChanges();
            db.Entry(empresa).State = EntityState.Detached;
            reutilizada = true;
        }
        return reutilizada;
    }
}
