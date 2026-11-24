using System.Text.Json;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Agentes;

namespace RadarEmpleo.Tests;

/// <summary>
/// Lo de las tareas por partes que no necesita la cola: el reparto del presupuesto y del tiempo
/// (<see cref="DefinicionTarea.PresupuestoParte"/>, <see cref="DefinicionTarea.TiempoParte"/>), <see cref="PreparacionTarea"/>
/// y lo que hace <see cref="IManejadorTarea.AplicarPartesAsync"/> por defecto.
/// </summary>
public class PartesTareaTests
{
    /// <summary>Con los valores de ExtraerRegistro tras la salida corta (24/11): 1,50 y 30 min en total, 0,50 y 5 min como mucho por parte.</summary>
    private static readonly DefinicionTarea PorPartes = DefinicionesTareas.ExtraerRegistro with
    {
        PresupuestoUsd = 1.50m,
        TiempoMaximo = TimeSpan.FromMinutes(30),
        Partes = new PartesTarea(16_000, 6, 0.50m, TimeSpan.FromMinutes(5))
    };

    [Theory]
    [InlineData(1, "0.50", 300)]
    [InlineData(3, "0.50", 300)]
    [InlineData(4, "0.37", 300)]
    [InlineData(5, "0.30", 300)]
    [InlineData(6, "0.25", 300)]
    public void ElRepartoPorParte_TieneSusTopes(int partes, string presupuesto, int segundos)
    {
        Assert.Equal(decimal.Parse(presupuesto, System.Globalization.CultureInfo.InvariantCulture), PorPartes.PresupuestoParte(partes));
        Assert.Equal(TimeSpan.FromSeconds(segundos), PorPartes.TiempoParte(partes));
    }

    [Fact]
    public void LaSumaDeLasPartes_NuncaPasaDelTotal()
    {
        for (var partes = 1; partes <= 6; partes++)
        {
            Assert.True(PorPartes.PresupuestoParte(partes) * partes <= PorPartes.PresupuestoUsd);
            Assert.True(PorPartes.TiempoParte(partes) * partes <= PorPartes.TiempoMaximo);
        }
    }

    [Fact]
    public void SinPartes_CadaParteEsLaTareaEntera()
    {
        var definicion = DefinicionesTareas.AnalizarEncaje;
        Assert.Null(definicion.Partes);
        Assert.Equal(definicion.PresupuestoUsd, definicion.PresupuestoParte(3));
        Assert.Equal(definicion.TiempoMaximo, definicion.TiempoParte(3));
    }

    [Fact]
    public void CeroPartes_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PorPartes.PresupuestoParte(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PorPartes.TiempoParte(0));
    }

    [Fact]
    public void UnRepartoQueNoLlegaAUnCentimo_DiceQueNoDaParaLasPartes()
    {
        var error = Assert.Throws<ReglaNegocioException>(() => (PorPartes with { PresupuestoUsd = 0.05m }).PresupuestoParte(6));
        Assert.Equal(MensajesEjecucion.PresupuestoNoLlegaParaLasPartes, error.Message);
        Assert.Equal(0.01m, (PorPartes with { PresupuestoUsd = 0.06m }).PresupuestoParte(6));
    }

    /// <summary>Unas peticiones con ese tiempo y ese <c>--max-budget-usd</c>.</summary>
    private static List<PeticionClaude> Partes(int numero, int minutos, decimal presupuesto) =>
        Enumerable.Range(1, numero).Select(k => Peticion($"{k} de {numero}") with
        {
            TiempoMaximo = TimeSpan.FromMinutes(minutos),
            Argumentos = ConstructorArgumentos.Construir(PorPartes, """{"type":"object"}""", new ValoresArgumentos(Carpeta), presupuesto)
        }).ToList();

    private static readonly string Carpeta = OperatingSystem.IsWindows() ? @"C:\datos" : "/datos";

    [Fact]
    public void CabenLasPartes_ConElRepartoDeLaTarea()
    {
        Assert.True(PorPartes.CabenLasPartes(Partes(6, 5, 0.25m)));
        Assert.True(PorPartes.CabenLasPartes(Partes(4, 5, 0.37m)));
        // Una sola cabe siempre, como antes de las partes.
        Assert.True(DefinicionesTareas.AnalizarEncaje.CabenLasPartes([Peticion()]));
    }

    [Fact]
    public void CabenLasPartes_NoSiPasanDelTiempoDelPresupuestoODelNumeroOSinPartes()
    {
        // Seis de 6 min con 30 min en total: el bloqueo caducaría antes de acabar.
        Assert.False(PorPartes.CabenLasPartes(Partes(6, 6, 0.25m)));
        // Cuatro de 0,50 con 1,50 en total.
        Assert.False(PorPartes.CabenLasPartes(Partes(4, 5, 0.50m)));
        Assert.False(PorPartes.CabenLasPartes(Partes(7, 5, 0.10m)));
        Assert.False((PorPartes with { Partes = null }).CabenLasPartes(Partes(2, 5, 0.10m)));
        // Sin --max-budget-usd no se sabe lo que puede gastar.
        Assert.False(PorPartes.CabenLasPartes([Peticion("1 de 2"), Peticion("2 de 2")]));
    }

    [Fact]
    public void PresupuestoDe_LeeElDeLosArgumentos()
    {
        Assert.Equal(0.33m, ConstructorArgumentos.PresupuestoDe(Partes(1, 1, 0.33m)[0].Argumentos));
        Assert.Null(ConstructorArgumentos.PresupuestoDe(["-p"]));
        Assert.Null(ConstructorArgumentos.PresupuestoDe(["--max-budget-usd"]));
        Assert.Null(ConstructorArgumentos.PresupuestoDe(["--max-budget-usd", "mucho"]));
    }

    [Fact]
    public void ExtraerRegistro_VaPorPartesConLoAprobado_YLasDemasDeUnaVez()
    {
        // Decisiones de Eduardo del 24/11: por partes y, con la salida corta, topes más bajos.
        var extraer = DefinicionesTareas.ExtraerRegistro;
        Assert.Equal(1.50m, extraer.PresupuestoUsd);
        Assert.Equal(TimeSpan.FromMinutes(30), extraer.TiempoMaximo);
        Assert.Equal(new PartesTarea(16_000, 6, 0.50m, TimeSpan.FromMinutes(5)), extraer.Partes);
        // El bloqueo caduca 5 min después del tiempo máximo: por debajo de la hora en la que cualquiera lo da por caducado.
        Assert.True(extraer.TiempoMaximo + TimeSpan.FromMinutes(5) < BloqueoEjecucion.DuracionMaxima);
        Assert.Null(DefinicionesTareas.AnalizarEncaje.Partes);
        Assert.Null(DefinicionesTareas.Rastreo.Partes);
    }

    private static PeticionClaude Peticion(string? parte = null) =>
        new(TareaAgente.ExtraerRegistro, ["-p"], "carpeta", "prompt", TimeSpan.FromMinutes(1), "log", Parte: parte);

    [Fact]
    public void Preparacion_ConClaudeSinPeticiones_Lanza()
    {
        Assert.Throws<ArgumentException>(() => PreparacionTarea.ConClaude(Array.Empty<PeticionClaude>()));
    }

    [Fact]
    public void Preparacion_GuardaLasPeticionesEnSuOrdenYTerminadaNoTieneNinguna()
    {
        var lista = new List<PeticionClaude> { Peticion("1 de 2"), Peticion("2 de 2") };
        var preparacion = PreparacionTarea.ConClaude(lista);
        // Una copia: cambiar la lista después no cambia lo que se lanza.
        lista.Clear();

        Assert.Equal(["1 de 2", "2 de 2"], preparacion.Peticiones.Select(p => p.Parte));
        Assert.Null(preparacion.SinClaude);
        Assert.Single(PreparacionTarea.ConClaude(Peticion()).Peticiones);
        Assert.Empty(PreparacionTarea.Terminada(new ResultadoTarea("Hecho.", null)).Peticiones);
    }

    [Fact]
    public async Task AplicarPartesPorDefecto_ConUnaParte_EsAplicar()
    {
        IManejadorTarea manejador = new ManejadorDeUnaVez();
        var respuesta = new RespuestaClaude(true, "success", 0.1m, 1, "s", "Hecho.", JsonSerializer.SerializeToElement(new { }), null, null);

        var resultado = await manejador.AplicarPartesAsync(null!, [(Peticion(), respuesta)], CancellationToken.None);

        Assert.Equal("Aplicada.", resultado.Resumen);
    }

    [Fact]
    public async Task AplicarPartesPorDefecto_ConVariasPartes_Lanza()
    {
        IManejadorTarea manejador = new ManejadorDeUnaVez();
        var respuesta = RespuestaClaude.Fallida(FalloClaude.ErrorDeClaude);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manejador.AplicarPartesAsync(null!, [(Peticion(), respuesta), (Peticion(), respuesta)], CancellationToken.None));
    }

    /// <summary>Un manejador que no sabe de partes: usa lo de por defecto.</summary>
    private sealed class ManejadorDeUnaVez : IManejadorTarea
    {
        public DefinicionTarea Definicion => DefinicionesTareas.AnalizarEncaje;

        public Task<bool> ValidarAsync(int? candidaturaId, CancellationToken cancelacion) => Task.FromResult(true);

        public Task<PreparacionTarea> ConstruirAsync(EjecucionAgente ejecucion, ContextoTarea contexto, CancellationToken cancelacion) =>
            throw new NotSupportedException();

        public Task<ResultadoTarea> AplicarAsync(EjecucionAgente ejecucion, RespuestaClaude respuesta, CancellationToken cancelacion) =>
            Task.FromResult(new ResultadoTarea("Aplicada.", null));
    }
}
