using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RadarEmpleo.Services.Curriculum;

/// <summary>
/// Lo que .NET no da sobre un archivo ya abierto en Windows: su ruta real (con las mayúsculas del disco,
/// los nombres largos y los enlaces ya resueltos) y cuántos enlaces duros tiene.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ArchivosWindows
{
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareTodo = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileNameNormalized = 0x0;

    [StructLayout(LayoutKind.Sequential)]
    private struct InformacionArchivo
    {
        public uint Atributos;
        public uint CreacionBajo, CreacionAlto;
        public uint AccesoBajo, AccesoAlto;
        public uint EscrituraBajo, EscrituraAlto;
        public uint NumeroSerieVolumen;
        public uint TamanoAlto, TamanoBajo;
        public uint NumeroEnlaces;
        public uint IndiceAlto, IndiceBajo;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle archivo, char[] ruta, uint largo, uint opciones);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle archivo, out InformacionArchivo informacion);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string ruta, uint acceso, uint compartir, IntPtr seguridad,
        uint creacion, uint opciones, IntPtr plantilla);

    /// <summary>Ruta real del archivo abierto, sin el prefijo «\\?\».</summary>
    /// <exception cref="IOException">Windows no la ha dado.</exception>
    public static string RutaFinal(SafeFileHandle archivo)
    {
        var bufer = new char[512];
        while (true)
        {
            var largo = GetFinalPathNameByHandle(archivo, bufer, (uint)bufer.Length, FileNameNormalized);
            if (largo == 0) throw new IOException("No se ha podido leer la ruta real del archivo.", Marshal.GetLastPInvokeError());
            if (largo < bufer.Length) return SinPrefijo(new string(bufer, 0, (int)largo));
            bufer = new char[largo + 1];
        }
    }

    /// <summary>Ruta real de una carpeta (para comparar con la de los archivos con el mismo criterio).</summary>
    public static string RutaFinalCarpeta(string carpeta)
    {
        using var manejador = CreateFile(carpeta, FileReadAttributes, FileShareTodo, IntPtr.Zero, OpenExisting,
            FileFlagBackupSemantics, IntPtr.Zero);
        if (manejador.IsInvalid)
            throw new IOException("No se ha podido abrir la carpeta.", Marshal.GetLastPInvokeError());
        return RutaFinal(manejador);
    }

    /// <summary>Cuántos nombres tiene el archivo en el disco (más de 1 si hay enlaces duros).</summary>
    public static uint NumeroEnlaces(SafeFileHandle archivo) =>
        GetFileInformationByHandle(archivo, out var informacion)
            ? informacion.NumeroEnlaces
            : throw new IOException("No se ha podido leer la información del archivo.", Marshal.GetLastPInvokeError());

    /// <summary>
    /// Enlaces duros de un archivo sin abrir su contenido (solo atributos: no choca con el bloqueo de Word).
    /// </summary>
    /// <exception cref="IOException">No se ha podido abrir o leer.</exception>
    public static uint NumeroEnlaces(string ruta)
    {
        using var manejador = CreateFile(ruta, FileReadAttributes, FileShareTodo, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (manejador.IsInvalid)
            throw new IOException("No se ha podido abrir el archivo.", Marshal.GetLastPInvokeError());
        return NumeroEnlaces(manejador);
    }

    private static string SinPrefijo(string ruta)
    {
        if (ruta.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + ruta[8..];
        if (ruta.StartsWith(@"\\?\", StringComparison.Ordinal)) return ruta[4..];
        return ruta;
    }
}
