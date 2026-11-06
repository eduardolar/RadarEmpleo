using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Services.Importacion;

/// <summary>Lo que sale de analizar el registro.</summary>
/// <param name="TablaEncontrada">False si ninguna tabla tiene las columnas necesarias.</param>
/// <param name="Lote">Las filas que se han entendido.</param>
/// <param name="SinLeer">Las filas de la tabla que no se han entendido.</param>
/// <param name="EnviosEnTexto">Líneas con «ENVIADA» fuera de la tabla.</param>
public sealed record AnalisisRegistro(
    bool TablaEncontrada,
    LoteImportacion Lote,
    IReadOnlyList<FilaSinLeerDto> SinLeer,
    int EnviosEnTexto);

/// <summary>
/// Convierte el texto del registro en un lote (función pura: ni disco ni BD). Solo interpreta la tabla:
/// el resto del archivo se cuenta, no se interpreta. Una fila que no se entiende va a
/// <see cref="AnalisisRegistro.SinLeer"/>: nunca se adivina y nunca rompe las demás.
/// </summary>
public static partial class AnalizadorRegistro
{
    /// <summary>Valor de <see cref="Candidatura.SeccionOrigen"/> de lo importado de la tabla.</summary>
    public const string SeccionTabla = "Tabla del registro";

    private const string MarcaEnvioEnTexto = "ENVIADA";

    private static readonly string[] ColumnasObligatorias = ["fecha", "empresa", "puesto", "estado"];

    private static readonly string[] Portales = ["LinkedIn", "InfoJobs", "Indeed", "Tecnoempleo", "Workday"];

    // Se comparan con el texto ya pasado por TextoComparable, así que van sin tildes ni mayúsculas.
    private static readonly (string Prefijo, EstadoCandidatura Estado)[] PrefijosEstado =
    [
        ("enviad", EstadoCandidatura.Enviada),
        ("descartad", EstadoCandidatura.Descartada),
        ("caducad", EstadoCandidatura.Cerrada),
        ("cerrad", EstadoCandidatura.Cerrada),
        ("entrevista", EstadoCandidatura.Entrevista),
        ("oferta", EstadoCandidatura.Oferta),
        ("preparad", EstadoCandidatura.Preparada),
        ("detectad", EstadoCandidatura.Detectada)
    ];

    // Para cuando el estado no va al principio («Llamada para concertar; entrevista el 11/11»). Por
    // prioridad: lo que cierra la candidatura va antes que lo que la hace avanzar.
    private static readonly (string Palabra, EstadoCandidatura Estado)[] PalabrasEstado =
    [
        ("cerrad", EstadoCandidatura.Cerrada),
        ("caducad", EstadoCandidatura.Cerrada),
        ("descartad", EstadoCandidatura.Descartada),
        ("entrevista", EstadoCandidatura.Entrevista),
        ("oferta", EstadoCandidatura.Oferta),
        ("enviad", EstadoCandidatura.Enviada)
    ];

    // Un texto que duda, niega o espera («Quizá enviada», «No descartada aún», «Pendiente de entrevista»)
    // no se interpreta por lo que contiene: solo vale cómo empieza.
    private static readonly HashSet<string> PalabrasDeDuda =
        ["no", "sin", "quiza", "quizas", "pendiente", "esperando", "espera", "posible"];

    // «Inscripciones cerradas» o «plazo cerrado» hablan de la oferta, no de la candidatura.
    private static readonly HashSet<string> CierresQueNoSonDeLaCandidatura = ["inscripciones", "inscripcion", "plazo"];

    [GeneratedRegex(@"\r\n|\r|\n")]
    private static partial Regex SaltoDeLinea();

    [GeneratedRegex(@"^\|[\s:|-]+$")]
    private static partial Regex LineaSeparadora();

    [GeneratedRegex(@"^\(\s*antes del\s+(\d{1,2})-(\d{1,2})\s*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FechaAproximada();

    [GeneratedRegex(@"^presencial\s*,\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UbicacionPresencial();

    // Solo con mayúscula inicial: «Híbrido Málaga» es un sitio, «Híbrido 3 días» o «Híbrido flexible» no.
    // (?i:) solo en la palabra: con IgnoreCase global, \p{Lu} también aceptaría minúsculas.
    [GeneratedRegex(@"^(?i:h[ií]brid[oa])[\s,]+(\p{Lu}.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex UbicacionHibrido();

    // Negrita, tachado y código: sus marcas no forman parte de ningún nombre.
    [GeneratedRegex(@"\*\*|~~|`")]
    private static partial Regex MarcasFormato();

    // Cursiva con _ o * (y negrita con __) solo si envuelve texto y por fuera no está pegada a letras o
    // números: así «snake_case» o una URL con un guion bajo se quedan como están.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(__|[_*])(?=\S)(.+?)(?<=\S)\1(?![\p{L}\p{N}])")]
    private static partial Regex Enfasis();

    // En el estado, lo tachado ya no vale: «~~Descartada~~ Enviada» es Enviada.
    [GeneratedRegex(@"~~.*?~~")]
    private static partial Regex Tachado();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex SeparadorPalabras();

    // «Descartada?»: la duda es sobre el propio estado.
    [GeneratedRegex(@"^\p{L}+\s*\?")]
    private static partial Regex EstadoConInterrogacion();

    // «Teletrabajo parcial» es híbrido; «jornada parcial» habla del horario, no de la modalidad.
    [GeneratedRegex(@"(?<!\bjornada\s+)\bparcial")]
    private static partial Regex Parcial();

    [GeneratedRegex(@"^\[[^\]]*\]\((.+)\)$")]
    private static partial Regex EnlaceMarkdown();

    [GeneratedRegex(@"^<(.+)>$")]
    private static partial Regex EnlaceAngular();

    public static AnalisisRegistro Analizar(string texto)
    {
        var lineas = SaltoDeLinea().Split(texto.TrimStart('﻿'));

        var cabecera = Array.FindIndex(lineas, EsCabecera);
        if (cabecera < 0)
            return new AnalisisRegistro(false, LoteVacio(), [], ContarEnvios(lineas));

        var columnas = Columnas.Desde(Celdas(lineas[cabecera]));
        var filas = new List<FilaLote>();
        var sinLeer = new List<FilaSinLeerDto>();
        // Se anula si la fila no tiene una fecha legible: «la fila anterior» es la anterior de verdad.
        DateOnly? fechaAnterior = null;

        var fin = cabecera + 1;
        for (; fin < lineas.Length && EsLineaDeTabla(lineas[fin]); fin++)
        {
            if (LineaSeparadora().IsMatch(lineas[fin].Trim())) continue;

            var numero = fin + 1;
            var celdas = Celdas(lineas[fin]);
            if (celdas.Count != columnas.Total)
            {
                sinLeer.Add(new FilaSinLeerDto(numero, $"La fila tiene {celdas.Count} celdas y la cabecera {columnas.Total}."));
                fechaAnterior = null;
                continue;
            }

            var fecha = LeerFecha(LimpiarCelda(celdas[columnas.Fecha]), fechaAnterior);
            fechaAnterior = fecha?.Dia;

            var (fila, motivo) = LeerFila(numero, celdas, columnas, fecha);
            if (fila is null) sinLeer.Add(new FilaSinLeerDto(numero, motivo!));
            else filas.Add(fila);
        }

        var fuera = lineas.Take(cabecera).Concat(lineas.Skip(fin));
        return new AnalisisRegistro(true, LoteVacio() with { Filas = filas }, sinLeer, ContarEnvios(fuera));
    }

    private static LoteImportacion LoteVacio() => new(LimitesLote.VersionActual, OrigenLote.Tabla, []);

    private static int ContarEnvios(IEnumerable<string> lineas) =>
        lineas.Count(l => l.Contains(MarcaEnvioEnTexto, StringComparison.Ordinal));

    private static bool EsLineaDeTabla(string linea) => linea.TrimStart().StartsWith('|');

    private static bool EsCabecera(string linea)
    {
        if (!EsLineaDeTabla(linea)) return false;
        var nombres = Celdas(linea).Select(NombreDeColumna).ToHashSet();
        return ColumnasObligatorias.All(nombres.Contains);
    }

    /// <summary>
    /// Parte una línea en celdas. Un <c>\|</c> es una barra dentro de la celda. Una barra sin escapar
    /// parte la celda, y la fila acaba en <see cref="AnalisisRegistro.SinLeer"/> por el número de celdas.
    /// Las celdas salen solo recortadas: las marcas de formato se quitan campo a campo, porque en un
    /// enlace los guiones bajos o los asteriscos son parte de la dirección.
    /// </summary>
    private static List<string> Celdas(string linea)
    {
        var texto = linea.Trim();
        var celdas = new List<string>();
        var actual = new StringBuilder();
        for (var i = 0; i < texto.Length; i++)
        {
            if (texto[i] == '\\' && i + 1 < texto.Length && texto[i + 1] == '|')
            {
                actual.Append('|');
                i++;
            }
            else if (texto[i] == '|')
            {
                celdas.Add(actual.ToString());
                actual.Clear();
            }
            else
            {
                actual.Append(texto[i]);
            }
        }
        celdas.Add(actual.ToString());

        // Lo que hay antes de la primera barra y después de la última no son celdas.
        celdas.RemoveAt(0);
        if (celdas.Count > 0 && string.IsNullOrWhiteSpace(celdas[^1])) celdas.RemoveAt(celdas.Count - 1);

        return celdas.Select(c => c.Normalize(NormalizationForm.FormC).Trim()).ToList();
    }

    private static string NombreDeColumna(string celda) => TextoComparable.Normalizar(LimpiarCelda(celda));

    /// <summary>Quita las marcas de formato Markdown estén donde estén: «**Acme** S.L.» es «Acme S.L.».</summary>
    private static string LimpiarCelda(string celda)
    {
        var sinMarcas = Enfasis().Replace(MarcasFormato().Replace(celda, ""), "$2");
        return sinMarcas.Trim().Trim('*', '_').Trim();
    }

    private static bool EsVacia(string? celda) => celda is null or "" or "-" or "—" or "–";

    private static (FilaLote? Fila, string? Motivo) LeerFila(int numero, List<string> celdas, Columnas columnas, FechaLeida? fecha)
    {
        var empresa = LimpiarCelda(celdas[columnas.Empresa]);
        var puesto = LimpiarCelda(celdas[columnas.Puesto]);
        var textoEstado = LimpiarCelda(Tachado().Replace(celdas[columnas.Estado], ""));
        var textoFecha = LimpiarCelda(celdas[columnas.Fecha]);

        if (EsVacia(empresa) || EsVacia(puesto))
            return (null, "Falta la empresa o el puesto.");
        if (!NombresEmpresa.Normalizar(empresa).Any(char.IsLetterOrDigit))
            return (null, "El nombre de la empresa no es válido.");
        if (fecha is null)
            return (null, $"La fecha «{textoFecha}» no se entiende.");
        if (LeerEstado(textoEstado) is not { } leido)
            return (null, $"El estado «{textoEstado}» no se reconoce.");
        var (estado, deducido) = leido;

        var celdaModalidad = columnas.Modalidad is { } m ? LimpiarCelda(celdas[m]) : null;
        var textoModalidad = EsVacia(celdaModalidad) ? null : celdaModalidad;
        var ubicacion = LeerUbicacion(textoModalidad);
        var enlace = columnas.Enlace is { } e ? LeerEnlace(celdas[e]) : null;

        var demasiadoLargo =
            empresa.Length > LimitesLote.Empresa ? "La empresa" :
            puesto.Length > LimitesLote.Puesto ? "El puesto" :
            textoModalidad?.Length > LimitesLote.ModalidadDetalle ? "La modalidad" :
            ubicacion?.Length > LimitesLote.Ubicacion ? "La ubicación" :
            enlace?.Length > LimitesLote.Enlace ? "El enlace" : null;
        if (demasiadoLargo is not null)
            return (null, $"{demasiadoLargo} es demasiado largo para importarlo.");

        var descartada = estado == EstadoCandidatura.Descartada;
        return (new FilaLote(
            numero,
            fecha.Dia,
            fecha.Aproximada,
            empresa,
            puesto,
            LeerModalidad(textoModalidad),
            textoModalidad,
            ubicacion,
            LeerPortal(textoEstado),
            enlace,
            estado,
            descartada ? MotivoDescarte.Otro : null,
            descartada ? Recortar(textoEstado, LimitesLote.MotivoDescarteTexto) : null,
            // Al descartar, el texto ya va en el motivo: en la nota saldría dos veces en la ficha.
            ComponerNota(descartada ? null : textoEstado, textoFecha, fecha.Aproximada),
            deducido), null);
    }

    private sealed record FechaLeida(DateOnly Dia, bool Aproximada);

    /// <summary>
    /// «(antes del DD-MM)» no dice el año: se toma el de la fila anterior, salvo que así quede más de medio
    /// año antes que ella. Entonces la tabla ha cruzado el cambio de año: tras el 30-12, el 02-01 es del
    /// año siguiente.
    /// </summary>
    private static FechaLeida? LeerFecha(string texto, DateOnly? anterior)
    {
        if (DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia))
            return new FechaLeida(dia, false);

        var aproximada = FechaAproximada().Match(texto);
        if (!aproximada.Success || anterior is not { } referencia) return null;

        var diaDelMes = int.Parse(aproximada.Groups[1].Value, CultureInfo.InvariantCulture);
        var mes = int.Parse(aproximada.Groups[2].Value, CultureInfo.InvariantCulture);
        var fecha = CrearFecha(referencia.Year, mes, diaDelMes);
        if (fecha < referencia.AddMonths(-6))
            fecha = CrearFecha(referencia.Year + 1, mes, diaDelMes);
        return fecha is { } elegida ? new FechaLeida(elegida, true) : null;
    }

    private static DateOnly? CrearFecha(int anio, int mes, int dia) =>
        anio is >= 1 and <= 9999 && mes is >= 1 and <= 12 && dia >= 1 && dia <= DateTime.DaysInMonth(anio, mes)
            ? new DateOnly(anio, mes, dia)
            : null;

    /// <summary>
    /// Primero por cómo empieza («Enviada (LinkedIn)») y, si no empieza por un estado, por las palabras que
    /// contiene. Un estado final escrito después manda sobre el del principio («Oferta caducada», «Enviada;
    /// descartada el 10/11»): es lo último que ha pasado. Si el texto duda, niega o espera, solo vale el
    /// principio, y si la duda es sobre el propio estado («Descartada?»), no se lee. <c>Deducido</c> dice
    /// que el estado sale de una palabra del medio y no del principio.
    /// </summary>
    private static (EstadoCandidatura Estado, bool Deducido)? LeerEstado(string texto)
    {
        var normalizado = TextoComparable.Normalizar(texto);
        var porPrefijo = PrimerEstado(PrefijosEstado, p => normalizado.StartsWith(p, StringComparison.Ordinal));
        if (porPrefijo is not null && EstadoConInterrogacion().IsMatch(normalizado)) return null;

        var palabras = SeparadorPalabras().Split(normalizado).Where(p => p.Length > 0).ToArray();
        if (normalizado.Contains('?') || palabras.Any(PalabrasDeDuda.Contains))
            return porPrefijo is { } soloPrefijo ? (soloPrefijo, false) : null;

        var porPalabra = PrimerEstado(PalabrasEstado, p => AparecePalabraDeEstado(palabras, p));
        if (porPrefijo is not { } inicial)
            return porPalabra is { } deducido ? (deducido, true) : null;
        return porPalabra is { } final && ReglasEstado.EsFinal(final) && !ReglasEstado.EsFinal(inicial)
            ? (final, true)
            : (inicial, false);
    }

    private static bool AparecePalabraDeEstado(string[] palabras, string raiz)
    {
        for (var i = 0; i < palabras.Length; i++)
        {
            if (!palabras[i].StartsWith(raiz, StringComparison.Ordinal)) continue;
            // Mira las dos palabras de antes: «inscripciones cerradas», «plazo ya cerrado».
            var cierreDeLaOferta = raiz == "cerrad"
                && palabras[Math.Max(0, i - 2)..i].Any(CierresQueNoSonDeLaCandidatura.Contains);
            if (!cierreDeLaOferta) return true;
        }
        return false;
    }

    private static EstadoCandidatura? PrimerEstado((string Texto, EstadoCandidatura Estado)[] lista, Func<string, bool> aparece)
    {
        foreach (var (texto, estado) in lista)
        {
            if (aparece(texto)) return estado;
        }
        return null;
    }

    /// <summary>
    /// El orden importa: «no indicada» antes que nada, e «híbrido» (o «parcial», «mixto») antes que
    /// «presencial» o «remoto»: «Teletrabajo parcial» es híbrido, pero «Remoto, jornada parcial» es remoto.
    /// </summary>
    private static Modalidad LeerModalidad(string? texto)
    {
        var normalizado = TextoComparable.Normalizar(texto);
        if (normalizado.Contains("no indicada", StringComparison.Ordinal)) return Modalidad.NoIndicada;
        if (normalizado.Contains("hibrid", StringComparison.Ordinal)
            || Parcial().IsMatch(normalizado)
            || normalizado.Contains("mixt", StringComparison.Ordinal))
            return Modalidad.Hibrido;
        if (normalizado.Contains("presencial", StringComparison.Ordinal)) return Modalidad.Presencial;
        if (normalizado.Contains("remoto", StringComparison.Ordinal) || normalizado.Contains("teletrabajo", StringComparison.Ordinal))
            return Modalidad.Remoto;
        return Modalidad.NoIndicada;
    }

    private static string? LeerUbicacion(string? modalidad)
    {
        if (modalidad is null) return null;
        var coincidencia = UbicacionPresencial().Match(modalidad);
        if (!coincidencia.Success) coincidencia = UbicacionHibrido().Match(modalidad);
        return coincidencia.Success ? coincidencia.Groups[1].Value.Trim() : null;
    }

    private static string? LeerPortal(string estado)
    {
        var parentesis = estado.IndexOf('(');
        if (parentesis < 0) return null;

        var dentro = TextoComparable.Normalizar(estado[(parentesis + 1)..]);
        return Portales.FirstOrDefault(p => dentro.StartsWith(TextoComparable.Normalizar(p), StringComparison.Ordinal));
    }

    private static string? LeerEnlace(string celda)
    {
        if (EsVacia(celda)) return null;

        var marcado = EnlaceMarkdown().Match(celda);
        if (!marcado.Success) marcado = EnlaceAngular().Match(celda);
        return marcado.Success ? marcado.Groups[1].Value.Trim() : celda;
    }

    /// <summary>La nota guarda el texto original del estado si trae algo más que el estado (y no va ya en otro sitio), y lo aproximado de la fecha.</summary>
    private static string? ComponerNota(string? textoEstado, string textoFecha, bool fechaAproximada)
    {
        var partes = new List<string>();
        if (fechaAproximada) partes.Add($"Fecha aproximada: {textoFecha.Trim('(', ')', ' ')}.");
        if (textoEstado is not null && TieneDetalle(textoEstado)) partes.Add(textoEstado);
        return partes.Count == 0 ? null : Recortar(string.Join(" ", partes), LimitesLote.Nota);
    }

    private static bool TieneDetalle(string estado) => estado.TrimEnd('.', ' ').Any(c => !char.IsLetter(c));

    /// <summary>Para los textos que no identifican la candidatura: mejor recortar el detalle que perder la fila.</summary>
    private static string Recortar(string texto, int maximo) =>
        texto.Length <= maximo ? texto : $"{texto[..(maximo - 1)].TrimEnd()}…";

    /// <summary>Posición de cada columna, buscada por nombre.</summary>
    private sealed record Columnas(int Total, int Fecha, int Empresa, int Puesto, int Estado, int? Modalidad, int? Enlace)
    {
        public static Columnas Desde(List<string> cabecera)
        {
            var nombres = cabecera.Select(NombreDeColumna).ToList();
            int? Opcional(string nombre) => nombres.IndexOf(nombre) is >= 0 and var i ? i : null;

            return new Columnas(cabecera.Count, nombres.IndexOf("fecha"), nombres.IndexOf("empresa"),
                nombres.IndexOf("puesto"), nombres.IndexOf("estado"), Opcional("modalidad"), Opcional("enlace"));
        }
    }
}
