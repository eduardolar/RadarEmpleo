using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary>Máquina de estados de <see cref="EjecucionAgente"/> (sin BD).</summary>
public class EjecucionAgenteTests
{
    private static readonly DateTime Ahora = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

    private static EjecucionAgente Nueva() =>
        EjecucionAgente.Nueva(TareaAgente.AnalizarEncaje, """{"candidaturaId":12}""", 0.50m, 300, null, Ahora, 12);

    private static EjecucionAgente EnCurso()
    {
        var e = Nueva();
        e.Empezar(Ahora.AddSeconds(1), "aaa", "ppp");
        return e;
    }

    private static EjecucionAgente Terminada(EstadoEjecucion estado)
    {
        var e = EnCurso();
        switch (estado)
        {
            case EstadoEjecucion.Ok: e.Terminar(Ahora, 0.1m, 2, "s", "Hecho", "{}", "aaa", "ppp"); break;
            case EstadoEjecucion.Error: e.Fallar(Ahora, "Mal"); break;
            case EstadoEjecucion.Cancelada: e.PedirCancelacion(Ahora); e.ConfirmarCancelacion(Ahora, null, "aaa", "ppp"); break;
        }
        return e;
    }

    [Fact]
    public void Nueva_QuedaEnColaSinMotivoDeEspera()
    {
        var e = Nueva();
        Assert.Equal(EstadoEjecucion.EnCola, e.Estado);
        Assert.Null(e.Espera);
        Assert.Null(e.QuienBloquea);
        Assert.Equal(Ahora, e.FechaAlta);
        Assert.Equal(12, e.CandidaturaId);
        Assert.Null(e.Inicio);
        Assert.False(e.EsTerminal);
    }

    [Fact]
    public void Nueva_ConPresupuestoCero_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EjecucionAgente.Nueva(TareaAgente.Rastreo, "{}", 0m, 60, null, Ahora));
    }

    [Fact]
    public void Esperar_PorBloqueo_ApuntaQuien()
    {
        var e = Nueva();
        e.Esperar(MotivoEspera.BloqueoOcupado, "la tarea de los lunes");
        Assert.Equal(MotivoEspera.BloqueoOcupado, e.Espera);
        Assert.Equal("la tarea de los lunes", e.QuienBloquea);

        e.Esperar(MotivoEspera.Turno, "da igual");
        Assert.Null(e.QuienBloquea);
    }

    [Fact]
    public void Empezar_DesdeEnCola_PasaAEnCursoConLosHashes()
    {
        var e = Nueva();
        e.Esperar(MotivoEspera.BloqueoOcupado, "/empleo");
        e.Empezar(Ahora.AddMinutes(1), "aaa", "ppp");

        Assert.Equal(EstadoEjecucion.EnCurso, e.Estado);
        Assert.Null(e.Espera);
        Assert.Null(e.QuienBloquea);
        Assert.Equal(Ahora.AddMinutes(1), e.Inicio);
        Assert.Equal("aaa", e.HashRegistroAntes);
        Assert.Equal("ppp", e.HashPerfilAntes);
    }

    [Fact]
    public void AnotarProceso_EnCurso_GuardaPidEInicio()
    {
        var e = EnCurso();
        e.AnotarProceso(4321, Ahora);
        Assert.Equal(4321, e.Pid);
        Assert.Equal(Ahora, e.InicioProceso);
    }

    [Fact]
    public void AnotarProceso_EnCola_Lanza()
    {
        Assert.Throws<ReglaNegocioException>(() => Nueva().AnotarProceso(1, Ahora));
    }

    [Fact]
    public void Terminar_DesdeEnCurso_QuedaOkConTodo()
    {
        var e = EnCurso();
        e.Terminar(Ahora.AddMinutes(2), 0.18m, 4, "sesion", "Encaje medio.", """{"ok":true}""", "aaa", "ppp");

        Assert.Equal(EstadoEjecucion.Ok, e.Estado);
        Assert.True(e.EsTerminal);
        Assert.Equal(Ahora.AddMinutes(2), e.Fin);
        Assert.Equal(0.18m, e.CosteUsd);
        Assert.Equal(4, e.Turnos);
        Assert.Equal("sesion", e.SesionClaude);
        Assert.Equal("Encaje medio.", e.Resumen);
        Assert.Equal("""{"ok":true}""", e.ResultadoJson);
        Assert.False(e.RegistroCambiado);
        Assert.False(e.PerfilCambiado);
    }

    [Fact]
    public void Terminar_ConResumenLargo_LoRecorta()
    {
        var e = EnCurso();
        e.Terminar(Ahora, null, null, null, new string('x', 900), null, null, null);
        Assert.Equal(EjecucionAgente.LargoResumen, e.Resumen!.Length);
    }

    [Fact]
    public void Terminar_DesdeEnCola_Lanza()
    {
        Assert.Throws<ReglaNegocioException>(() => Nueva().Terminar(Ahora, null, null, null, "x", null, null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fallar_DesdeEnColaOEnCurso_QuedaError(bool empezada)
    {
        var e = empezada ? EnCurso() : Nueva();
        e.Fallar(Ahora, "Has llegado al tope.", 0.05m);
        Assert.Equal(EstadoEjecucion.Error, e.Estado);
        Assert.Equal("Has llegado al tope.", e.Error);
        Assert.Equal(0.05m, e.CosteUsd);
        Assert.Null(e.Espera);
    }

    [Fact]
    public void PedirCancelacion_EnCola_QuedaCanceladaAlMomento()
    {
        var e = Nueva();
        e.PedirCancelacion(Ahora);
        Assert.Equal(EstadoEjecucion.Cancelada, e.Estado);
        Assert.Equal(Ahora, e.Fin);
    }

    [Fact]
    public void PedirCancelacion_EnCurso_SoloLaMarca()
    {
        var e = EnCurso();
        e.PedirCancelacion(Ahora);
        e.PedirCancelacion(Ahora);
        Assert.Equal(EstadoEjecucion.EnCurso, e.Estado);
        Assert.True(e.CancelacionPedida);

        e.ConfirmarCancelacion(Ahora.AddSeconds(3), null, "aaa", "ppp");
        Assert.Equal(EstadoEjecucion.Cancelada, e.Estado);
        Assert.Null(e.CosteUsd);
    }

    [Fact]
    public void ConfirmarCancelacion_SinPedirla_Lanza()
    {
        Assert.Throws<ReglaNegocioException>(() => EnCurso().ConfirmarCancelacion(Ahora, null, null, null));
    }

    [Fact]
    public void Interrumpir_EnCurso_QuedaErrorConSuMensaje()
    {
        var e = EnCurso();
        e.Interrumpir(Ahora);
        Assert.Equal(EstadoEjecucion.Error, e.Estado);
        Assert.Equal(EjecucionAgente.MensajeInterrumpida, e.Error);
    }

    [Fact]
    public void Interrumpir_EnCola_QuedaCancelada()
    {
        var e = Nueva();
        e.Interrumpir(Ahora);
        Assert.Equal(EstadoEjecucion.Cancelada, e.Estado);
        Assert.Equal(EjecucionAgente.MensajeReiniciadaAntesDeEmpezar, e.Resumen);
    }

    public static TheoryData<EstadoEjecucion> Terminales => [EstadoEjecucion.Ok, EstadoEjecucion.Error, EstadoEjecucion.Cancelada];

    [Theory]
    [MemberData(nameof(Terminales))]
    public void DesdeUnEstadoTerminal_NadaSePuede(EstadoEjecucion estado)
    {
        var e = Terminada(estado);
        Assert.Equal(estado, e.Estado);
        Assert.Throws<ReglaNegocioException>(() => e.Empezar(Ahora, null, null));
        Assert.Throws<ReglaNegocioException>(() => e.Esperar(MotivoEspera.Turno, null));
        Assert.Throws<ReglaNegocioException>(() => e.AnotarProceso(1, Ahora));
        Assert.Throws<ReglaNegocioException>(() => e.Terminar(Ahora, null, null, null, "x", null, null, null));
        Assert.Throws<ReglaNegocioException>(() => e.Fallar(Ahora, "x"));
        Assert.Throws<ReglaNegocioException>(() => e.ConfirmarCancelacion(Ahora, null, null, null));
        Assert.Throws<ReglaNegocioException>(() => e.Interrumpir(Ahora));
        var cancelar = Assert.Throws<ReglaNegocioException>(() => e.PedirCancelacion(Ahora));
        Assert.Equal("Ya ha terminado.", cancelar.Message);
        Assert.Equal(estado, e.Estado);
    }

    [Fact]
    public void Empezar_DesdeOk_Lanza()
    {
        Assert.Throws<ReglaNegocioException>(() => Terminada(EstadoEjecucion.Ok).Empezar(Ahora, null, null));
    }

    [Theory]
    [InlineData("aaa", "aaa", false)]
    [InlineData("aaa", null, false)]
    [InlineData("aaa", "bbb", true)]
    [InlineData(null, "bbb", false)]
    [InlineData("aaa", "ausente", true)]
    [InlineData("ausente", "bbb", true)]
    public void RegistroCambiado_SoloSiElHashDeDespuesEsOtro(string? antes, string? despues, bool cambiado)
    {
        var e = Nueva();
        e.Empezar(Ahora, antes, antes);
        e.Terminar(Ahora, null, null, null, "x", null, despues, despues);
        Assert.Equal(cambiado, e.RegistroCambiado);
        Assert.Equal(cambiado, e.PerfilCambiado);
    }
}
