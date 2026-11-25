using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using RadarEmpleo.Services.Agentes;

namespace RadarEmpleo.Tests;

/// <summary>
/// <c>TrabajoWindows</c> (el Job Object que mata a Claude y a sus hijos si la API muere) con
/// <c>ClaudeSimulado</c>, sin ventana: cuando Windows no deja meter el proceso y cuando sí. La clase es interna y
/// el proyecto no abre sus internos a los tests, así que se llama por reflexión: si cambia de nombre, fallan.
/// </summary>
public sealed class TrabajoWindowsTests
{
    private const string Motivo = "los Job Objects solo existen en Windows, donde la app lanza los agentes";

    /// <summary><c>TrabajoWindows.Para(proceso)</c>, con su excepción tal cual (sin el envoltorio de la reflexión).</summary>
    private static IDisposable Para(Process proceso)
    {
        var tipo = typeof(EjecutorClaudeCli).Assembly.GetType("RadarEmpleo.Services.Agentes.TrabajoWindows", throwOnError: true)!;
        var para = tipo.GetMethod("Para", BindingFlags.Public | BindingFlags.Static, [typeof(Process)])
            ?? throw new MissingMethodException(tipo.FullName, "Para");
        try
        {
            return (IDisposable)para.Invoke(null, [proceso])!;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    /// <summary>ClaudeSimulado oculto. Con la entrada cerrada sigue adelante: en «espera», se queda esperando.</summary>
    private static Process Lanzar(params string[] argumentos)
    {
        var proceso = LanzarSinCerrarLaEntrada(argumentos);
        proceso.StandardInput.Close();
        return proceso;
    }

    /// <summary>ClaudeSimulado oculto y parado: no hace nada (tampoco lanzar a su hijo) hasta que se le cierra la entrada.</summary>
    private static Process LanzarSinCerrarLaEntrada(params string[] argumentos)
    {
        var inicio = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ClaudeSimulado.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argumento in argumentos) inicio.ArgumentList.Add(argumento);
        return Process.Start(inicio)!;
    }

    private static void Matar(Process proceso)
    {
        try
        {
            if (!proceso.HasExited) proceso.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            // Ya había terminado.
        }
    }

    /// <summary>
    /// Lo que el ejecutor espera cuando el trabajo no se puede montar: una <see cref="Win32Exception"/>, que apunta
    /// como aviso para seguir sin la red de seguridad. Un proceso que ya ha terminado no entra en ningún trabajo.
    /// </summary>
    [FactSoloWindows(Motivo)]
    public async Task Para_ConUnProcesoQueYaHaTerminado_LanzaWin32Exception()
    {
        using var proceso = Lanzar("--version");
        await proceso.WaitForExitAsync(CancellationToken.None);

        var error = Assert.Throws<Win32Exception>(() => Para(proceso));

        Assert.NotEqual(0, error.NativeErrorCode);
    }

    /// <summary>
    /// Lo que hace de red de seguridad: al cerrar el trabajo muere también el hijo que el proceso lanzó después de
    /// entrar, sin que nadie llame a <c>Kill</c>. Sin carrera: ClaudeSimulado no lanza al hijo hasta que se le cierra la
    /// entrada, y eso se hace con el padre ya dentro del trabajo.
    /// </summary>
    [FactSoloWindows(Motivo)]
    public async Task Dispose_MataTambienAlHijoQueElProcesoLanzaDespuesDeEntrar()
    {
        var archivoPid = Path.Combine(Path.GetTempPath(), $"radar-trabajo-{Guid.NewGuid():N}.txt");
        using var padre = LanzarSinCerrarLaEntrada("--sim:modo=hijo", "--sim:segundos=120", $"--sim:pidhijo={archivoPid}");
        int? pidHijo = null;
        try
        {
            var trabajo = Para(padre);
            padre.StandardInput.Close();
            pidHijo = await Esperar.HastaAsync(() => Task.FromResult(LeerPid(archivoPid)), p => p is not null,
                _ => "ClaudeSimulado no ha escrito el pid de su hijo.");
            Assert.True(Vivo(pidHijo!.Value), "El hijo tenía que seguir vivo antes de cerrar el trabajo.");
            Assert.False(padre.HasExited);

            trabajo.Dispose();

            await Esperar.HastaAsync(() => padre.HasExited && !Vivo(pidHijo.Value), "Al cerrar el trabajo siguen vivos el proceso o su hijo.");
        }
        finally
        {
            Matar(padre);
            if (pidHijo is { } pid) MatarPorPid(pid);
            File.Delete(archivoPid);
        }
    }

    /// <summary>El pid del archivo, o null si aún no existe o está a medio escribir.</summary>
    private static int? LeerPid(string ruta)
    {
        try
        {
            return int.TryParse(File.ReadAllText(ruta), out var pid) ? pid : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool Vivo(int pid)
    {
        try
        {
            using var proceso = Process.GetProcessById(pid);
            return !proceso.HasExited;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static void MatarPorPid(int pid)
    {
        try
        {
            using var proceso = Process.GetProcessById(pid);
            Matar(proceso);
        }
        catch (ArgumentException)
        {
            // Ya no existe.
        }
    }

    [FactSoloWindows(Motivo)]
    public async Task Dispose_MataAlProcesoQueEstaDentro_YRepetirloNoFalla()
    {
        using var proceso = Lanzar("--sim:modo=espera", "--sim:segundos=120");
        try
        {
            var trabajo = Para(proceso);
            Assert.False(proceso.HasExited);

            trabajo.Dispose();
            trabajo.Dispose();

            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await proceso.WaitForExitAsync(limite.Token);
            Assert.True(proceso.HasExited);
        }
        finally
        {
            Matar(proceso);
        }
    }
}
