namespace RadarEmpleo.Domain;

/// <summary>Un caso de la evaluación: el encaje esperado y el que ha dado Claude (null si no ha respondido).</summary>
public sealed record CasoPuntuado(Encaje Esperado, Encaje? Obtenido);

/// <summary>
/// El acierto de AnalizarEncaje sobre un conjunto de casos.
/// </summary>
/// <param name="Exactos">El encaje obtenido es el esperado.</param>
/// <param name="AUnNivel">Fallados por un solo nivel (Alto y Medio, Medio y TiroLargo, TiroLargo y No).</param>
/// <param name="Lejos">Fallados por dos niveles o más.</param>
/// <param name="SinRespuesta">Claude no ha dado un encaje (error, corte o salida que no se entiende).</param>
/// <param name="Matriz">Filas: el esperado; columnas: el obtenido, en el orden de <see cref="PuntuacionEncaje.Niveles"/>.</param>
public sealed record ResultadoPuntuacion(int Total, int Exactos, int AUnNivel, int Lejos, int SinRespuesta, int[,] Matriz);

/// <summary>
/// Puntúa la evaluación del encaje (PR 6 de la F7b), sin BD ni reloj. Los niveles van en orden, de Alto a No, y la distancia
/// entre dos es cuántos pasos hay entre ellos.
/// </summary>
public static class PuntuacionEncaje
{
    /// <summary>Los cuatro niveles que puede dar Claude, de mejor a peor.</summary>
    public static readonly IReadOnlyList<Encaje> Niveles = [Encaje.Alto, Encaje.Medio, Encaje.TiroLargo, Encaje.No];

    /// <summary>Posición del nivel en <see cref="Niveles"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="Encaje.SinValorar"/> u otro valor que no es un nivel.</exception>
    public static int Indice(Encaje encaje) => encaje switch
    {
        Encaje.Alto => 0,
        Encaje.Medio => 1,
        Encaje.TiroLargo => 2,
        Encaje.No => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(encaje), encaje, "No es un nivel de encaje.")
    };

    /// <summary>Cuántos niveles hay entre dos encajes (0 si son el mismo).</summary>
    public static int Distancia(Encaje a, Encaje b) => Math.Abs(Indice(a) - Indice(b));

    public static ResultadoPuntuacion Calcular(IEnumerable<CasoPuntuado> casos)
    {
        var matriz = new int[Niveles.Count, Niveles.Count];
        int total = 0, exactos = 0, aUnNivel = 0, lejos = 0, sinRespuesta = 0;
        foreach (var caso in casos)
        {
            total++;
            var esperado = Indice(caso.Esperado);
            if (caso.Obtenido is not { } obtenido)
            {
                sinRespuesta++;
                continue;
            }
            matriz[esperado, Indice(obtenido)]++;
            switch (Distancia(caso.Esperado, obtenido))
            {
                case 0: exactos++; break;
                case 1: aUnNivel++; break;
                default: lejos++; break;
            }
        }
        return new ResultadoPuntuacion(total, exactos, aUnNivel, lejos, sinRespuesta, matriz);
    }
}
