using RadarEmpleo.Services;
using RadarEmpleo.Services.Agentes;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Ofertas;

namespace RadarEmpleo.Api.Configuracion;

/// <summary>Monta lo que los agentes necesitan de la configuración y comprueba que tiene sentido al arrancar.</summary>
public static class ConfiguracionAgentes
{
    /// <summary>Las opciones del ejecutor a partir de las de la API (Services no referencia a Api).</summary>
    public static OpcionesEjecutor Opciones(OpcionesAgentes agentes, OpcionesRadar radar)
    {
        var carpetaDatos = string.IsNullOrWhiteSpace(agentes.CarpetaDatos)
            ? Path.GetDirectoryName(radar.RutaBaseDatosEfectiva())!
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(agentes.CarpetaDatos));
        return new OpcionesEjecutor(agentes.RutaClaudeEfectiva(), agentes.ArgumentosPrevios, carpetaDatos,
            agentes.TopeGastoDiarioUsd, agentes.VersionProbada)
        {
            RutaExclusion = radar.RutaBaseDatosEfectiva()
        };
    }

    /// <summary>
    /// El ejecutor configurado. El falso solo se admite en modo Local y con una base de datos que no sea la de por
    /// defecto: nunca debe dejar ejecuciones inventadas en la BD de Eduardo.
    /// </summary>
    /// <exception cref="InvalidOperationException">Ejecutor falso con la BD por defecto o en modo Demo.</exception>
    public static IEjecutorClaude Ejecutor(IServiceProvider servicios, OpcionesAgentes agentes, OpcionesRadar radar)
    {
        if (agentes.Ejecutor != TipoEjecutor.Falso)
            return ActivatorUtilities.CreateInstance<EjecutorClaudeCli>(servicios);

        ExigirFalsoPermitido(agentes, radar);
        var falso = new EjecutorClaudeFalso(servicios.GetRequiredService<IClock>());
        falso.CargarDe(CarpetaRespuestasFalso(agentes));
        return falso;
    }

    /// <summary>
    /// El descargador de ofertas: con el ejecutor falso, también el falso (decisión 9 de la F7b: la demo y las pruebas no
    /// salen a internet), con <c>descargas.json</c> de la misma carpeta que las respuestas del ejecutor.
    /// </summary>
    public static IDescargadorOfertas Descargador(OpcionesAgentes agentes) =>
        agentes.Ejecutor == TipoEjecutor.Falso
            ? new DescargadorOfertasFalso().CargarDe(CarpetaRespuestasFalso(agentes))
            : new DescargadorOfertasHttp(new ConectorSeguro(new ResolvedorDnsSistema()));

    private static string CarpetaRespuestasFalso(OpcionesAgentes agentes) =>
        string.IsNullOrWhiteSpace(agentes.CarpetaRespuestasFalso)
            ? Path.Combine(AppContext.BaseDirectory, "datos-ejemplo", "agentes")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(agentes.CarpetaRespuestasFalso));

    /// <summary>
    /// Comprueba la configuración de los agentes al arrancar, antes de abrir la BD: el falso, solo en modo Local y con la
    /// BD bajo la carpeta temporal del sistema (<see cref="EstaBajoTemporal"/>); <c>ArgumentosPrevios</c>, solo con el
    /// falso (es para los tests: con el Cli dejaría meter argumentos delante de los de la tarea).
    /// </summary>
    /// <exception cref="InvalidOperationException">La configuración no se admite.</exception>
    public static void ExigirFalsoPermitido(OpcionesAgentes agentes, OpcionesRadar radar)
    {
        if (agentes.Ejecutor != TipoEjecutor.Falso)
        {
            if (agentes.ArgumentosPrevios.Count > 0)
                throw new InvalidOperationException("Agentes:ArgumentosPrevios solo se admite en los tests, nunca con Agentes:Ejecutor=Cli.");
            return;
        }
        if (radar.Modo != ModoEjecucion.Local)
            throw new InvalidOperationException("Agentes:Ejecutor=Falso solo se admite en modo Local.");
        var ruta = radar.RutaBaseDatosEfectiva();
        if (!EstaBajoTemporal(ruta, Path.GetTempPath()) || CarpetasDatosPosibles().Any(carpeta => EsLaDeVerdad(ruta, carpeta)))
            throw new InvalidOperationException("Agentes:Ejecutor=Falso solo se admite con una base de datos temporal (bajo %TEMP%): usa --RadarEmpleo:RutaBaseDatos.");
    }

    /// <summary>
    /// <c>…\AppData\Local\RadarEmpleo</c> resuelta por Windows (<see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>,
    /// que no lee las variables de entorno): <c>%TEMP%</c> sí se puede cambiar y no basta por sí solo.
    /// </summary>
    public static string CarpetaDatosPorDefecto() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "RadarEmpleo");

    /// <summary>
    /// Las carpetas que pueden ser la de datos de Eduardo: la de <see cref="CarpetaDatosPorDefecto"/> y, en Windows, la que
    /// sale del perfil registrado para el usuario actual (<c>HKLM\…\ProfileList\&lt;SID&gt;\ProfileImagePath</c>).
    /// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> expande <c>%USERPROFILE%</c> del proceso, que quien
    /// arranca la API puede cambiar; el registro no.
    /// </summary>
    public static IReadOnlyList<string> CarpetasDatosPosibles()
    {
        var carpetas = new List<string> { CarpetaDatosPorDefecto() };
        if (OperatingSystem.IsWindows() && PerfilRegistrado() is { Length: > 0 } perfil)
            carpetas.Add(Path.Combine(perfil, "AppData", "Local", "RadarEmpleo"));
        return carpetas;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? PerfilRegistrado()
    {
        try
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            if (sid is null) return null;
            using var clave = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
            // ProfileImagePath es REG_EXPAND_SZ con %SystemDrive%: se expande con la variable del sistema, no la del perfil.
            return clave?.GetValue("ProfileImagePath", null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) is string valor
                ? valor.Replace("%SystemDrive%", Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                : null;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// ¿Puede ser la BD de Eduardo? Sí si está dentro de <paramref name="carpetaDatos"/> por el texto o por identidad de
    /// archivo (la carpeta o cualquiera de sus padres es el mismo objeto del disco aunque se llegue por un nombre 8.3, una
    /// junction o un enlace), si es el mismo archivo que la BD por defecto, o si tiene más de un enlace duro.
    /// </summary>
    public static bool EsLaDeVerdad(string rutaBaseDatos, string carpetaDatos)
    {
        var ruta = Path.GetFullPath(rutaBaseDatos);
        var carpeta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpetaDatos));
        if (ruta.StartsWith(carpeta + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;

        if (IdentidadArchivo.Leer(ruta) is { } archivo)
        {
            if (archivo.Enlaces > 1) return true;
            if (IdentidadArchivo.Leer(Path.Combine(carpeta, "radar.db")) is { } real && archivo.MismoQue(real)) return true;
        }
        if (IdentidadArchivo.Leer(carpeta) is not { } datos) return false;
        for (var padre = Path.GetDirectoryName(ruta); !string.IsNullOrEmpty(padre); padre = Path.GetDirectoryName(padre))
            if (IdentidadArchivo.Leer(padre) is { } identidad && identidad.MismoQue(datos)) return true;
        return false;
    }

    /// <summary>
    /// ¿Está la BD de verdad dentro de la carpeta temporal? No basta con comparar texto: se rechazan las rutas con
    /// <c>\?\</c> o <c>\.\</c>, las de red (<c>\localhost\C$\…</c>), los flujos alternativos (<c>::$DATA</c>) y
    /// cualquier enlace o junction entre la carpeta temporal y el archivo, que podrían llevar a la BD real.
    /// </summary>
    public static bool EstaBajoTemporal(string rutaBaseDatos, string carpetaTemporal)
    {
        if (rutaBaseDatos.StartsWith(@"\\", StringComparison.Ordinal) || rutaBaseDatos.StartsWith("//", StringComparison.Ordinal)) return false;
        string ruta, temporal;
        try
        {
            ruta = Path.GetFullPath(rutaBaseDatos);
            temporal = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpetaTemporal));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        if (ruta.IndexOf(':', 2) >= 0 || !ruta.StartsWith(temporal + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;

        // Ni la carpeta temporal ni nada entre ella y la BD puede ser un enlace.
        for (var actual = ruta; actual.Length >= temporal.Length; actual = Path.GetDirectoryName(actual) ?? "")
        {
            var info = new FileInfo(actual);
            if (info.Exists || Directory.Exists(actual))
            {
                var atributos = File.GetAttributes(actual);
                if ((atributos & FileAttributes.ReparsePoint) != 0) return false;
            }
            if (actual.Length == temporal.Length) break;
        }
        return true;
    }
}
