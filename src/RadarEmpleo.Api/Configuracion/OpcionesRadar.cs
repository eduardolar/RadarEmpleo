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

    /// <summary>Ruta final del archivo de la base de datos, con las variables de entorno expandidas.</summary>
    public string RutaBaseDatosEfectiva()
    {
        if (!string.IsNullOrWhiteSpace(RutaBaseDatos))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(RutaBaseDatos));

        var datosLocales = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(datosLocales, "RadarEmpleo", "radar.db");
    }
}

/// <summary>Sección <c>Agentes</c> de appsettings. Los agentes llegan en la F6; hasta entonces, apagados.</summary>
public sealed class OpcionesAgentes
{
    public const string Seccion = "Agentes";

    public bool Habilitado { get; set; }
}
