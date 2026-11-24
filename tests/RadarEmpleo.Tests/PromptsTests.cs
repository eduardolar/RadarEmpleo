using System.Text.Json;
using System.Text.RegularExpressions;
using RadarEmpleo.Services.Agentes;

namespace RadarEmpleo.Tests;

/// <summary><see cref="Prompts"/>: las plantillas y los esquemas embebidos de AnalizarEncaje y ExtraerRegistro (tabla 4.4 del plan).</summary>
public class PromptsTests
{
    public static TheoryData<string, string[]> MarcadoresDeCadaTarea => new()
    {
        { "analizar-encaje", ["empresa", "puesto", "enlace", "ubicacion", "modalidad", "lecturaOferta", "oferta", "perfil"] },
        { "extraer-registro", ["registro", "parte"] }
    };

    private static DefinicionTarea Definicion(string nombre) => DefinicionesTareas.Todas.Single(d => d.NombrePrompt == nombre);

    private static JsonElement EsquemaDe(string nombre) => JsonDocument.Parse(Prompts.Esquema(Definicion(nombre))).RootElement;

    [Theory]
    [MemberData(nameof(MarcadoresDeCadaTarea))]
    public void Plantilla_Embebida_TieneSusMarcadoresDentroDelBloqueYSeRellena(string nombre, string[] marcadores)
    {
        var plantilla = Prompts.Plantilla(Definicion(nombre));

        var usados = Regex.Matches(plantilla, @"\{\{([a-zA-Z0-9]+)\}\}").Select(m => m.Groups[1].Value).Distinct().Order();
        Assert.Equal(marcadores.Order(), usados);
        var prompt = PlantillaPrompt.Rellenar(plantilla, marcadores.ToDictionary(m => m, m => new DatoPrompt($"valor de {m}", 100)));
        Assert.DoesNotContain("{{", prompt);
        Assert.Contains("valor de " + marcadores[0], prompt);
        Assert.DoesNotContain('\r', prompt);
    }

    [Fact]
    public void Plantilla_DeAnalizarEncaje_DiceQueNoHayCvYQueNoTieneHerramientas()
    {
        var plantilla = Prompts.Plantilla(DefinicionesTareas.AnalizarEncaje);

        Assert.Contains("no tienes su CV", plantilla);
        Assert.Contains("sin herramientas", plantilla);
        Assert.Contains("Haz lo que haría el analista de encaje", plantilla);
    }

    [Fact]
    public void Plantilla_DeExtraerRegistro_PideElNumeroDeLineaYNuncaElTexto()
    {
        var plantilla = Prompts.Plantilla(DefinicionesTareas.ExtraerRegistro);

        Assert.Contains("empieza por su número y «│»", plantilla);
        Assert.Contains("**Nunca copies el texto de la línea**", plantilla);
        Assert.DoesNotContain("`cita`", plantilla);
    }

    [Fact]
    public void Esquema_DeAnalizarEncaje_TieneLosCamposYLimitesDeLaTabla()
    {
        var esquema = EsquemaDe("analizar-encaje");

        Assert.False(esquema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["encaje", "pega", "analisis", "resumen"], Textos(esquema.GetProperty("required")));
        var campos = esquema.GetProperty("properties");
        Assert.Equal(["Alto", "Medio", "TiroLargo", "No"], Textos(campos.GetProperty("encaje").GetProperty("enum")));
        Assert.Equal(2000, campos.GetProperty("pega").GetProperty("maxLength").GetInt32());
        Assert.Equal(8000, campos.GetProperty("analisis").GetProperty("maxLength").GetInt32());
        Assert.Equal(300, campos.GetProperty("resumen").GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void Esquema_DeExtraerRegistro_TieneLosCamposYLimitesDeLaTabla()
    {
        var esquema = EsquemaDe("extraer-registro");

        Assert.Equal(["candidaturas", "dudas"], Textos(esquema.GetProperty("required")));
        var candidaturas = esquema.GetProperty("properties").GetProperty("candidaturas");
        Assert.Equal(1000, candidaturas.GetProperty("maxItems").GetInt32());
        var dudas = esquema.GetProperty("properties").GetProperty("dudas");
        Assert.Equal(ManejadorExtraerRegistro.MaximoDudas, dudas.GetProperty("maxItems").GetInt32());
        var duda = dudas.GetProperty("items");
        Assert.False(duda.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["linea", "texto"], Textos(duda.GetProperty("required")));
        Assert.Equal(ManejadorExtraerRegistro.LargoDuda, duda.GetProperty("properties").GetProperty("texto").GetProperty("maxLength").GetInt32());
        Assert.Equal(1, duda.GetProperty("properties").GetProperty("linea").GetProperty("minimum").GetInt32());

        var fila = candidaturas.GetProperty("items");
        Assert.False(fila.GetProperty("additionalProperties").GetBoolean());
        // Solo lo que hay que interpretar: la sección, el enlace y la nota los saca la app de la línea.
        string[] esperados = ["linea", "fecha", "fechaAproximada", "empresa", "puesto", "estado", "motivoDescarte"];
        Assert.Equal(esperados, Textos(fila.GetProperty("required")));
        Assert.Equal(esperados.Order(), fila.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());

        var campos = fila.GetProperty("properties");
        Assert.Equal(Enum.GetNames<Domain.EstadoCandidatura>(), Textos(campos.GetProperty("estado").GetProperty("enum")));
        Assert.Equal("integer", campos.GetProperty("linea").GetProperty("type").GetString());
        Assert.Equal(1, campos.GetProperty("linea").GetProperty("minimum").GetInt32());
        Assert.Equal([.. Enum.GetNames<Domain.MotivoDescarte>(), null],
            campos.GetProperty("motivoDescarte").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(@"^\d{4}-\d{2}-\d{2}$", campos.GetProperty("fecha").GetProperty("pattern").GetString());
        Assert.Equal(200, campos.GetProperty("empresa").GetProperty("maxLength").GetInt32());
        Assert.Equal(300, campos.GetProperty("puesto").GetProperty("maxLength").GetInt32());
    }

    [Theory]
    [InlineData("analizar-encaje")]
    [InlineData("extraer-registro")]
    public void Esquema_EntraMinificadoEnLosArgumentos(string nombre)
    {
        var definicion = Definicion(nombre);
        var argumentos = ConstructorArgumentos.Construir(definicion, Prompts.Esquema(definicion), new ValoresArgumentos(Path.GetTempPath()));

        var esquema = argumentos[argumentos.ToList().IndexOf("--json-schema") + 1];
        Assert.DoesNotContain('\n', esquema);
        Assert.DoesNotContain(": ", esquema);
        Assert.True(Prompts.Existen(definicion));
    }

    [Fact]
    public void Rastreo_TodaviaNoTienePlantilla()
    {
        Assert.False(Prompts.Existen(DefinicionesTareas.Rastreo));
        Assert.Throws<InvalidOperationException>(() => Prompts.Plantilla(DefinicionesTareas.Rastreo));
    }

    private static string[] Textos(JsonElement lista) => [.. lista.EnumerateArray().Select(v => v.GetString()!)];
}
