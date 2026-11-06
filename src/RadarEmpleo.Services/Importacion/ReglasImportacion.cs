using System.Globalization;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Services.Importacion;

/// <summary>Lo que hace falta saber de una candidatura guardada para compararla con el registro.</summary>
public sealed record CandidaturaExistente(int Id, string? ClaveImportacion, string EmpresaNormalizada, string Puesto, EstadoCandidatura Estado);

/// <summary>
/// Reglas de deduplicación (funciones puras): la clave de cada fila y qué pasaría al aplicarla. Nunca
/// se hace retroceder un estado, nunca se reabre una candidatura y nunca se duplica.
/// </summary>
public static class ReglasImportacion
{
    public const string MotivoRepetida = "Aparece dos veces en el registro";
    public const string MotivoParecida = "Ya tienes una parecida";
    public const string MotivoParecidaEnElRegistro = "Ya aparece más arriba en el registro con otra fecha";
    public const string AvisoFechaAproximada = "La fecha es aproximada.";
    public const string AvisoEnlaceNoValido = "El enlace no es válido: se importará sin enlace.";
    public const string AvisoEstadoDeducido = "Estado deducido del texto: compruébalo.";

    /// <summary>
    /// Clave de idempotencia: <c>empresa normalizada|puesto normalizado|AAAA-MM-DD</c>. Se guarda al
    /// crear y no se recalcula al editar, y por eso editar una importada no hace que vuelva como nueva.
    /// </summary>
    public static string Clave(string empresa, string puesto, DateOnly fecha) =>
        $"{Parecido(NombresEmpresa.Normalizar(empresa), puesto)}|{fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Compara cada fila con lo guardado, en el mismo orden que <paramref name="filas"/>. Si una clave
    /// se repite en el lote, la primera vale y las demás son conflicto. Lo mismo con las parecidas
    /// (misma empresa y puesto, otra fecha): la primera es nueva y las demás, para revisar, igual que
    /// si la primera ya estuviera guardada. Así da igual aplicar el lote entero o fila a fila.
    /// </summary>
    public static List<FilaVistaPreviaDto> Comparar(IEnumerable<FilaLote> filas, IReadOnlyCollection<CandidaturaExistente> existentes)
    {
        var porClave = existentes
            .Where(c => c.ClaveImportacion is not null)
            .ToDictionary(c => c.ClaveImportacion!, StringComparer.Ordinal);
        var porParecido = existentes.ToLookup(c => Parecido(c.EmpresaNormalizada, c.Puesto), StringComparer.Ordinal);
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        var parecidasVistas = new HashSet<string>(StringComparer.Ordinal);

        return filas.Select(fila =>
        {
            var clave = Clave(fila.Empresa, fila.Puesto, fila.Fecha);
            var parecido = Parecido(NombresEmpresa.Normalizar(fila.Empresa), fila.Puesto);

            if (!vistas.Add(clave))
                return Fila(fila, clave, ResultadoFila.Conflicto, null, MotivoRepetida);
            if (porClave.TryGetValue(clave, out var misma))
                return CompararEstado(fila, clave, misma);
            if (porParecido[parecido].OrderBy(c => c.Id).FirstOrDefault() is { } similar)
                return Fila(fila, clave, ResultadoFila.Conflicto, similar, MotivoParecida);
            if (!parecidasVistas.Add(parecido))
                return Fila(fila, clave, ResultadoFila.Conflicto, null, MotivoParecidaEnElRegistro);
            return Fila(fila, clave, ResultadoFila.Nueva, null, null);
        }).ToList();
    }

    /// <summary>Cuenta las filas comparadas de cada tipo.</summary>
    public static ResumenImportacionDto Resumir(IReadOnlyCollection<FilaVistaPreviaDto> filas, int sinLeer, int enviosEnTexto)
    {
        int Contar(ResultadoFila resultado) => filas.Count(f => f.Resultado == resultado);

        return new ResumenImportacionDto(Contar(ResultadoFila.Nueva), Contar(ResultadoFila.AvanzaEstado),
            Contar(ResultadoFila.YaExiste), Contar(ResultadoFila.Conflicto), sinLeer, enviosEnTexto);
    }

    /// <summary>Enlace listo para guardar, o null si no hay o no es una dirección web de un tamaño admitido.</summary>
    public static string? EnlaceParaGuardar(string? enlace) =>
        Enlaces.Normalizar(enlace) is { Length: > 0 and <= LimitesLote.Enlace } normalizado ? normalizado : null;

    private static string Parecido(string empresaNormalizada, string puesto) =>
        $"{empresaNormalizada}|{TextoComparable.Normalizar(puesto)}";

    private static FilaVistaPreviaDto CompararEstado(FilaLote fila, string clave, CandidaturaExistente misma)
    {
        if (misma.Estado == fila.Estado)
            return Fila(fila, clave, ResultadoFila.YaExiste, misma, null);

        if (ReglasEstado.EsFinal(misma.Estado))
        {
            var motivo = $"En la app está en «{ReglasEstado.Texto(misma.Estado)}» y en el registro en «{ReglasEstado.Texto(fila.Estado)}»: no se reabre.";
            return Fila(fila, clave, ResultadoFila.Conflicto, misma, motivo);
        }

        var resultado = ReglasEstado.PuedePasar(misma.Estado, fila.Estado) ? ResultadoFila.AvanzaEstado : ResultadoFila.YaExiste;
        return Fila(fila, clave, resultado, misma, null);
    }

    private static FilaVistaPreviaDto Fila(FilaLote fila, string clave, ResultadoFila resultado, CandidaturaExistente? existente, string? motivo) =>
        new(clave, resultado, fila.Empresa, fila.Puesto, fila.Fecha, fila.Estado, existente?.Estado, existente?.Id, motivo, Avisos(fila));

    private static List<string> Avisos(FilaLote fila)
    {
        var avisos = new List<string>();
        if (fila.FechaAproximada) avisos.Add(AvisoFechaAproximada);
        if (fila.EstadoDeducido) avisos.Add(AvisoEstadoDeducido);
        if (!string.IsNullOrWhiteSpace(fila.Enlace) && EnlaceParaGuardar(fila.Enlace) is null) avisos.Add(AvisoEnlaceNoValido);
        return avisos;
    }
}
