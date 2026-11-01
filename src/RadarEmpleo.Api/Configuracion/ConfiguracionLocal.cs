using Microsoft.Extensions.Configuration.Json;

namespace RadarEmpleo.Api.Configuracion;

/// <summary>
/// Carga de <c>appsettings.Local.json</c> (opcional y fuera de git: rutas personales y, en la F7, el
/// token del móvil) en el sitio que le toca.
/// </summary>
public static class ConfiguracionLocal
{
    public const string Archivo = "appsettings.Local.json";

    /// <summary>
    /// Inserta <see cref="Archivo"/> justo después del último archivo JSON ya registrado
    /// (appsettings.json, appsettings.{Entorno}.json y secretos de usuario) y, por tanto, ANTES de
    /// las variables de entorno y los argumentos de línea de comandos de la aplicación: así pisa a
    /// appsettings.json, pero <c>--RadarEmpleo:RutaBaseDatos=…</c> o una variable de entorno siempre
    /// mandan sobre él. Si no hay ningún JSON todavía, se pone el primero.
    /// </summary>
    public static void Anadir(IConfigurationBuilder configuracion)
    {
        var fuente = new JsonConfigurationSource
        {
            Path = Archivo,
            Optional = true,
            ReloadOnChange = true
        };

        var fuentes = configuracion.Sources;
        var ultimoJson = -1;
        for (var i = 0; i < fuentes.Count; i++)
        {
            if (fuentes[i] is FileConfigurationSource) ultimoJson = i;
        }

        fuentes.Insert(ultimoJson + 1, fuente);
    }
}
