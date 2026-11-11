using RadarEmpleo.Services.Perfil;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary><see cref="AnalizadorPerfil"/> con <c>perfil-ejemplo.md</c> y textos inventados.</summary>
public class AnalizadorPerfilTests
{
    [Fact]
    public void Analizar_PerfilDeEjemplo_TituloIntroduccionYSecciones()
    {
        var perfil = AnalizadorPerfil.Analizar(File.ReadAllText(FabricaApi.RutaPerfilEjemplo).ReplaceLineEndings("\r\n"));

        Assert.Equal("Perfil de búsqueda (ejemplo inventado)", perfil.Titulo);
        Assert.Equal("Todo lo que hay aquí es inventado. Sirve para los tests de RadarEmpleo.", perfil.Introduccion);
        Assert.Equal(["Filtros de búsqueda", "Lo que no encaja", "Notas"], perfil.Secciones.Select(s => s.Titulo));
        Assert.StartsWith("- Puestos: desarrollador .NET", perfil.Secciones[0].Markdown);
        Assert.EndsWith("22.000 € brutos al año.", perfil.Secciones[0].Markdown);
        Assert.DoesNotContain(perfil.Secciones, s => s.Markdown.Contains('\r'));
    }

    [Fact]
    public void Analizar_SubseccionesYLineasEnBlanco_SeQuedanDentroDeSuSeccion()
    {
        const string texto = "# Perfil\n\n## Stack\n\n- C#\n\n### Front\n- Angular\n\n\n## Zona\nMálaga\n";

        var perfil = AnalizadorPerfil.Analizar(texto);

        Assert.Null(perfil.Introduccion);
        Assert.Equal("- C#\n\n### Front\n- Angular", perfil.Secciones[0].Markdown);
        Assert.Equal("Málaga", perfil.Secciones[1].Markdown);
    }

    [Fact]
    public void Analizar_SinTitulo_UsaElTituloPorDefectoYLoDeAntesEsLaIntroduccion()
    {
        var perfil = AnalizadorPerfil.Analizar("﻿Ficha del equipo.\n\n## Filtros\n- Remoto\n");

        Assert.Equal(AnalizadorPerfil.TituloPorDefecto, perfil.Titulo);
        Assert.Equal("Ficha del equipo.", perfil.Introduccion);
        Assert.Equal("Filtros", Assert.Single(perfil.Secciones).Titulo);
    }

    [Fact]
    public void Analizar_UnSegundoTituloPrincipal_EsParteDeLaSeccion()
    {
        var perfil = AnalizadorPerfil.Analizar("# Perfil\n## Notas\n# Otro\ntexto\n");

        Assert.Equal("Perfil", perfil.Titulo);
        Assert.Equal("# Otro\ntexto", perfil.Secciones[0].Markdown);
    }

    [Fact]
    public void Analizar_SeccionVacia_SeQuedaConElTitulo()
    {
        var perfil = AnalizadorPerfil.Analizar("# Perfil\n## Vacía\n## Llena\nalgo");

        Assert.Equal(("Vacía", ""), (perfil.Secciones[0].Titulo, perfil.Secciones[0].Markdown));
    }

    [Fact]
    public void Analizar_TextoVacio_SinSecciones()
    {
        var perfil = AnalizadorPerfil.Analizar("");

        Assert.Equal(AnalizadorPerfil.TituloPorDefecto, perfil.Titulo);
        Assert.Null(perfil.Introduccion);
        Assert.Empty(perfil.Secciones);
    }
}
