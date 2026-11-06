using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary>Efectos de <see cref="Candidatura.CambiarEstado"/> sobre la propia entidad (sin BD).</summary>
public class CandidaturaTests
{
    private static readonly DateTime Ahora = new(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);

    private static Candidatura Nueva() =>
        Candidatura.Nueva(new Empresa { Nombre = "Acme Software", NombreNormalizado = "acme software" },
            "Desarrollador .NET", Ahora, Origen.Manual);

    [Fact]
    public void Nueva_SinMas_EmpiezaEnDetectadaConElAltaEnElHistorial()
    {
        var c = Nueva();

        Assert.Equal(EstadoCandidatura.Detectada, c.Estado);
        var alta = Assert.Single(c.Historial);
        Assert.Null(alta.De);
        Assert.Equal(EstadoCandidatura.Detectada, alta.A);
        Assert.Equal(Ahora, c.FechaDeteccion);
        Assert.Equal(Ahora, c.FechaEstado);
    }

    [Fact]
    public void Nueva_ConFechaDeteccionAnterior_FechaEstadoEsLaDeteccion()
    {
        var detectada = Ahora.AddDays(-5);

        var c = Candidatura.Nueva(new Empresa { Nombre = "Acme Software", NombreNormalizado = "acme software" },
            "Desarrollador .NET", Ahora, Origen.Registro, detectada);

        Assert.Equal(detectada, c.FechaDeteccion);
        Assert.Equal(detectada, c.FechaEstado);     // lleva en «Detectada» desde que se detectó
        Assert.Equal(Ahora, c.FechaUltimoCambio);
        Assert.Equal(Ahora, Assert.Single(c.Historial).Fecha);
    }

    [Fact]
    public void CambiarEstado_CualquierCambio_ActualizaFechaEstadoYFechaUltimoCambio()
    {
        var c = Nueva();
        var envio = Ahora.AddDays(2);
        var entrevista = Ahora.AddDays(9);

        c.CambiarEstado(EstadoCandidatura.Enviada, envio, Origen.Manual);
        Assert.Equal(envio, c.FechaEstado);

        c.CambiarEstado(EstadoCandidatura.Entrevista, entrevista, Origen.Manual);
        Assert.Equal(entrevista, c.FechaEstado);
        Assert.Equal(entrevista, c.FechaUltimoCambio);
    }

    [Fact]
    public void CambiarEstado_AEnviada_RellenaFechaEnvioYCreaSeguimientoALas9DeMadridSieteDiasDespues()
    {
        var c = Nueva();
        var envio = Ahora.AddDays(2); // jueves 29/10/2026 10:00 UTC (11:00 en Madrid, horario de invierno)

        c.CambiarEstado(EstadoCandidatura.Enviada, envio, Origen.Manual);

        Assert.Equal(EstadoCandidatura.Enviada, c.Estado);
        Assert.Equal(envio, c.FechaEnvio);
        Assert.Equal(envio, c.FechaUltimoCambio);
        var seguimiento = Assert.Single(c.Recordatorios);
        Assert.Equal(TipoRecordatorio.Seguimiento, seguimiento.Tipo);
        Assert.Equal(new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc), seguimiento.FechaHora); // 09:00 Madrid
        Assert.Equal(DateTimeKind.Utc, seguimiento.FechaHora.Kind);
        Assert.False(seguimiento.Hecho);
        Assert.Equal("Seguimiento: Desarrollador .NET en Acme Software", seguimiento.Titulo);
    }

    [Fact]
    public void CambiarEstado_AEnviadaSinProgramarSeguimiento_RellenaFechaEnvioSinRecordatorio()
    {
        var c = Nueva();

        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Registro, programarSeguimiento: false);

        Assert.Equal(Ahora, c.FechaEnvio);
        Assert.Empty(c.Recordatorios);
    }

    [Theory]
    [InlineData(EstadoCandidatura.Entrevista)]
    [InlineData(EstadoCandidatura.Oferta)]
    public void CambiarEstado_AEntrevistaUOfertaSinPasarPorEnviada_RellenaFechaEnvioSinSeguimiento(EstadoCandidatura estado)
    {
        var c = Nueva();
        var ahora = Ahora.AddHours(5);

        c.CambiarEstado(estado, ahora, Origen.Manual);

        Assert.Equal(ahora, c.FechaEnvio);
        Assert.Empty(c.Recordatorios);
    }

    [Fact]
    public void CambiarEstado_DeEnviadaAEntrevista_NoTocaLaFechaDeEnvio()
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Manual);

        c.CambiarEstado(EstadoCandidatura.Entrevista, Ahora.AddDays(3), Origen.Manual);

        Assert.Equal(Ahora, c.FechaEnvio);
    }

    [Fact]
    public void CambiarEstado_EnviadaADescartada_DaPorHechoElSeguimiento()
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Manual);

        c.CambiarEstado(EstadoCandidatura.Descartada, Ahora.AddDays(1), Origen.Manual, MotivoDescarte.Salario);

        Assert.True(Assert.Single(c.Recordatorios).Hecho);
    }

    [Fact]
    public void CambiarEstado_EnviadaACerrada_DaPorHechoElSeguimiento()
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Manual);

        c.CambiarEstado(EstadoCandidatura.Cerrada, Ahora.AddDays(1), Origen.Manual);

        Assert.True(Assert.Single(c.Recordatorios).Hecho);
    }

    [Fact]
    public void CambiarEstado_EnviadaDescartadaReabiertaYEnviadaOtraVez_UnSoloSeguimientoPendienteYNuevaFechaDeEnvio()
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Manual);
        c.CambiarEstado(EstadoCandidatura.Descartada, Ahora.AddDays(1), Origen.Manual, MotivoDescarte.Zona);
        c.CambiarEstado(EstadoCandidatura.Detectada, Ahora.AddDays(2), Origen.Manual);
        var reenvio = Ahora.AddDays(3);

        c.CambiarEstado(EstadoCandidatura.Enviada, reenvio, Origen.Manual);

        Assert.Equal(reenvio, c.FechaEnvio);
        Assert.Equal(2, c.Recordatorios.Count);
        var pendiente = Assert.Single(c.Recordatorios, r => !r.Hecho);
        Assert.Equal(ReglasEstado.FechaSeguimiento(reenvio), pendiente.FechaHora);
    }

    [Fact]
    public void CambiarEstado_AEnviadaConUnSeguimientoYaPendiente_NoCreaOtro()
    {
        var c = Nueva();
        c.Recordatorios.Add(new Recordatorio { Tipo = TipoRecordatorio.Seguimiento, Titulo = "Ya puesto", FechaHora = Ahora.AddDays(1) });

        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Manual);

        Assert.Equal("Ya puesto", Assert.Single(c.Recordatorios).Titulo);
    }

    [Fact]
    public void CambiarEstado_DeDescartadaACerrada_BorraElMotivo()
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Descartada, Ahora, Origen.Manual, MotivoDescarte.Ingles, "Piden C1");

        c.CambiarEstado(EstadoCandidatura.Cerrada, Ahora.AddHours(1), Origen.Manual);

        Assert.Equal(EstadoCandidatura.Cerrada, c.Estado);
        Assert.Null(c.MotivoDescarte);
        Assert.Null(c.MotivoDescarteTexto);
    }

    [Fact]
    public void CambiarEstado_EstadoQueNoExiste_LanzaValidacionYNoTocaNada()
    {
        var c = Nueva();

        var ex = Assert.Throws<ValidacionException>(() => c.CambiarEstado((EstadoCandidatura)99, Ahora, Origen.Manual));

        Assert.Equal("estado", ex.Campo);
        Assert.Equal(EstadoCandidatura.Detectada, c.Estado);
        Assert.Single(c.Historial);
    }

    [Fact]
    public void CambiarEstado_Retroceso_LanzaYNoTocaNada()
    {
        var c = Nueva();
        c.CambiarEstado(EstadoCandidatura.Enviada, Ahora, Origen.Manual);

        Assert.Throws<ReglaNegocioException>(() => c.CambiarEstado(EstadoCandidatura.Preparada, Ahora, Origen.Manual));

        Assert.Equal(EstadoCandidatura.Enviada, c.Estado);
        Assert.Equal(2, c.Historial.Count); // alta + envío, sin el intento fallido
    }

    [Fact]
    public void CambiarEstado_DescartarYReabrir_GuardaYLuegoBorraElMotivo()
    {
        var c = Nueva();

        c.CambiarEstado(EstadoCandidatura.Descartada, Ahora, Origen.Manual, MotivoDescarte.Ingles, "Piden C1");
        Assert.Equal(MotivoDescarte.Ingles, c.MotivoDescarte);
        Assert.Equal("Piden C1", c.MotivoDescarteTexto);

        c.CambiarEstado(EstadoCandidatura.Detectada, Ahora.AddHours(1), Origen.Manual, comentario: "Reabierta");
        Assert.Equal(EstadoCandidatura.Detectada, c.Estado);
        Assert.Null(c.MotivoDescarte);
        Assert.Null(c.MotivoDescarteTexto);

        var ultimo = c.Historial.Last();
        Assert.Equal(EstadoCandidatura.Descartada, ultimo.De);
        Assert.Equal(EstadoCandidatura.Detectada, ultimo.A);
        Assert.Equal("Reabierta", ultimo.Comentario);
    }
}
