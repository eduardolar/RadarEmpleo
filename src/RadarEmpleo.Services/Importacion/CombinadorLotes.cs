using System.Globalization;
using System.Text;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Agentes;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Ofertas;

namespace RadarEmpleo.Services.Importacion;

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
/// <item><b>La empresa tiene que salir en la línea citada</b>: si no (otra línea, el título, unas siglas), la fila entra
/// igual con <see cref="FilaLote.EmpresaFueraDeLaCita"/>, que la vista previa enseña como
/// <see cref="ReglasImportacion.AvisoEmpresaNoEnLaLinea"/>. Se comparan las palabras enteras, sin forma jurídica, tildes
/// ni signos; si no, la empresa sin espacios (4 caracteres o más) dentro de la línea sin espacios, para «B.B.V.A.».</item>
/// <item>Las extraídas llevan su sección con el prefijo <see cref="AnalizadorRegistro.SeccionTextoLibre"/>.</item>
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

    /// <summary>Lo mínimo que tiene que medir una empresa sin espacios para buscarla pegada dentro de la línea.</summary>
    private const int LargoEmpresaPegada = 4;

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
            var cita = Comparable(fila.Cita);
            if (!primeraLinea.TryGetValue(cita, out var linea))
                sinLeer.Add(new FilaSinLeerDto(0, $"{MotivoCitaInexistente}: {Citar(fila.Cita)}"));
            else if (Problema(fila) is { } motivo)
                sinLeer.Add(new FilaSinLeerDto(linea, motivo));
            else
                extraidas.Add(AFilaLote(fila, linea, empresaEnLaLinea: EmpresaEnLinea(fila.Empresa, cita)));
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
    /// los espacios y tabuladores seguidos en uno. Los invisibles se quitan antes de pasar a una línea, como en el prompt
    /// (que limpia el texto entero): así un tabulador vertical (U+000B) o un salto de página (U+000C) del registro
    /// desaparece en vez de volverse un espacio. Se repite hasta que no cambia, porque la cita ya viene limpiada una vez y
    /// limpiar otra vez no siempre deja lo mismo («{{{» pasa a «{ {{» y luego a «{ { {»).
    /// </summary>
    private static string Comparable(string texto)
    {
        string anterior;
        do
        {
            anterior = texto;
            texto = HtmlATexto.NormalizarEspacios(PlantillaPrompt.Normalizar(PlantillaPrompt.QuitarInvisibles(texto), unaLinea: true));
        } while (texto != anterior);
        return texto;
    }

    /// <summary>La línea de una cita de una fila sin leer: exacta o, si el manejador la recortó (justo
    /// <see cref="ManejadorExtraerRegistro.LargoCitaSinLeer"/> caracteres acabados en «…»), la primera que empieza por ella.
    /// Una cita corta que acaba en «…» por sí sola no se busca como principio: daría una línea equivocada.</summary>
    private static int? BuscarPrincipio(List<(int Numero, string Texto)> lineas, Dictionary<string, int> primeraLinea, string cita)
    {
        var comparable = Comparable(cita);
        if (comparable.Length == 0) return null;
        if (primeraLinea.TryGetValue(comparable, out var exacta)) return exacta;
        if (!cita.EndsWith('…') || cita.EnumerateRunes().Count() != ManejadorExtraerRegistro.LargoCitaSinLeer
            || Comparable(cita[..^1]) is not { Length: > 0 } principio) return null;
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

    private static FilaLote AFilaLote(FilaExtraida f, int linea, bool empresaEnLaLinea)
    {
        var descartada = f.Estado == EstadoCandidatura.Descartada;
        return new FilaLote(
            linea, f.Fecha, f.FechaAproximada, f.Empresa, f.Puesto, f.Modalidad,
            Ubicacion: f.Ubicacion,
            Enlace: f.Enlace,
            Estado: f.Estado,
            MotivoDescarte: descartada ? f.MotivoDescarte ?? MotivoDescarte.Otro : null,
            Nota: f.Nota is null ? null : Recortar(f.Nota, LimitesLote.Nota),
            Seccion: string.IsNullOrWhiteSpace(f.Seccion)
                ? AnalizadorRegistro.SeccionTextoLibre
                : $"{AnalizadorRegistro.SeccionTextoLibre}: {f.Seccion}",
            EmpresaFueraDeLaCita: !empresaEnLaLinea);
    }

    /// <summary>
    /// Si la empresa sale en la línea citada (ya <see cref="Comparable"/>): las dos sin tildes ni mayúsculas, la empresa sin
    /// forma jurídica y todo lo que no es letra o dígito como un espacio. Vale con las palabras enteras o, si la empresa
    /// tiene <see cref="LargoEmpresaPegada"/> caracteres o más, con las dos sin espacios y empezando donde empieza una palabra
    /// («B.B.V.A.» y «BBVA»). «Grupo» no se quita: «Grupo Inditec» en la empresa y solo «Inditec» en la línea no encaja.
    /// </summary>
    private static bool EmpresaEnLinea(string empresa, string linea)
    {
        var nombre = Palabras(NombresEmpresa.Normalizar(Comparable(empresa)));
        var texto = Palabras(TextoComparable.Normalizar(linea));
        if (nombre.Length == 0) return false;
        if ($" {texto} ".Contains($" {nombre} ", StringComparison.Ordinal)) return true;
        var pegado = nombre.Replace(" ", "", StringComparison.Ordinal);
        return pegado.Length >= LargoEmpresaPegada && PegadoDesdeUnaPalabra(pegado, texto.Split(' '));
    }

    /// <summary>Si <paramref name="pegado"/> sale en las palabras unidas sin espacios empezando donde empieza una de ellas:
    /// «B.B.V.A.» encaja con «BBVA», pero «Acme» no con «Pacmen».</summary>
    private static bool PegadoDesdeUnaPalabra(string pegado, string[] palabras)
    {
        var unido = string.Concat(palabras);
        var inicios = new HashSet<int>();
        var posicion = 0;
        foreach (var palabra in palabras)
        {
            inicios.Add(posicion);
            posicion += palabra.Length;
        }
        for (var i = unido.IndexOf(pegado, StringComparison.Ordinal); i >= 0; i = unido.IndexOf(pegado, i + 1, StringComparison.Ordinal))
        {
            if (inicios.Contains(i)) return true;
        }
        return false;
    }

    /// <summary>Solo letras y dígitos: lo demás pasa a un espacio, sin repetirlos ni dejarlos en los extremos.</summary>
    private static string Palabras(string texto)
    {
        var resultado = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            if (char.IsLetterOrDigit(c)) resultado.Append(c);
            else if (resultado.Length > 0 && resultado[^1] != ' ') resultado.Append(' ');
        }
        return resultado.ToString().TrimEnd();
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
