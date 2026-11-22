using System.Text.Json;
using System.Text.RegularExpressions;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary>
/// El conjunto inventado de <c>datos-ejemplo/encaje/</c> con el que se mide AnalizarEncaje (PR 6 de la F7b): completo, con
/// los cuatro niveles y sin datos personales.
/// </summary>
public class ConjuntoEncajeTests
{
    private static readonly string Carpeta = Path.Combine(FabricaApi.CarpetaDatosEjemplo, "encaje");
    private static readonly string[] Niveles = ["Alto", "Medio", "TiroLargo", "No"];

    private static JsonElement[] Ofertas()
    {
        using var documento = JsonDocument.Parse(File.ReadAllText(Path.Combine(Carpeta, "esperado.json")));
        return documento.RootElement.GetProperty("ofertas").EnumerateArray().Select(o => o.Clone()).ToArray();
    }

    [Fact]
    public void Esperado_TieneVeinteOfertasConSuArchivoYSusCampos()
    {
        var ofertas = Ofertas();

        Assert.Equal(20, ofertas.Length);
        Assert.Equal(Enumerable.Range(1, 20).Select(n => $"oferta-{n:00}"), ofertas.Select(o => o.GetProperty("id").GetString()));
        foreach (var oferta in ofertas)
        {
            Assert.True(File.Exists(Path.Combine(Carpeta, oferta.GetProperty("id").GetString() + ".md")));
            foreach (var campo in new[] { "empresa", "puesto", "ubicacion", "modalidad", "porque" })
                Assert.False(string.IsNullOrWhiteSpace(oferta.GetProperty(campo).GetString()), $"{campo} vacío");
        }
        Assert.Equal(20, Directory.GetFiles(Carpeta, "oferta-*.md").Length);
    }

    [Fact]
    public void Esperado_SoloUsaLosCuatroNivelesYLosCubreTodos()
    {
        var encajes = Ofertas().Select(o => o.GetProperty("encaje").GetString()).ToArray();

        Assert.All(encajes, e => Assert.Contains(e, Niveles));
        Assert.All(Niveles, n => Assert.True(encajes.Count(e => e == n) >= 3, $"Pocas ofertas con {n}"));
    }

    [Fact]
    public void Conjunto_NoTieneDatosPersonalesNiRutasDelPc()
    {
        var telefono = new Regex(@"(?<![\d.])(?:\+34\s?)?[6-9]\d{2}(?:[\s.-]?\d{2,3}){2,3}(?![\d.])");
        var correo = new Regex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+");
        foreach (var archivo in Directory.GetFiles(Carpeta))
        {
            var texto = File.ReadAllText(archivo);
            var nombre = Path.GetFileName(archivo);
            Assert.DoesNotMatch(telefono, texto);
            Assert.DoesNotMatch(correo, texto);
            foreach (var prohibido in new[] { "Eduardo", "Lara Navarro", "Desktop", @"C:\Users", "C:/Users", "AppData", "Curriculum\\", "http://", "https://" })
                Assert.False(texto.Contains(prohibido, StringComparison.OrdinalIgnoreCase), $"{nombre} contiene «{prohibido}»");
        }
    }

    [Fact]
    public void Ofertas_DicenQueSonInventadas()
    {
        Assert.All(Directory.GetFiles(Carpeta, "oferta-*.md"),
            archivo => Assert.Contains("(oferta inventada)", File.ReadAllLines(archivo)[0]));
    }
}
