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
    public const string AvisoEmpresaNoEnLaLinea = "Revisa la empresa: Claude no la ve escrita en esa línea. Si está mal, corrígela en tu archivo antes de añadir.";
    public const string AvisoEstadoDeClaude = "Estado leído por Claude: compruébalo en tu archivo.";

    /// <summary>Conflicto de una fila del texto libre que cambiaría el estado sin que su línea lo respalde.</summary>
    public static string MotivoEstadoSinRespaldo(EstadoCandidatura estado) =>
        $"Claude dice que pasó a «{ReglasEstado.Texto(estado)}», pero tu archivo no lo dice: revísalo en la ficha.";

    /// <summary>Conflicto de una fila del texto libre que llevaría la candidatura a Entrevista, Oferta, Descartada o Cerrada
    /// (<see cref="NoSeAplicaSolo"/>), aunque su línea lo diga.</summary>
    public static string MotivoEstadoFinalDelTextoLibre(EstadoCandidatura estado) =>
        $"Claude ha leído en el texto libre que pasó a «{ReglasEstado.Texto(estado)}»: eso no se aplica solo, cámbialo tú en la ficha.";

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
    public static ResumenImportacionDto Resumir(IReadOnlyCollection<FilaVistaPreviaDto> filas, int sinLeer, int enviosEnTexto,
        int delTextoLibre = 0)
    {
        int Contar(ResultadoFila resultado) => filas.Count(f => f.Resultado == resultado);

        return new ResumenImportacionDto(Contar(ResultadoFila.Nueva), Contar(ResultadoFila.AvanzaEstado),
            Contar(ResultadoFila.YaExiste), Contar(ResultadoFila.Conflicto), sinLeer, enviosEnTexto, delTextoLibre);
    }

    /// <summary>Enlace listo para guardar, o null si no hay o no es una dirección web de un tamaño admitido.</summary>
    public static string? EnlaceParaGuardar(string? enlace) =>
        Enlaces.Normalizar(enlace) is { Length: > 0 and <= LimitesLote.Enlace } normalizado ? normalizado : null;

    /// <summary>
    /// Los estados que, leídos por Claude en el texto libre, nunca se dan por buenos solos: los que cierran la candidatura,
    /// Oferta, porque «oferta» sale en cualquier línea («Vi la oferta de Acme en LinkedIn») y no prueba nada, y Entrevista,
    /// porque la palabra tampoco dice que la haya («Entrevista: no me llamaron», «solicité entrevista»). Del texto libre
    /// solo Preparada y Enviada pueden aplicarse solas, y solo si su línea las respalda.
    /// </summary>
    private static bool NoSeAplicaSolo(EstadoCandidatura estado) =>
        ReglasEstado.EsFinal(estado) || estado is EstadoCandidatura.Entrevista or EstadoCandidatura.Oferta;

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

        if (!ReglasEstado.PuedePasar(misma.Estado, fila.Estado))
            return Fila(fila, clave, ResultadoFila.YaExiste, misma, null);

        // Lo del texto libre lo ha leído Claude: un cambio que su línea no respalda, o a Entrevista, Oferta, Descartada o Cerrada, no
        // se aplica con el botón general. Las filas de la tabla (sin sección) no pasan por aquí.
        if (fila.Seccion is not null && fila.EstadoSinRespaldo)
            return Fila(fila, clave, ResultadoFila.Conflicto, misma, MotivoEstadoSinRespaldo(fila.Estado));
        if (fila.Seccion is not null && NoSeAplicaSolo(fila.Estado))
            return Fila(fila, clave, ResultadoFila.Conflicto, misma, MotivoEstadoFinalDelTextoLibre(fila.Estado));
        return Fila(fila, clave, ResultadoFila.AvanzaEstado, misma, null);
    }

    private static FilaVistaPreviaDto Fila(FilaLote fila, string clave, ResultadoFila resultado, CandidaturaExistente? existente, string? motivo) =>
        new(clave, resultado, fila.Empresa, fila.Puesto, fila.Fecha, fila.Estado, existente?.Estado, existente?.Id, motivo, Avisos(fila, resultado),
            // Las de la tabla no llevan sección: las extraídas siempre («Texto libre del registro…»)
            fila.Seccion is not null);

    private static List<string> Avisos(FilaLote fila, ResultadoFila resultado)
    {
        var avisos = new List<string>();
        if (fila.FechaAproximada) avisos.Add(AvisoFechaAproximada);
        if (fila.EstadoDeducido) avisos.Add(AvisoEstadoDeducido);
        if (fila.EmpresaFueraDeLaCita) avisos.Add(AvisoEmpresaNoEnLaLinea);
        // Una nueva del texto libre en Entrevista, Oferta, Descartada o Cerrada avisa siempre, aunque su línea lo diga.
        if (fila.Seccion is not null && (fila.EstadoSinRespaldo || resultado == ResultadoFila.Nueva && NoSeAplicaSolo(fila.Estado)))
            avisos.Add(AvisoEstadoDeClaude);
        if (!string.IsNullOrWhiteSpace(fila.Enlace) && EnlaceParaGuardar(fila.Enlace) is null) avisos.Add(AvisoEnlaceNoValido);
        return avisos;
    }
}
