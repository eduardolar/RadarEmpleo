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
    /// crea la misma empresa entre medias, quien llama reintenta con <see cref="ReutilizarSiDuplicadaAsync"/>.
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
    /// Tras un <see cref="EsEmpresaDuplicada"/>: si <paramref name="candidatura"/> iba a crear su
    /// empresa, deja de crearla (la quita del contexto) y la apunta a la que otra petición acaba de
    /// guardar con el mismo nombre normalizado. Devuelve false si no hay nada que reintentar.
    /// </summary>
    internal static async Task<bool> ReutilizarSiDuplicadaAsync(AppDbContext db, Candidatura candidatura)
    {
        var empresa = candidatura.Empresa;
        if (db.Entry(empresa).State != EntityState.Added) return false;

        var existente = await db.Empresas.FirstOrDefaultAsync(e => e.NombreNormalizado == empresa.NombreNormalizado);
        if (existente is null) return false;

        // Primero se reasigna (y EF mueve la candidatura de una empresa a otra) y después se quita la
        // duplicada: al revés, EF entendería que la candidatura se queda sin empresa.
        candidatura.Empresa = existente;
        candidatura.EmpresaId = existente.Id;
        db.ChangeTracker.DetectChanges();
        db.Entry(empresa).State = EntityState.Detached;
        return true;
    }
}
