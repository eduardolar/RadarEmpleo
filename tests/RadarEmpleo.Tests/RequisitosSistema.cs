using System.Runtime.InteropServices;

namespace RadarEmpleo.Tests;

/// <summary>
/// Lo que algunos tests necesitan del sistema: Windows, poder crear enlaces simbólicos o un disco con nombres cortos
/// (8.3). Si falta, el test sale como omitido con el motivo en vez de terminar en verde sin haber probado nada.
/// </summary>
internal static class RequisitosSistema
{
    private static readonly Lazy<bool> HayEnlacesSimbolicos = new(ProbarEnlacesSimbolicos);
    private static readonly Lazy<bool> HayNombresCortos = new(ProbarNombresCortos);

    /// <summary>Null en Windows; si no, el motivo de la omisión.</summary>
    public static string? SoloWindows(string motivo) =>
        OperatingSystem.IsWindows() ? null : $"Solo se prueba en Windows: {motivo}.";

    /// <summary>Null si se pueden crear enlaces simbólicos en la carpeta temporal; si no, el motivo de la omisión.</summary>
    public static string? EnlacesSimbolicos() =>
        HayEnlacesSimbolicos.Value ? null
        : OperatingSystem.IsWindows() ? "Windows no deja crear enlaces simbólicos: activa el modo de desarrollador."
        : "Este sistema no deja crear enlaces simbólicos en la carpeta temporal.";

    /// <summary>Null si el disco de la carpeta temporal da nombres cortos (8.3); si no, el motivo de la omisión.</summary>
    public static string? NombresCortos() =>
        SoloWindows("los nombres cortos (8.3) son de Windows")
        ?? (HayNombresCortos.Value ? null : "El disco de la carpeta temporal no genera nombres cortos (8.3): el caso no se puede dar.");

    /// <summary>El nombre corto (8.3) de una ruta, o null si Windows no lo da.</summary>
    public static string? NombreCorto(string ruta)
    {
        var bufer = new char[1024];
        var largo = GetShortPathName(ruta, bufer, (uint)bufer.Length);
        return largo == 0 ? null : new string(bufer, 0, (int)largo);
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode)]
    private static extern uint GetShortPathName(string larga, char[] corta, uint largo);

    private static bool ProbarEnlacesSimbolicos() =>
        ConCarpetaDePrueba(carpeta =>
        {
            var destino = Path.Combine(carpeta, "destino.txt");
            File.WriteAllText(destino, "x");
            try
            {
                File.CreateSymbolicLink(Path.Combine(carpeta, "enlace.txt"), destino);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        });

    private static bool ProbarNombresCortos() =>
        ConCarpetaDePrueba(carpeta =>
        {
            var larga = Path.Combine(carpeta, "sonda_nombre_largo.pdfx");
            File.WriteAllText(larga, "x");
            var corto = NombreCorto(larga);
            return corto is not null && !string.Equals(Path.GetFileName(corto), "sonda_nombre_largo.pdfx", StringComparison.OrdinalIgnoreCase);
        });

    private static bool ConCarpetaDePrueba(Func<string, bool> prueba)
    {
        var carpeta = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"radar-sonda-{Guid.NewGuid():N}")).FullName;
        try
        {
            return prueba(carpeta);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }
}

/// <summary><c>[Fact]</c> que solo se ejecuta en Windows; en otro sistema sale omitido con el motivo.</summary>
public sealed class FactSoloWindowsAttribute : FactAttribute
{
    public FactSoloWindowsAttribute(string motivo) => Skip = RequisitosSistema.SoloWindows(motivo);
}

/// <summary><c>[Theory]</c> que solo se ejecuta en Windows; en otro sistema sale omitido con el motivo.</summary>
public sealed class TheorySoloWindowsAttribute : TheoryAttribute
{
    public TheorySoloWindowsAttribute(string motivo) => Skip = RequisitosSistema.SoloWindows(motivo);
}

/// <summary>
/// <c>[Fact]</c> que crea enlaces simbólicos: si el sistema no deja, sale omitido. Con <paramref name="soloWindows"/>,
/// además, solo se ejecuta en Windows.
/// </summary>
public sealed class FactConEnlacesSimbolicosAttribute : FactAttribute
{
    public FactConEnlacesSimbolicosAttribute(string? soloWindows = null) =>
        Skip = (soloWindows is null ? null : RequisitosSistema.SoloWindows(soloWindows)) ?? RequisitosSistema.EnlacesSimbolicos();
}

/// <summary><c>[Fact]</c> que necesita nombres cortos (8.3): solo en Windows y en un disco que los genere.</summary>
public sealed class FactConNombresCortosAttribute : FactAttribute
{
    public FactConNombresCortosAttribute() => Skip = RequisitosSistema.NombresCortos();
}
