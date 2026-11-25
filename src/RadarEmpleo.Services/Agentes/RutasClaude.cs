namespace RadarEmpleo.Services.Agentes;

/// <summary>Dónde está <c>claude.exe</c>. Lo comparten la API y el evaluador del encaje.</summary>
public static class RutasClaude
{
    /// <summary>
    /// El que instala npm: <c>%APPDATA%\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe</c> (sin pasar por
    /// <c>claude.cmd</c>).
    /// </summary>
    public static string PorDefecto() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe");

    /// <summary>La ruta configurada, con las variables de entorno expandidas; vacía, <see cref="PorDefecto"/>.</summary>
    public static string Efectiva(string? configurada) =>
        string.IsNullOrWhiteSpace(configurada) ? PorDefecto() : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configurada));
}
