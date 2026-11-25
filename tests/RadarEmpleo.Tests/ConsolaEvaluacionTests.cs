extern alias evaluador;

using evaluador::RadarEmpleo.EvaluacionEncaje;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary>
/// Las reglas puras del evaluador del encaje (<c>tests/EvaluacionEncaje</c>): opciones, margen del tope, carpeta de
/// salida, hash del prompt y validación del conjunto. Nunca lanzan claude.
/// </summary>
public class ConsolaEvaluacionTests
{
    private const decimal Presupuesto = 0.50m;

    private static CasoConjunto Caso(string id, Encaje encaje = Encaje.Medio, string empresa = "Empresa") =>
        new(id, empresa, "Puesto", "Madrid", "Remoto", encaje, "Porque sí");

    [Fact]
    public void LeerOpciones_ConTodo_DevuelveLasOpciones()
    {
        var leidas = ConsolaEvaluacion.LeerOpciones(
            ["--confirmo-gasto", "--tope-usd", "2,5", "--salida", "C:\\x", "--ruta-claude", "C:\\claude.exe", "--falso"], Presupuesto);

        Assert.Null(leidas.Error);
        Assert.Equal(new OpcionesEvaluacion(2.5m, "C:\\x", "C:\\claude.exe", true), leidas.Opciones);
    }

    [Fact]
    public void LeerOpciones_SoloLasObligatorias_DejaLoDemasVacio()
    {
        var leidas = ConsolaEvaluacion.LeerOpciones(["--tope-usd", "5", "--confirmo-gasto"], Presupuesto);

        Assert.Equal(new OpcionesEvaluacion(5m, null, null, false), leidas.Opciones);
    }

    [Theory]
    [InlineData()]
    [InlineData("--tope-usd", "5")]
    [InlineData("--confirmo-gasto")]
    [InlineData("--confirmo-gasto", "--falso")]
    public void LeerOpciones_SinConfirmarOSinTope_Da1(params string[] args)
    {
        var leidas = ConsolaEvaluacion.LeerOpciones(args, Presupuesto);

        Assert.Null(leidas.Opciones);
        Assert.Equal(1, leidas.Codigo);
        Assert.Contains("no he lanzado nada", leidas.Error);
    }

    [Theory]
    [InlineData("--confirmo-gasto", "--tope-usd")] // sin valor
    [InlineData("--confirmo-gasto", "--tope-usd", "5", "--otra")]
    [InlineData("--confirmo-gasto", "--tope-usd", "5", "--salida")]
    [InlineData("--confirmo-gasto", "--tope-usd", "cinco")]
    [InlineData("--confirmo-gasto", "--tope-usd", "-1")]
    [InlineData("--confirmo-gasto", "--tope-usd", "1e1")]
    [InlineData("--confirmo-gasto", "--tope-usd", "0.74")] // no cabe ni una oferta con su margen
    [InlineData("--confirmo-gasto", "--tope-usd", "10.01")]
    public void LeerOpciones_LoQueNoSeEntiendeOUnTopeFueraDeRango_Da2(params string[] args)
    {
        var leidas = ConsolaEvaluacion.LeerOpciones(args, Presupuesto);

        Assert.Null(leidas.Opciones);
        Assert.Equal(2, leidas.Codigo);
        Assert.False(string.IsNullOrWhiteSpace(leidas.Error));
    }

    [Theory]
    [InlineData("0.75", 0.75)]
    [InlineData("0,75", 0.75)]
    [InlineData("10", 10)]
    public void LeerOpciones_LosBordesDelTope_Valen(string texto, double esperado)
    {
        var leidas = ConsolaEvaluacion.LeerOpciones(["--confirmo-gasto", "--tope-usd", texto], Presupuesto);

        Assert.Equal((decimal)esperado, leidas.Opciones!.Tope);
    }

    [Fact]
    public void LeerOpciones_ElErrorDelTopeDiceElMinimoConElMargen()
    {
        var leidas = ConsolaEvaluacion.LeerOpciones(["--confirmo-gasto", "--tope-usd", "0.5"], Presupuesto);

        Assert.Contains("entre 0,75 y 10", leidas.Error);
    }

    [Theory]
    [InlineData(0, 0.75, true)] // el borde exacto cabe
    [InlineData(0, 0.74, false)]
    [InlineData(4.25, 5, true)]
    [InlineData(4.26, 5, false)]
    [InlineData(4.50, 5, false)] // sin margen habría cabido
    [InlineData(5.10, 5, false)]
    public void CabeOtraOferta_ReservaElPresupuestoYElMargen(double gastado, double tope, bool cabe)
    {
        Assert.Equal(cabe, ConsolaEvaluacion.CabeOtraOferta((decimal)gastado, Presupuesto, (decimal)tope));
    }

    [Fact]
    public void CabeOtraOferta_AunqueCadaOfertaSePaseElMargenEntero_NuncaSePasaDelTope()
    {
        const decimal tope = 5m;
        var gastado = 0m;
        while (ConsolaEvaluacion.CabeOtraOferta(gastado, Presupuesto, tope))
            gastado += Presupuesto + ConsolaEvaluacion.MargenPorOfertaUsd;

        Assert.True(gastado <= tope);
        Assert.Null(ConsolaEvaluacion.AvisoTope(gastado, tope));
    }

    [Fact]
    public void AvisoTope_SoloSiSeHaGastadoMasQueElTope()
    {
        Assert.Null(ConsolaEvaluacion.AvisoTope(5m, 5m));
        var aviso = ConsolaEvaluacion.AvisoTope(5.01m, 5m);
        Assert.Contains("5.01", aviso);
        Assert.Contains("5.00", aviso);
    }

    [Fact]
    public void CarpetaSalida_Elegida_VaSiempreAUnaSubcarpetaConLaFecha()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "radar-eval-elegida");
        var ahora = new DateTime(2026, 11, 25, 9, 5, 7, DateTimeKind.Utc);

        var carpeta = ConsolaEvaluacion.CarpetaSalida(raiz, "no-se-usa", ahora);

        Assert.Equal(Path.Combine(raiz, "20261125-090507"), carpeta);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CarpetaSalida_SinElegir_UsaLaDePorDefectoConLaFecha(string? elegida)
    {
        var porDefecto = Path.Combine(Path.GetTempPath(), "radar-eval-defecto");

        var carpeta = ConsolaEvaluacion.CarpetaSalida(elegida, porDefecto, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        Assert.Equal(Path.Combine(porDefecto, "20260102-030405"), carpeta);
    }

    [Fact]
    public void HashPrompt_NoDependeDeLosFinalesDeLinea()
    {
        var conLf = ConsolaEvaluacion.HashPrompt("uno\ndos\ntres\n");

        Assert.Equal(conLf, ConsolaEvaluacion.HashPrompt("uno\r\ndos\r\ntres\r\n"));
        Assert.Equal(conLf, ConsolaEvaluacion.HashPrompt("uno\rdos\r\ntres\n"));
        Assert.NotEqual(conLf, ConsolaEvaluacion.HashPrompt("uno\ndos\ntres"));
        Assert.Matches("^[0-9a-f]{64}$", conLf);
    }

    [Fact]
    public void ErrorDelConjunto_ConOfertasBuenas_NoDaError()
    {
        var conjunto = new Conjunto([Caso("oferta-01"), Caso("oferta-02", Encaje.No)]);

        Assert.Null(ConsolaEvaluacion.ErrorDelConjunto(conjunto, _ => true));
    }

    [Fact]
    public void ErrorDelConjunto_SinOfertas_LoDice()
    {
        Assert.NotNull(ConsolaEvaluacion.ErrorDelConjunto(null, _ => true));
        Assert.NotNull(ConsolaEvaluacion.ErrorDelConjunto(new Conjunto(null!), _ => true));
        Assert.NotNull(ConsolaEvaluacion.ErrorDelConjunto(new Conjunto([]), _ => true));
        Assert.NotNull(ConsolaEvaluacion.ErrorDelConjunto(new Conjunto([null!]), _ => true));
    }

    [Theory]
    [InlineData("oferta-1")]
    [InlineData("oferta-001")]
    [InlineData("oferta-01\n")]
    [InlineData("..\\oferta-01")]
    [InlineData("oferta-٠١")] // cifras árabes: \d las admitía
    [InlineData("Oferta-01")]
    [InlineData("")]
    [InlineData(null)]
    public void ErrorDelConjunto_ConUnIdRaro_NoLlegaAMirarElDisco(string? id)
    {
        var error = ConsolaEvaluacion.ErrorDelConjunto(new Conjunto([Caso("oferta-01"), Caso(id!)]),
            visto => visto == "oferta-01" ? true : throw new InvalidOperationException("No debía mirar el disco."));

        Assert.StartsWith("Id de oferta no válido", error);
    }

    [Fact]
    public void ErrorDelConjunto_ConUnIdRepetido_LoDice()
    {
        var error = ConsolaEvaluacion.ErrorDelConjunto(new Conjunto([Caso("oferta-01"), Caso("oferta-01")]), _ => true);

        Assert.Contains("repetida", error);
    }

    [Fact]
    public void ErrorDelConjunto_SinEncajeOSinEmpresa_LoDice()
    {
        // Sin «encaje» en el JSON, el enum queda en SinValorar.
        Assert.Contains("encaje", ConsolaEvaluacion.ErrorDelConjunto(new Conjunto([Caso("oferta-01", Encaje.SinValorar)]), _ => true));
        Assert.Contains("empresa", ConsolaEvaluacion.ErrorDelConjunto(new Conjunto([Caso("oferta-01", empresa: " ")]), _ => true));
    }

    [Fact]
    public void ErrorDelConjunto_SiFaltaElArchivoDeUnaOferta_DiceCual()
    {
        var error = ConsolaEvaluacion.ErrorDelConjunto(new Conjunto([Caso("oferta-01"), Caso("oferta-02")]), id => id != "oferta-02");

        Assert.Contains("oferta-02", error);
    }
}
