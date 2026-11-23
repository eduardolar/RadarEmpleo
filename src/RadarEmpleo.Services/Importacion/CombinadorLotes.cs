using System.Globalization;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Agentes;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Ofertas;

namespace RadarEmpleo.Services.Importacion;

/// <summary>Una fila extraída que se ha quedado fuera del lote porque ya estaba, con el aviso para enseñarlo tal cual.</summary>
/// <param name="Linea">Línea de su <c>cita</c> en el registro, empezando en 1.</param>
public sealed record FilaFueraDelLote(int Linea, string Empresa, string Puesto, DateOnly Fecha, string Aviso);

/// <summary>Lo que sale de unir la tabla del registro y lo extraído por Claude del texto libre.</summary>
/// <param name="Filas">Las de la tabla, en su orden, y detrás las extraídas, por línea. Como mucho <see cref="LimitesLote.Filas"/>.</param>
/// <param name="SinLeer">Las de la tabla y las extraídas que no se han podido leer o no caben, por línea (las sin línea, al final).</param>
/// <param name="Fuera">Las extraídas que ya estaban en la tabla o más arriba en el texto libre.</param>
public sealed record LoteCombinado(IReadOnlyList<FilaLote> Filas, IReadOnlyList<FilaSinLeerDto> SinLeer, IReadOnlyList<FilaFueraDelLote> Fuera);

/// <summary>
/// Une la tabla del registro (<see cref="AnalizadorRegistro.Analizar"/>) y lo que ExtraerRegistro ha leído del texto libre
/// (función pura: ni disco ni BD). Las reglas:
/// <list type="bullet">
/// <item><b>La <c>cita</c> tiene que ser una línea del texto enviado</b> (<see cref="AnalizadorRegistro.TextoLibre"/>, sin la
/// tabla ni los rastreos), entera: no vale un trozo ni dos líneas juntas. Las dos se comparan con la limpieza de
/// <see cref="PlantillaPrompt"/> (sin invisibles, NFKC, delimitadores neutralizados) y los espacios seguidos en uno. Si
/// encaja con varias líneas, vale la primera. Si no encaja con ninguna, la fila va a SinLeer con la línea 0 y el principio
/// de la cita en el motivo: es la defensa contra filas inventadas.</item>
/// <item><b>Manda la tabla</b>: una fila extraída con la misma empresa normalizada que una de la tabla y una fecha a 3 días
/// o menos se queda fuera con <see cref="AvisoYaEnLaTabla"/>. Eso incluye la misma <see cref="ReglasImportacion.Clave"/>,
/// que lleva la empresa normalizada y la fecha exacta.</item>
/// <item>Dos extraídas con la misma clave: vale la de la línea más arriba y la otra se queda fuera con
/// <see cref="AvisoRepetidaEnElTexto"/>.</item>
/// <item>Las <see cref="ResultadoExtraccionRegistro.SinLeer"/> van a SinLeer con la línea de su cita (recortada a
/// <see cref="ManejadorExtraerRegistro.LargoCitaSinLeer"/>, se busca como principio de línea) o, si no se encuentra, con la
/// línea 0 y la cita en el motivo.</item>
/// <item><b>Nada que dé 400 al aplicar</b>: una fecha fuera de 2000-2100, un enumerado desconocido o un campo más largo que
/// <see cref="LimitesLote"/> (contado como lo cuenta la API) van a SinLeer; la nota larga se recorta; una descartada sin
/// motivo lleva <see cref="MotivoDescarte.Otro"/>. Lo que pasa de <see cref="LimitesLote.Filas"/> va a SinLeer con
/// <see cref="MotivoNoCabe"/>: primero entran las de la tabla.</item>
/// </list>
/// El enlace va tal cual: se normaliza después, en <see cref="ReglasImportacion"/>.
/// </summary>
public static class CombinadorLotes
{
    public const string AvisoYaEnLaTabla = "Ya está en la tabla del registro";
    public const string AvisoRepetidaEnElTexto = "Aparece dos veces en el texto libre";
    public const string MotivoCitaInexistente = "No encuentro esta línea en el registro";
    public static readonly string MotivoNoCabe = $"No cabe: el lote ya tiene {LimitesLote.Filas} filas.";

    /// <summary>Días de margen entre una fila de la tabla y una extraída de la misma empresa para tomarlas por la misma.</summary>
    public const int DiasDeMargen = 3;

    /// <summary>Años que admite el aplicar (<c>ImportacionService.ValidarLote</c>).</summary>
    public const int AnioMinimo = 2000, AnioMaximo = 2100;

    private const int LargoCitaEnMotivo = 80;

    public static LoteCombinado Combinar(AnalisisRegistro tabla, string registro, ResultadoExtraccionRegistro extraccion)
    {
        var lineas = AnalizadorRegistro.TextoLibre(registro).Lineas
            .Select(l => (l.Numero, Texto: Comparable(l.Texto)))
            .Where(l => l.Texto.Length > 0)
            .ToList();
        var primeraLinea = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (numero, texto) in lineas) primeraLinea.TryAdd(texto, numero);

        var sinLeer = new List<FilaSinLeerDto>(tabla.SinLeer);
        foreach (var fila in extraccion.SinLeer)
        {
            var linea = BuscarPrincipio(lineas, primeraLinea, fila.Cita);
            sinLeer.Add(new FilaSinLeerDto(linea ?? 0, linea is null ? $"{fila.Motivo} {Citar(fila.Cita)}" : fila.Motivo));
        }

        var extraidas = new List<FilaLote>();
        foreach (var fila in extraccion.Candidaturas)
        {
            if (!primeraLinea.TryGetValue(Comparable(fila.Cita), out var linea))
                sinLeer.Add(new FilaSinLeerDto(0, $"{MotivoCitaInexistente}: {Citar(fila.Cita)}"));
            else if (Problema(fila) is { } motivo)
                sinLeer.Add(new FilaSinLeerDto(linea, motivo));
            else
                extraidas.Add(AFilaLote(fila, linea));
        }

        var deLaTabla = tabla.Lote.Filas.ToLookup(f => NombresEmpresa.Normalizar(f.Empresa), f => f.Fecha, StringComparer.Ordinal);
        var claves = new HashSet<string>(StringComparer.Ordinal);
        var filas = new List<FilaLote>(tabla.Lote.Filas);
        var fuera = new List<FilaFueraDelLote>();
        foreach (var fila in extraidas.OrderBy(f => f.Linea))
        {
            var aviso = deLaTabla[NombresEmpresa.Normalizar(fila.Empresa)].Any(f => Math.Abs(f.DayNumber - fila.Fecha.DayNumber) <= DiasDeMargen)
                ? AvisoYaEnLaTabla
                : !claves.Add(ReglasImportacion.Clave(fila.Empresa, fila.Puesto, fila.Fecha)) ? AvisoRepetidaEnElTexto : null;
            if (aviso is not null) fuera.Add(new FilaFueraDelLote(fila.Linea, fila.Empresa, fila.Puesto, fila.Fecha, aviso));
            else filas.Add(fila);
        }

        // Lo que no cabe: primero entran las de la tabla y luego las extraídas por línea.
        sinLeer.AddRange(filas.Skip(LimitesLote.Filas).Select(f => new FilaSinLeerDto(f.Linea, MotivoNoCabe)));
        return new LoteCombinado(
            filas.Take(LimitesLote.Filas).ToList(),
            sinLeer.OrderBy(f => f.Linea == 0).ThenBy(f => f.Linea).ToList(),
            fuera);
    }

    /// <summary>
    /// Una línea (o una cita) tal como se compara: con la limpieza de <see cref="PlantillaPrompt.Normalizar"/> en una línea y
    /// los espacios y tabuladores seguidos en uno. Se repite hasta que no cambia, porque la cita ya viene limpiada una vez y
    /// limpiar otra vez no siempre deja lo mismo («{{{» pasa a «{ {{» y luego a «{ { {»).
    /// </summary>
    private static string Comparable(string texto)
    {
        string anterior;
        do
        {
            anterior = texto;
            texto = HtmlATexto.NormalizarEspacios(PlantillaPrompt.Normalizar(texto, unaLinea: true));
        } while (texto != anterior);
        return texto;
    }

    /// <summary>La línea de una cita de una fila sin leer: exacta o, si viene recortada con «…», la primera que empieza por ella.</summary>
    private static int? BuscarPrincipio(List<(int Numero, string Texto)> lineas, Dictionary<string, int> primeraLinea, string cita)
    {
        var comparable = Comparable(cita);
        if (comparable.Length == 0) return null;
        if (primeraLinea.TryGetValue(comparable, out var exacta)) return exacta;
        if (!cita.EndsWith('…') || Comparable(cita[..^1]) is not { Length: > 0 } principio) return null;
        return lineas.FirstOrDefault(l => l.Texto.StartsWith(principio, StringComparison.Ordinal)) is { Numero: > 0 } encontrada
            ? encontrada.Numero
            : null;
    }

    /// <summary>Lo que haría que el aplicar diera 400 o rompiera la fila: la fila va a SinLeer con este motivo.</summary>
    private static string? Problema(FilaExtraida fila)
    {
        if (fila.Fecha.Year is < AnioMinimo or > AnioMaximo)
            return $"La fecha {fila.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} no es válida para importarla.";
        if (!Enum.IsDefined(fila.Estado) || !Enum.IsDefined(fila.Modalidad)
            || fila.MotivoDescarte is { } motivo && !Enum.IsDefined(motivo))
            return "El estado, la modalidad o el motivo no se reconocen.";
        if (!NombresEmpresa.Normalizar(fila.Empresa).Any(char.IsLetterOrDigit))
            return "El nombre de la empresa no es válido.";

        var demasiadoLargo =
            fila.Empresa.Length > LimitesLote.Empresa ? "La empresa" :
            fila.Puesto.Length > LimitesLote.Puesto ? "El puesto" :
            fila.Ubicacion?.Length > LimitesLote.Ubicacion ? "La ubicación" :
            fila.Enlace?.Length > LimitesLote.Enlace ? "El enlace" : null;
        return demasiadoLargo is null ? null : $"{demasiadoLargo}: demasiado largo para importarlo.";
    }

    private static FilaLote AFilaLote(FilaExtraida f, int linea)
    {
        var descartada = f.Estado == EstadoCandidatura.Descartada;
        return new FilaLote(
            linea, f.Fecha, f.FechaAproximada, f.Empresa, f.Puesto, f.Modalidad,
            Ubicacion: f.Ubicacion,
            Enlace: f.Enlace,
            Estado: f.Estado,
            MotivoDescarte: descartada ? f.MotivoDescarte ?? MotivoDescarte.Otro : null,
            Nota: f.Nota is null ? null : Recortar(f.Nota, LimitesLote.Nota));
    }

    private static string Citar(string cita) => $"«{Recortar(cita, LargoCitaEnMotivo)}».";

    /// <summary>Recorta a <paramref name="maximo"/> unidades UTF-16 (como cuenta la API), sin partir un carácter.</summary>
    private static string Recortar(string texto, int maximo)
    {
        if (texto.Length <= maximo) return texto;
        var corte = maximo - 1;
        if (char.IsHighSurrogate(texto[corte - 1])) corte--;
        return $"{texto[..corte].TrimEnd()}…";
    }
}
