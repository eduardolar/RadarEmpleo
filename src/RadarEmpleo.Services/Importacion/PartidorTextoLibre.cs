using RadarEmpleo.Services.Agentes;
using RadarEmpleo.Services.Rastreos;

namespace RadarEmpleo.Services.Importacion;

/// <summary>Un trozo del texto libre, para leerlo en una consulta de ExtraerRegistro.</summary>
/// <param name="Lineas">Las líneas del trozo, tal cual y con su número en el registro. Si el trozo sigue a otro del mismo
/// apartado, la primera es la línea del título de ese apartado, repetida.</param>
/// <param name="Titulo">La línea del título del primer apartado del trozo, sin espacios en los extremos («## Agosto»), o
/// null si el trozo empieza en lo que va antes del primer título.</param>
public sealed record TrozoTextoLibre(IReadOnlyList<LineaRegistro> Lineas, string? Titulo)
{
    /// <summary>Las líneas unidas con saltos LF, como van al prompt.</summary>
    public string Texto => new TextoLibreRegistro(Lineas).Texto;
}

/// <summary>
/// Parte el texto libre del registro en trozos de un largo máximo (función pura: ni disco ni BD). Un <b>apartado</b> empieza
/// en un encabezado de nivel 1 o 2 (<see cref="AnalizadorRastreo.EsEncabezado"/>, el mismo criterio que
/// <see cref="AnalizadorRastreo.SeccionesRastreo"/>; los «###» no cortan) y llega hasta el siguiente; lo de antes del primer
/// título es un apartado más. Los apartados enteros se juntan mientras quepan. Uno que no cabe solo se parte por párrafos
/// (separados por líneas en blanco); un párrafo que no cabe, por líneas (el título, con la siguiente), y una línea que no cabe va sola. <b>Nunca se parte
/// una línea</b>: cada una sigue siendo una línea del registro, con su número, para buscar después la cita. Los trozos que
/// siguen a uno de un apartado partido llevan delante su línea de título, repetida, para que se sepa de qué apartado son.
/// </summary>
public static class PartidorTextoLibre
{
    /// <summary>Los trozos, en el orden del registro. Ninguno queda solo con líneas en blanco; un texto vacío no da ninguno.</summary>
    /// <param name="largo">Largo máximo de un trozo: lo que mide en el prompt (<see cref="Medir"/>). Solo se pasa si una
    /// línea sola, o una línea con el título repetido delante, no cabe.</param>
    public static IReadOnlyList<TrozoTextoLibre> Partir(TextoLibreRegistro texto, int largo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(largo);
        var trozos = new List<TrozoTextoLibre>();
        var actual = new List<LineaRegistro>();
        var medida = 0;
        string? titulo = null;

        void Cerrar()
        {
            if (!new TextoLibreRegistro(actual).Vacio) trozos.Add(new TrozoTextoLibre(actual.ToList(), titulo));
            actual.Clear();
            medida = 0;
        }

        foreach (var apartado in Apartados(texto.Lineas))
        {
            var cabecera = apartado.Titulo is null ? null : apartado.Lineas[0];
            foreach (var (bloque, sigue) in Bloques(apartado, cabecera, largo))
            {
                var tamano = bloque.Sum(Medir);
                if (actual.Count > 0 && medida + tamano > largo) Cerrar();
                if (actual.Count == 0)
                {
                    titulo = apartado.Titulo;
                    if (sigue && cabecera is not null)
                    {
                        actual.Add(cabecera);
                        medida = Medir(cabecera);
                    }
                }
                actual.AddRange(bloque);
                medida += tamano;
            }
        }
        Cerrar();
        return trozos;
    }

    /// <summary>Lo que mide una línea en el prompt: su texto como lo deja <see cref="PlantillaPrompt"/>, más el salto.</summary>
    public static int Medir(LineaRegistro linea) => PlantillaPrompt.Normalizar(linea.Texto, unaLinea: false).Length + 1;

    private sealed record Apartado(List<LineaRegistro> Lineas, string? Titulo);

    private static List<Apartado> Apartados(IReadOnlyList<LineaRegistro> lineas)
    {
        var apartados = new List<Apartado>();
        foreach (var linea in lineas)
        {
            if (AnalizadorRastreo.EsEncabezado(linea.Texto)) apartados.Add(new Apartado([linea], linea.Texto.Trim()));
            else if (apartados.Count == 0) apartados.Add(new Apartado([linea], null));
            else apartados[^1].Lineas.Add(linea);
        }
        return apartados;
    }

    /// <summary>
    /// Las piezas que no se separan: el apartado entero si cabe y, si no, sus párrafos, o las líneas de los párrafos que no
    /// caben con el título repetido delante. <c>Sigue</c> dice que la pieza no tiene la primera línea del apartado.
    /// </summary>
    private static IEnumerable<(List<LineaRegistro> Bloque, bool Sigue)> Bloques(Apartado apartado, LineaRegistro? cabecera, int largo)
    {
        if (apartado.Lineas.Sum(Medir) <= largo)
        {
            yield return (apartado.Lineas, false);
            yield break;
        }

        // El primer párrafo ya lleva el título; los demás pueden necesitarlo repetido delante.
        var cabeDetras = largo - (cabecera is null ? 0 : Medir(cabecera));
        var primera = true;
        foreach (var parrafo in Parrafos(apartado.Lineas, cabecera))
        {
            if (parrafo.Sum(Medir) <= (primera ? largo : cabeDetras))
            {
                yield return (parrafo, !primera);
                primera = false;
                continue;
            }
            foreach (var lineas in PorLineas(parrafo, primera ? cabecera : null))
            {
                yield return (lineas, !primera);
                primera = false;
            }
        }
    }

    /// <summary>
    /// Cada línea con texto en su pieza; las líneas en blanco van con la de antes y el título, con la de después: así ningún
    /// trozo acaba en un título suelto que luego se repite.
    /// </summary>
    private static IEnumerable<List<LineaRegistro>> PorLineas(List<LineaRegistro> parrafo, LineaRegistro? cabecera)
    {
        var pieza = new List<LineaRegistro>();
        foreach (var linea in parrafo)
        {
            if (pieza.Count > 0 && !SoloElTitulo(pieza, cabecera) && !EnBlanco(linea))
            {
                yield return pieza;
                pieza = [];
            }
            pieza.Add(linea);
        }
        if (pieza.Count > 0) yield return pieza;
    }

    /// <summary>
    /// Un párrafo empieza en una línea con texto que va detrás de una en blanco; las líneas en blanco van con el de antes. El
    /// título no es un párrafo solo, aunque lleve líneas en blanco detrás: va con el primero.
    /// </summary>
    private static List<List<LineaRegistro>> Parrafos(List<LineaRegistro> lineas, LineaRegistro? cabecera)
    {
        var parrafos = new List<List<LineaRegistro>>();
        var anteriorEnBlanco = true;
        foreach (var linea in lineas)
        {
            var enBlanco = EnBlanco(linea);
            if (parrafos.Count == 0 || (anteriorEnBlanco && !enBlanco && !SoloElTitulo(parrafos[^1], cabecera))) parrafos.Add([]);
            parrafos[^1].Add(linea);
            anteriorEnBlanco = enBlanco;
        }
        return parrafos;
    }

    /// <summary>Las líneas son el título del apartado y, si acaso, líneas en blanco.</summary>
    private static bool SoloElTitulo(List<LineaRegistro> lineas, LineaRegistro? cabecera) =>
        cabecera is not null && ReferenceEquals(lineas[0], cabecera) && lineas.Skip(1).All(EnBlanco);

    private static bool EnBlanco(LineaRegistro linea) => string.IsNullOrWhiteSpace(linea.Texto);
}
