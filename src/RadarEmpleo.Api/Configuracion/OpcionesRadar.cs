namespace RadarEmpleo.Api.Configuracion;

/// <summary>Cómo se ejecuta la app: en el PC de Eduardo (Local) o como demo pública con datos inventados (Demo).</summary>
public enum ModoEjecucion
{
    Local,
    Demo
}

/// <summary>Sección <c>RadarEmpleo</c> de appsettings.</summary>
public sealed class OpcionesRadar
{
    public const string Seccion = "RadarEmpleo";

    public ModoEjecucion Modo { get; set; } = ModoEjecucion.Local;

    /// <summary>
    /// Ruta del archivo SQLite. Admite variables de entorno (<c>%LOCALAPPDATA%</c>). Si está vacía se usa
    /// <c>%LOCALAPPDATA%\RadarEmpleo\radar.db</c> (en Linux, <c>~/.local/share/RadarEmpleo/radar.db</c>).
    /// </summary>
    public string? RutaBaseDatos { get; set; }

    /// <summary>
    /// Carpeta Curriculum. Admite variables de entorno. La app lee de ella el registro y solo escribe
    /// <c>radar\candidaturas.md</c>. Si está vacía, la importación avisa y el radar no se genera.
    /// </summary>
    public string? CarpetaCurriculum { get; set; }

    /// <summary>
    /// Comprueba al arrancar que la ruta de la BD es solo una ruta: con «;», «=» o comillas podría llevar otra cadena de
    /// conexión dentro (<c>x.db;Data Source=…\radar.db</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">La ruta tiene alguno de esos caracteres.</exception>
    public void ExigirRutaBaseDatosValida()
    {
        foreach (var ruta in new[] { RutaBaseDatos ?? "", RutaBaseDatosEfectiva() })
            if (ruta.IndexOfAny([';', '=', '"', '\'']) >= 0)
                throw new InvalidOperationException("RadarEmpleo:RutaBaseDatos no puede llevar «;», «=» ni comillas: tiene que ser solo la ruta del archivo.");
    }

    /// <summary>Ruta final del archivo de la base de datos, con las variables de entorno expandidas.</summary>
    public string RutaBaseDatosEfectiva()
    {
        if (!string.IsNullOrWhiteSpace(RutaBaseDatos))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(RutaBaseDatos));

        var datosLocales = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(datosLocales, "RadarEmpleo", "radar.db");
    }
}

/// <summary>Qué lanza las ejecuciones de los agentes.</summary>
public enum TipoEjecutor
{
    /// <summary>Claude Code de verdad (<c>claude.exe</c>).</summary>
    Cli,
    /// <summary>Respuestas inventadas, sin Claude ni gasto: solo en modo Local y con una BD que no sea la de por defecto.</summary>
    Falso
}

/// <summary>
/// Sección <c>Agentes</c> de appsettings (llegan en la F7). Apagados por defecto: solo se encienden en el PC, y sus
/// endpoints solo responden por el endpoint de Kestrel «Local».
/// </summary>
public sealed class OpcionesAgentes
{
    public const string Seccion = "Agentes";

    /// <summary>Versión de Claude Code con la que se comprobaron los argumentos (paso 0 de la F7).</summary>
    public const string VersionComprobada = "2.1.185";

    public bool Habilitado { get; set; }

    /// <summary>
    /// Ruta de <c>claude.exe</c>. Admite variables de entorno. Vacía: el que instala npm,
    /// <c>%APPDATA%\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe</c> (sin pasar por <c>claude.cmd</c>).
    /// </summary>
    public string? RutaClaude { get; set; }

    /// <summary>Si la instalada es otra, Ajustes lo dice (pero no se bloquea).</summary>
    public string VersionProbada { get; set; } = VersionComprobada;

    /// <summary>Tope de gasto de un día de Madrid. Se cambiará desde Ajustes en la F11.</summary>
    public decimal TopeGastoDiarioUsd { get; set; } = 15m;

    public TipoEjecutor Ejecutor { get; set; } = TipoEjecutor.Cli;

    /// <summary>Van antes de los argumentos de la tarea. Solo para los tests (<c>ClaudeSimulado.dll</c>).</summary>
    public List<string> ArgumentosPrevios { get; set; } = [];

    /// <summary>
    /// Carpeta de <c>logs\</c> y <c>ejecuciones\</c> (con datos personales; nunca se sirven). Vacía: la de la base de
    /// datos (<c>%LOCALAPPDATA%\RadarEmpleo</c> con la BD por defecto).
    /// </summary>
    public string? CarpetaDatos { get; set; }

    /// <summary>Respuestas del ejecutor falso. Vacía: <c>datos-ejemplo\agentes</c> junto a la API.</summary>
    public string? CarpetaRespuestasFalso { get; set; }

    /// <summary>
    /// El <c>--model</c> de ExtraerRegistro («sonnet», «claude-haiku-4-5»…). Vacío: el de su definición («haiku»). Se
    /// comprueba al arrancar (<see cref="ConfiguracionAgentes.ExigirModeloValido"/>).
    /// </summary>
    public string? ModeloExtraerRegistro { get; set; }

    /// <summary>Ruta final de <c>claude.exe</c>, con las variables de entorno expandidas.</summary>
    public string RutaClaudeEfectiva()
    {
        if (!string.IsNullOrWhiteSpace(RutaClaude))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(RutaClaude));
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "npm", "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe");
    }
}
