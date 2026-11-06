using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Curriculum;

/// <summary>
/// Escribe el radar como una tabla Markdown con una línea por candidatura, sin notas ni análisis. Va a
/// un temporal y se mueve encima del archivo para que nadie lea nunca uno a medias. El semáforo hace que
/// las exportaciones vayan de una en una, y como cada una lee la BD dentro de su turno, la última deja
/// siempre lo más reciente. Por eso es un singleton.
/// </summary>
public sealed partial class ExportadorRadarMd : IExportadorRadar
{
    private const string SinDato = "—";

    private static readonly UTF8Encoding Utf8SinBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly RutasCurriculum _rutas;
    private readonly IClock _reloj;
    private readonly ILogger<ExportadorRadarMd> _registro;
    private readonly SemaphoreSlim _turno = new(1, 1);

    public ExportadorRadarMd(IDbContextFactory<AppDbContext> factory, RutasCurriculum rutas, IClock reloj, ILogger<ExportadorRadarMd> registro)
    {
        _factory = factory;
        _rutas = rutas;
        _reloj = reloj;
        _registro = registro;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    public async Task<bool> ExportarAsync()
    {
        if (!_rutas.HayCarpeta) return false;

        await _turno.WaitAsync();
        try
        {
            await EscribirAsync(await GenerarAsync());
            return true;
        }
        catch (Exception error)
        {
            // Lo que ha pedido el usuario ya está guardado: el radar no puede convertirlo en un error.
            _registro.LogError(error, "No se ha podido escribir el radar de candidaturas.");
            return false;
        }
        finally
        {
            _turno.Release();
        }
    }

    private async Task<string> GenerarAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var candidaturas = await db.Candidaturas.AsNoTracking()
            .Select(c => new
            {
                c.Id,
                Fecha = c.FechaEnvio ?? c.FechaDeteccion,
                c.Estado,
                Empresa = c.Empresa.Nombre,
                c.Puesto,
                c.MotivoDescarte,
                c.MotivoDescarteTexto,
                c.Enlace
            })
            .ToListAsync();

        var generado = Fechas.UtcAMadrid(_reloj.AhoraUtc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var texto = new StringBuilder()
            .Append("# Candidaturas\n\n")
            .Append("NO EDITAR: lo genera RadarEmpleo. Lo que se cambie aquí se pierde.\n\n")
            .Append($"Generado el {generado} (hora de Madrid).\n\n")
            .Append("| Fecha | Estado | Empresa | Puesto | Motivo de descarte | Enlace |\n")
            .Append("|---|---|---|---|---|---|\n");

        foreach (var c in candidaturas.OrderByDescending(c => c.Fecha).ThenBy(c => c.Id))
        {
            var fecha = Fechas.UtcAMadrid(c.Fecha).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var celdas = new[]
            {
                fecha,
                ReglasEstado.Texto(c.Estado),
                c.Empresa,
                c.Puesto,
                Motivo(c.MotivoDescarte, c.MotivoDescarteTexto),
                c.Enlace
            };
            texto.Append($"| {string.Join(" | ", celdas.Select(Celda))} |\n");
        }
        return texto.ToString();
    }

    /// <summary>
    /// «Inglés: piden C1»; si solo hay uno de los dos, ese. «Otro» delante del texto no dice nada (y es
    /// el motivo de todo lo importado del registro), así que con «Otro» va solo el texto.
    /// </summary>
    private static string? Motivo(MotivoDescarte? motivo, string? texto)
    {
        var tieneTexto = !string.IsNullOrWhiteSpace(texto);
        return motivo switch
        {
            null => texto,
            MotivoDescarte.Otro when tieneTexto => texto,
            { } m when tieneTexto => $"{ReglasEstado.Texto(m)}: {texto}",
            { } m => ReglasEstado.Texto(m)
        };
    }

    /// <summary>Una celda en una sola línea: un salto o una barra romperían la fila.</summary>
    private static string Celda(string? texto)
    {
        var limpio = Espacios().Replace(texto ?? "", " ").Trim();
        return limpio.Length == 0 ? SinDato : limpio.Replace("|", @"\|");
    }

    private async Task EscribirAsync(string contenido)
    {
        var destino = _rutas.Radar!;
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

        var temporal = $"{destino}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporal, contenido, Utf8SinBom);
            File.Move(temporal, destino, overwrite: true);
        }
        finally
        {
            File.Delete(temporal);
        }
    }
}
