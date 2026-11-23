using System.Diagnostics;

namespace RadarEmpleo.Tests;

/// <summary>
/// Esperas de los tests sin pausas fijas: miran la condición cada poco, terminan en cuanto se cumple y, si no se cumple
/// en <see cref="Tope"/>, fallan diciendo qué se esperaba. Solo para lo que no avisa de otra forma (lo que el procesador
/// escribe en la BD en segundo plano, un proceso de verdad); si el falso puede avisar, mejor una señal.
/// </summary>
internal static class Esperar
{
    /// <summary>Tope generoso: aunque la máquina vaya cargada, nada de lo que se espera debería acercarse.</summary>
    public static readonly TimeSpan Tope = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Intervalo = TimeSpan.FromMilliseconds(20);

    /// <summary>Lee hasta que lo leído cumpla <paramref name="condicion"/> y lo devuelve.</summary>
    /// <param name="queFalta">El mensaje si se acaba el tope, con lo último leído.</param>
    public static async Task<T> HastaAsync<T>(Func<Task<T>> leer, Func<T, bool> condicion, Func<T, string> queFalta)
    {
        var cronometro = Stopwatch.StartNew();
        while (true)
        {
            var leido = await leer();
            if (condicion(leido)) return leido;
            if (cronometro.Elapsed > Tope) throw new TimeoutException($"{queFalta(leido)} (tras {Tope.TotalSeconds:0} s)");
            await Task.Delay(Intervalo);
        }
    }

    /// <summary>Espera a que se cumpla <paramref name="condicion"/>.</summary>
    /// <param name="queFalta">El mensaje si se acaba el tope.</param>
    public static Task HastaAsync(Func<bool> condicion, string queFalta) =>
        HastaAsync(() => Task.FromResult(condicion()), cumplida => cumplida, _ => queFalta);
}
