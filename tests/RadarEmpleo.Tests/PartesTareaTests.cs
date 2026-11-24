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
    /// <summary>Con los valores aprobados el 24/11 para ExtraerRegistro: 2,00 y 50 min en total, 1,00 y 10 min como mucho por parte.</summary>
    private static readonly DefinicionTarea PorPartes = DefinicionesTareas.ExtraerRegistro with
    {
        PresupuestoUsd = 2.00m,
        TiempoMaximo = TimeSpan.FromMinutes(50),
        Partes = new PartesTarea(16_000, 6, 1.00m, TimeSpan.FromMinutes(10))
    };

    [Theory]
    [InlineData(1, "1.00", 600)]
    [InlineData(2, "1.00", 600)]
    [InlineData(3, "0.66", 600)]
    [InlineData(4, "0.50", 600)]
    [InlineData(6, "0.33", 500)]
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
    public void ExtraerRegistro_TodaviaNoVaPorPartes()
    {
        // Se activa en el PR B, con el manejador que parte el texto libre.
        Assert.All(DefinicionesTareas.Todas, d => Assert.Null(d.Partes));
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
