using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Curriculum;

namespace RadarEmpleo.Services.Rastreos;

/// <summary>Una oferta leída de una línea del informe.</summary>
/// <param name="Linea">Línea del archivo, empezando en 1.</param>
/// <param name="TextoOriginal">La línea tal cual, con su Markdown.</param>
/// <param name="Enlace">El primer enlace web de la línea, ya normalizado (<see cref="Enlaces.Normalizar"/>).</param>
/// <param name="Ignorada">La línea dice que Eduardo la descartó: se crea ya como ignorada.</param>
public sealed record OfertaLeida(
    int Linea,
    string TextoOriginal,
    string Empresa,
    string Puesto,
    string Enlace,
    Encaje Encaje,
    string? Pega,
    bool Ignorada);

/// <summary>Un informe de rastreo leído: una sección «## Rastreo …» del registro o un <c>rastreos\rastreo_*.md</c>.</summary>
/// <param name="Clave">Lo identifica entre escaneos (<see cref="Rastreo.Clave"/>).</param>
/// <param name="Texto">El Markdown del informe sin su título.</param>
/// <param name="Hash">SHA-256 en hexadecimal de <paramref name="Texto"/>.</param>
/// <param name="LineasSinLeer">Líneas que parecen de una oferta pero no se han podido leer (sin enlace, sin
/// «Empresa — Puesto» en negrita, «Plan B», «Bajo:»…). Quedan para los agentes de la F7.</param>
/// <param name="ClaveAnterior">Solo en las secciones del registro: la clave que llevaban antes de incluir la
/// fecha («registro:título»), para reconocer los rastreos ya guardados con ella.</param>
public sealed record RastreoLeido(
    string Clave,
    OrigenRastreo Origen,
    string Titulo,
    DateOnly Fecha,
    string RutaInforme,
    string Texto,
    string Hash,
    IReadOnlyList<OfertaLeida> Ofertas,
    int LineasSinLeer,
    string? ClaveAnterior = null);

/// <summary>Lo que sale de analizar un archivo: los rastreos y los avisos de lo que no se ha podido leer.</summary>
public sealed record AnalisisRastreos(IReadOnlyList<RastreoLeido> Rastreos, IReadOnlyList<string> Avisos);

/// <summary>Qué es una línea de un informe.</summary>
public enum TipoLineaRastreo
{
    /// <summary>Texto que no es una oferta (una nota, una lista de descartadas, un «ENVIADA»…).</summary>
    Otra,
    Oferta,
    /// <summary>Parece una oferta, pero le falta algo para leerla sin adivinar.</summary>
    SinLeer
}

/// <summary>Resultado de leer una línea; <paramref name="Oferta"/> solo si es una oferta.</summary>
public sealed record LineaRastreo(TipoLineaRastreo Tipo, OfertaLeida? Oferta = null);

/// <summary>
/// Lee los informes de rastreo de forma determinista (función pura: ni disco ni BD ni IA). Una oferta solo
/// sale de una línea con viñeta (<c>-</c>, <c>*</c> o <c>1.</c>), una negrita «Empresa — Puesto» (con la
/// raya «—»; el guion «–» puede ir dentro del puesto) y al menos un enlace. Lo demás no se adivina.
/// Los formatos son los que usa el equipo /empleo:
/// <list type="bullet">
/// <item>Desde el 03/11: <c>- **Alto · Empresa — Puesto** (detalles)… Pega: … https://…</c>.</item>
/// <item>Antes: <c>1. **Empresa — Puesto**, … Pegas: … https://…</c> o <c>4. Tiro largo: **Empresa — Puesto** …</c>.</item>
/// </list>
/// </summary>
public static partial class AnalizadorRastreo
{
    /// <summary>Prefijo de la clave de los rastreos que salen de una sección del registro.</summary>
    public const string PrefijoClaveRegistro = "registro:";

    private const string MarcaEnvio = "ENVIAD";
    private const char Raya = '—';
    private const char PuntoMedio = '·';

    [GeneratedRegex(@"\r\n|\r|\n")]
    private static partial Regex SaltoDeLinea();

    [GeneratedRegex(@"^##(?!#)\s*(.*?)[\s#]*$")]
    private static partial Regex TituloSeccion();

    [GeneratedRegex(@"^#(?!#)\s*(.*?)[\s#]*$")]
    private static partial Regex TituloPrincipal();

    // Una sección termina en el siguiente encabezado de nivel 1 o 2; los «###» son parte de ella.
    [GeneratedRegex(@"^#{1,2}(?!#)\s")]
    private static partial Regex Encabezado();

    [GeneratedRegex(@"^rastreo\b", RegexOptions.CultureInvariant)]
    private static partial Regex EmpiezaPorRastreo();

    [GeneratedRegex(@"(?<!\d)(\d{4})-(\d{1,2})-(\d{1,2})(?!\d)")]
    private static partial Regex FechaIso();

    [GeneratedRegex(@"(?<![\d/])(\d{1,2})/(\d{1,2})(?:/(\d{4}|\d{2}))?(?![\d/])")]
    private static partial Regex FechaCorta();

    [GeneratedRegex(@"^rastreo_(\d{4}-\d{2}-\d{2})\.md$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NombreInforme();

    [GeneratedRegex(@"^\s*(?:[-*+]|\d{1,3}[.)])\s+(.+)$")]
    private static partial Regex Vineta();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Negrita();

    [GeneratedRegex(@"https?://[^\s<>()\[\]""'`]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnlaceWeb();

    // La pega va hasta el siguiente enlace o hasta el final de la línea.
    [GeneratedRegex(@"\bPegas?\s*:\s*(.+?)\s*(?=https?://|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pega();

    // Sobre el texto ya pasado por TextoComparable: sin tildes y en minúsculas.
    [GeneratedRegex(@"^(medio[\s-]+bajo|medio[\s-]+alto|medio|alto|bajo|tiro\s+largo)\b", RegexOptions.CultureInvariant)]
    private static partial Regex EtiquetaEncaje();

    // «· Encaje Alto.» o «· Tiro largo.» detrás del enlace (rastreo del 30/10 por la tarde).
    [GeneratedRegex(@"\bencaje\s+(medio[\s-]+bajo|medio[\s-]+alto|medio|alto|bajo|tiro\s+largo)\b|·\s*(tiro\s+largo)\b", RegexOptions.CultureInvariant)]
    private static partial Regex EncajeDetras();

    [GeneratedRegex(@"\*\*|__|~~|`")]
    private static partial Regex MarcasFormato();

    [GeneratedRegex(@"^\d{1,2}/\d{1,2}(?:/\d{2,4})?\s+")]
    private static partial Regex FechaDelante();

    [GeneratedRegex(@"\[([^\]]*)\]\(([^)\s]+)\)")]
    private static partial Regex EnlaceMarkdown();

    private static readonly char[] FinalesDeEnlace = ['.', ',', ';', ':', '!', '?', '»', '”', '’', '*', '_'];

    // ---------------------------------------------------------------- archivos

    /// <summary>
    /// Las secciones «## Rastreo …» del registro, en el orden del archivo. La fecha sale del título
    /// (<see cref="FechaDeTitulo"/>); una sección sin fecha legible no se lee y se avisa. La clave lleva la
    /// fecha y el título (<see cref="ClaveRegistro"/>); dos secciones con los dos iguales se distinguen con
    /// «#2», «#3»…
    /// </summary>
    /// <param name="referencia">Día (de Madrid) desde el que se calcula el año de las fechas «DD/MM»: el de
    /// la última modificación del registro, que no cambia por leerlo otro día.</param>
    public static AnalisisRastreos AnalizarRegistro(string texto, DateOnly referencia)
    {
        var lineas = SaltoDeLinea().Split(texto.TrimStart('﻿'));
        var rastreos = new List<RastreoLeido>();
        var avisos = new List<string>();
        var claves = new HashSet<string>(StringComparer.Ordinal);
        var clavesAnteriores = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < lineas.Length; i++)
        {
            var encabezado = TituloSeccion().Match(lineas[i]);
            if (!encabezado.Success) continue;
            var titulo = Recortar(encabezado.Groups[1].Value.Trim(), Rastreo.LargoTitulo);
            if (!EmpiezaPorRastreo().IsMatch(TextoComparable.Normalizar(titulo))) continue;

            var fin = i + 1;
            while (fin < lineas.Length && !Encabezado().IsMatch(lineas[fin])) fin++;

            if (FechaDeTitulo(titulo, referencia) is { } fecha)
            {
                // Con la fecha: una sección del año siguiente con el mismo título es otro rastreo.
                var normalizado = TextoComparable.Normalizar(titulo);
                var clave = ClaveUnica(ClaveRegistro(fecha, normalizado), claves);
                var anterior = ClaveUnica(PrefijoClaveRegistro + normalizado, clavesAnteriores);
                rastreos.Add(Leer(clave, OrigenRastreo.Registro, titulo, fecha, RutasCurriculum.ArchivoRegistro,
                    lineas, i + 1, fin) with { ClaveAnterior = anterior });
            }
            else
            {
                avisos.Add($"No entiendo la fecha del rastreo «{titulo}»: no lo he leído.");
            }
            i = fin - 1;
        }
        return new AnalisisRastreos(rastreos, avisos);
    }

    /// <summary>
    /// Un informe <c>rastreos\rastreo_AAAA-MM-DD.md</c> entero: la fecha sale del nombre del archivo y el
    /// título, del primer «# » (si no hay, «Rastreo del DD/MM»). La clave es la propia ruta.
    /// </summary>
    /// <param name="rutaRelativa">Ruta dentro de Curriculum con «/» («rastreos/rastreo_2026-11-09.md»).</param>
    public static AnalisisRastreos AnalizarInforme(string rutaRelativa, string texto)
    {
        var nombre = rutaRelativa[(rutaRelativa.LastIndexOf('/') + 1)..];
        var porNombre = NombreInforme().Match(nombre);
        if (!porNombre.Success || !DateOnly.TryParseExact(porNombre.Groups[1].Value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            return new AnalisisRastreos([], [$"No entiendo la fecha del informe «{nombre}»: no lo he leído."]);

        var lineas = SaltoDeLinea().Split(texto.TrimStart('﻿'));
        var primera = Array.FindIndex(lineas, l => l.Trim().Length > 0);
        var principal = primera >= 0 ? TituloPrincipal().Match(lineas[primera]) : Match.Empty;
        var titulo = principal.Success && principal.Groups[1].Value.Trim().Length > 0
            ? Recortar(principal.Groups[1].Value.Trim(), Rastreo.LargoTitulo)
            : $"Rastreo del {fecha:dd/MM}";
        var desde = principal.Success ? primera + 1 : 0;

        var rastreo = Leer(Recortar(rutaRelativa, Rastreo.LargoClave), OrigenRastreo.TareaLunes, titulo, fecha,
            rutaRelativa, lineas, desde, lineas.Length);
        return new AnalisisRastreos([rastreo], []);
    }

    /// <summary>Clave de una sección del registro: «registro:AAAA-MM-DD:título normalizado».</summary>
    public static string ClaveRegistro(DateOnly fecha, string tituloNormalizado) =>
        $"{PrefijoClaveRegistro}{fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{tituloNormalizado}";

    // ---------------------------------------------------------------- fechas

    /// <summary>
    /// Fecha del título de un rastreo: la primera «AAAA-MM-DD», o la primera «DD/MM» (o «DD/MM/AAAA»). Sin
    /// año se aplica <see cref="FechaConAnio"/>. Null si no hay ninguna fecha válida.
    /// </summary>
    public static DateOnly? FechaDeTitulo(string titulo, DateOnly hoy)
    {
        var iso = FechaIso().Match(titulo);
        if (iso.Success)
            return CrearFecha(Numero(iso.Groups[1]), Numero(iso.Groups[2]), Numero(iso.Groups[3]));

        var corta = FechaCorta().Match(titulo);
        if (!corta.Success) return null;

        var dia = Numero(corta.Groups[1]);
        var mes = Numero(corta.Groups[2]);
        if (!corta.Groups[3].Success) return FechaConAnio(dia, mes, hoy);

        var anio = Numero(corta.Groups[3]);
        return CrearFecha(anio < 100 ? 2000 + anio : anio, mes, dia);
    }

    /// <summary>
    /// Año de una fecha «DD/MM»: el de hoy, salvo que así quede más de un día en el futuro; entonces es del
    /// año anterior (el 28/12 leído el 02/01 es del año pasado). Null si el día no existe.
    /// </summary>
    public static DateOnly? FechaConAnio(int dia, int mes, DateOnly hoy)
    {
        var esteAnio = CrearFecha(hoy.Year, mes, dia);
        if (esteAnio is { } fecha && fecha <= hoy.AddDays(1)) return fecha;
        return CrearFecha(hoy.Year - 1, mes, dia);
    }

    private static DateOnly? CrearFecha(int anio, int mes, int dia) =>
        anio is >= 1 and <= 9999 && mes is >= 1 and <= 12 && dia >= 1 && dia <= DateTime.DaysInMonth(anio, mes)
            ? new DateOnly(anio, mes, dia)
            : null;

    private static int Numero(Group grupo) => int.Parse(grupo.Value, CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- líneas

    /// <summary>
    /// Lee una línea de un informe. Es una oferta si tiene viñeta, una negrita «[Encaje ·] Empresa — Puesto»
    /// y un enlace web (vale el primero). El encaje sale de lo que va antes del «·» dentro de la negrita, de
    /// lo que va antes de la negrita («Tiro largo:») o de un «Encaje Alto» detrás; «Descartada…» la marca
    /// como ignorada. Las líneas con «ENVIADA» que no son una oferta se saltan sin contarlas. Es «sin leer»
    /// si parece una oferta (tiene enlace, la raya «—» o una negrita de encaje como «**Bajo:**») y no se
    /// puede leer entera.
    /// </summary>
    public static LineaRastreo LeerLinea(string linea, int numero)
    {
        var vineta = Vineta().Match(linea);
        if (!vineta.Success) return new LineaRastreo(TipoLineaRastreo.Otra);

        var resto = vineta.Groups[1].Value.Trim();
        var enlace = PrimerEnlace(resto);
        var hayEnvio = resto.Contains(MarcaEnvio, StringComparison.Ordinal);
        var negrita = Negrita().Match(resto);

        if (negrita.Success && negrita.Groups[1].Value.IndexOf(Raya) is > 0 and var raya)
        {
            var dentro = negrita.Groups[1].Value;
            var antes = dentro[..raya];
            var punto = antes.LastIndexOf(PuntoMedio);
            var etiqueta = punto >= 0 ? antes[..punto] : null;
            // «**21/10 NEWLINE — …**»: la fecha del apunte no es parte del nombre.
            var empresa = FechaDelante().Replace(Limpiar(punto >= 0 ? antes[(punto + 1)..] : antes), "");
            // El puesto va entero, con lo que lleve detrás en la negrita («…, Tomelloso»): cortarlo en la
            // primera coma rompería «Python, .NET, Typescript developer».
            var puesto = Limpiar(dentro[(raya + 1)..]).TrimEnd('.').Trim();
            var prefijo = resto[..negrita.Index].Trim().TrimEnd(':').Trim();

            // «**ENVIADA el 03/11: Eurostaff — Operario/a…**» cuenta un envío, no es una oferta.
            if ($"{etiqueta} {empresa} {prefijo}".Contains(MarcaEnvio, StringComparison.Ordinal))
                return new LineaRastreo(TipoLineaRastreo.Otra);
            if (!EsValida(empresa, puesto)) return new LineaRastreo(TipoLineaRastreo.SinLeer);
            if (enlace is null) return new LineaRastreo(hayEnvio ? TipoLineaRastreo.Otra : TipoLineaRastreo.SinLeer);

            var ignorada = EsDescarte(etiqueta) || EsDescarte(prefijo);
            var detras = resto[(negrita.Index + negrita.Length)..];
            var encaje = ignorada
                ? Encaje.SinValorar
                : EncajeDeEtiqueta(etiqueta) ?? EncajeDeEtiqueta(prefijo) ?? EncajeEnElTexto(detras) ?? Encaje.SinValorar;

            return new LineaRastreo(TipoLineaRastreo.Oferta,
                new OfertaLeida(numero, linea.Trim(), empresa, puesto, enlace, encaje, LeerPega(detras), ignorada));
        }

        if (hayEnvio) return new LineaRastreo(TipoLineaRastreo.Otra);
        var negritaDeEncaje = negrita.Success && EncajeDeEtiqueta(negrita.Groups[1].Value) is not null;
        return enlace is not null || resto.Contains(Raya) || negritaDeEncaje
            ? new LineaRastreo(TipoLineaRastreo.SinLeer)
            : new LineaRastreo(TipoLineaRastreo.Otra);
    }

    /// <summary>
    /// Texto de una línea sin la viñeta ni las marcas de Markdown («[texto](url)» queda como «texto (url)»).
    /// Es lo que va al análisis de la candidatura al pasarla al tablero.
    /// </summary>
    public static string TextoPlano(string linea)
    {
        var vineta = Vineta().Match(linea);
        var texto = vineta.Success ? vineta.Groups[1].Value : linea;
        texto = EnlaceMarkdown().Replace(texto, "$1 ($2)");
        return MarcasFormato().Replace(texto, "").Trim();
    }

    /// <summary>
    /// Encaje de una etiqueta como «Alto», «Medio-Alto», «Medio-Bajo», «Bajo» o «Tiro largo» (y lo que siga:
    /// «Alto (sin verificar en origen)»). Medio-Alto es Medio; Medio-Bajo, Bajo y Tiro largo son TiroLargo.
    /// Null si no es una etiqueta de encaje.
    /// </summary>
    public static Encaje? EncajeDeEtiqueta(string? etiqueta)
    {
        var texto = TextoComparable.Normalizar(etiqueta).TrimEnd(':', ' ');
        var coincidencia = EtiquetaEncaje().Match(texto);
        if (coincidencia.Success) return EncajeDePalabra(coincidencia.Groups[1].Value);
        // «Única recomendable (tiro largo)»
        return texto.Contains("tiro largo", StringComparison.Ordinal) ? Encaje.TiroLargo : null;
    }

    private static Encaje? EncajeEnElTexto(string texto)
    {
        var coincidencia = EncajeDetras().Match(TextoComparable.Normalizar(texto));
        if (!coincidencia.Success) return null;
        var palabra = coincidencia.Groups[1].Success ? coincidencia.Groups[1].Value : coincidencia.Groups[2].Value;
        return EncajeDePalabra(palabra);
    }

    private static Encaje EncajeDePalabra(string palabra)
    {
        var texto = string.Join(' ', palabra.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries));
        return texto switch
        {
            "alto" => Encaje.Alto,
            "medio" or "medio alto" => Encaje.Medio,
            _ => Encaje.TiroLargo // medio bajo, bajo y tiro largo
        };
    }

    private static bool EsDescarte(string? etiqueta) =>
        TextoComparable.Normalizar(etiqueta).StartsWith("descartad", StringComparison.Ordinal);

    private static string? PrimerEnlace(string texto)
    {
        foreach (Match coincidencia in EnlaceWeb().Matches(texto))
        {
            var enlace = coincidencia.Value.TrimEnd(FinalesDeEnlace);
            if (enlace.Length > OfertaRastreo.LargoEnlace) continue;
            if (Enlaces.Normalizar(enlace) is { Length: > 0 } normalizado) return normalizado;
        }
        return null;
    }

    private static string? LeerPega(string texto)
    {
        var coincidencia = Pega().Match(texto);
        if (!coincidencia.Success) return null;
        var pega = MarcasFormato().Replace(coincidencia.Groups[1].Value, "").Trim().TrimEnd('—', '·', '-', '(', ' ').Trim();
        return pega.Length == 0 ? null : Recortar(pega, OfertaRastreo.LargoPega);
    }

    private static bool EsValida(string empresa, string puesto) =>
        NombresEmpresa.Normalizar(empresa).Any(char.IsLetterOrDigit)
        && puesto.Any(char.IsLetterOrDigit)
        && empresa.Length <= OfertaRastreo.LargoEmpresa
        && puesto.Length <= OfertaRastreo.LargoPuesto;

    // El punto final no se quita aquí: es parte de «S.L.» o «S.A.».
    private static string Limpiar(string texto) =>
        MarcasFormato().Replace(texto, "").Normalize(NormalizationForm.FormC).Trim().TrimEnd(',', ';', ':').Trim();

    // ---------------------------------------------------------------- auxiliares

    private static RastreoLeido Leer(string clave, OrigenRastreo origen, string titulo, DateOnly fecha, string ruta,
        string[] lineas, int desde, int hasta)
    {
        var ofertas = new List<OfertaLeida>();
        var sinLeer = 0;
        for (var i = desde; i < hasta; i++)
        {
            var leida = LeerLinea(lineas[i], i + 1);
            if (leida.Oferta is { } oferta) ofertas.Add(oferta);
            else if (leida.Tipo == TipoLineaRastreo.SinLeer) sinLeer++;
        }

        // Sin las líneas en blanco del principio y del final: son las que separan una sección de otra.
        while (desde < hasta && lineas[desde].Trim().Length == 0) desde++;
        while (hasta > desde && lineas[hasta - 1].Trim().Length == 0) hasta--;
        var texto = string.Join("\n", lineas[desde..hasta]);

        return new RastreoLeido(clave, origen, titulo, fecha, ruta, texto, Hash(texto), ofertas, sinLeer);
    }

    private static string ClaveUnica(string clave, HashSet<string> usadas)
    {
        var base_ = Recortar(clave, Rastreo.LargoClave - 4);
        var candidata = base_;
        for (var n = 2; !usadas.Add(candidata); n++) candidata = $"{base_}#{n}";
        return candidata;
    }

    private static string Hash(string texto) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    private static string Recortar(string texto, int maximo) =>
        texto.Length <= maximo ? texto : $"{texto[..(maximo - 1)].TrimEnd()}…";
}
