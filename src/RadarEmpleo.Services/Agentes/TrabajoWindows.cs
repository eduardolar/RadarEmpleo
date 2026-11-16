using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RadarEmpleo.Services.Agentes;

/// <summary>
/// Un Job Object de Windows con <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>: al cerrarlo (o si la API muere y
/// Windows cierra su manejador) mata a todos sus procesos, también a los hijos que lancen después de entrar.
/// Es la red de seguridad de <see cref="Process.Kill(bool)"/>.
/// </summary>
internal sealed class TrabajoWindows : IDisposable
{
    private const int InformacionLimitesExtendida = 9;
    private const uint MatarAlCerrar = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct LimitesBasicos
    {
        public long LimiteTiempoProceso;
        public long LimiteTiempoTrabajo;
        public uint Opciones;
        public UIntPtr MinimoMemoria;
        public UIntPtr MaximoMemoria;
        public uint LimiteProcesos;
        public UIntPtr Afinidad;
        public uint ClasePrioridad;
        public uint ClasePlanificacion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ContadoresEntradaSalida
    {
        public ulong Lecturas, Escrituras, Otras, BytesLeidos, BytesEscritos, BytesOtros;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LimitesExtendidos
    {
        public LimitesBasicos Basicos;
        public ContadoresEntradaSalida EntradaSalida;
        public UIntPtr LimiteMemoriaProceso;
        public UIntPtr LimiteMemoriaTrabajo;
        public UIntPtr PicoMemoriaProceso;
        public UIntPtr PicoMemoriaTrabajo;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr seguridad, string? nombre);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle trabajo, int clase, ref LimitesExtendidos informacion, uint largo);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle trabajo, IntPtr proceso);

    private readonly SafeFileHandle _manejador;

    private TrabajoWindows(SafeFileHandle manejador) => _manejador = manejador;

    /// <summary>Crea el trabajo y mete en él al proceso (y, desde ahí, a todo lo que lance).</summary>
    /// <exception cref="Win32Exception">Windows no lo ha dejado.</exception>
    [SupportedOSPlatform("windows")]
    public static TrabajoWindows Para(Process proceso)
    {
        var manejador = CreateJobObject(IntPtr.Zero, null);
        if (manejador.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var trabajo = new TrabajoWindows(manejador);
        try
        {
            var limites = new LimitesExtendidos { Basicos = new LimitesBasicos { Opciones = MatarAlCerrar } };
            if (!SetInformationJobObject(manejador, InformacionLimitesExtendida, ref limites, (uint)Marshal.SizeOf<LimitesExtendidos>()))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (!AssignProcessToJobObject(manejador, proceso.Handle))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return trabajo;
        }
        catch
        {
            trabajo.Dispose();
            throw;
        }
    }

    /// <summary>Cierra el trabajo: Windows mata a lo que quede dentro.</summary>
    public void Dispose() => _manejador.Dispose();
}
