using System.Text.RegularExpressions;
using RadarEmpleo.Services.Contratos;

namespace RadarEmpleo.Services.Perfil;

/// <summary>Lo que sale de partir el perfil (sin la fecha del archivo).</summary>
public sealed record PerfilAnalizado(string Titulo, string? Introduccion, IReadOnlyList<SeccionPerfilDto> Secciones);

/// <summary>
/// Parte <c>perfil-busqueda.md</c> por sus secciones «## » (función pura). El título es el primer «# »;
/// lo que va antes de la primera sección es la introducción. Los «### » quedan dentro de su sección.
/// </summary>
public static partial class AnalizadorPerfil
{
    /// <summary>Título cuando el archivo no empieza por un «# ».</summary>
    public const string TituloPorDefecto = "Perfil de búsqueda";

    [GeneratedRegex(@"\r\n|\r|\n")]
    private static partial Regex SaltoDeLinea();

    [GeneratedRegex(@"^#(?!#)\s*(.*?)[\s#]*$")]
    private static partial Regex TituloPrincipal();

    [GeneratedRegex(@"^##(?!#)\s*(.*?)[\s#]*$")]
    private static partial Regex TituloSeccion();

    public static PerfilAnalizado Analizar(string texto)
    {
        var lineas = SaltoDeLinea().Split(texto.TrimStart('﻿'));
        string? titulo = null;
        var introduccion = new List<string>();
        var secciones = new List<SeccionPerfilDto>();
        string? seccion = null;
        var contenido = new List<string>();

        void CerrarSeccion()
        {
            if (seccion is not null) secciones.Add(new SeccionPerfilDto(seccion, Juntar(contenido)));
            contenido.Clear();
        }

        foreach (var linea in lineas)
        {
            var encabezado = TituloSeccion().Match(linea);
            if (encabezado.Success)
            {
                CerrarSeccion();
                seccion = encabezado.Groups[1].Value.Trim();
                continue;
            }

            if (seccion is null && titulo is null && introduccion.All(l => l.Trim().Length == 0)
                && TituloPrincipal().Match(linea) is { Success: true } principal)
            {
                titulo = principal.Groups[1].Value.Trim();
                continue;
            }

            (seccion is null ? introduccion : contenido).Add(linea);
        }
        CerrarSeccion();

        var intro = Juntar(introduccion);
        return new PerfilAnalizado(
            string.IsNullOrWhiteSpace(titulo) ? TituloPorDefecto : titulo,
            intro.Length == 0 ? null : intro,
            secciones);
    }

    /// <summary>Las líneas sin las vacías del principio y del final.</summary>
    private static string Juntar(List<string> lineas)
    {
        var desde = lineas.FindIndex(l => l.Trim().Length > 0);
        if (desde < 0) return "";
        var hasta = lineas.FindLastIndex(l => l.Trim().Length > 0);
        return string.Join("\n", lineas.GetRange(desde, hasta - desde + 1));
    }
}
