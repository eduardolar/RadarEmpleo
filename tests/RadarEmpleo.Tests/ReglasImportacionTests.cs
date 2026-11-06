using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Importacion;

namespace RadarEmpleo.Tests;

/// <summary><see cref="ReglasImportacion"/>: clave y comparación con lo guardado.</summary>
public class ReglasImportacionTests
{
    private static readonly DateOnly Dia = new(2026, 9, 1);

    private static FilaLote Fila(string empresa = "Acme S.L.", string puesto = "Backend", DateOnly? fecha = null,
        EstadoCandidatura estado = EstadoCandidatura.Enviada, string? enlace = null, bool aproximada = false) =>
        new(1, fecha ?? Dia, aproximada, empresa, puesto, Estado: estado, Enlace: enlace);

    private static CandidaturaExistente Existente(EstadoCandidatura estado, string? clave = null, int id = 7,
        string empresa = "acme", string puesto = "Backend") =>
        new(id, clave ?? ReglasImportacion.Clave("Acme", "Backend", Dia), empresa, puesto, estado);

    private static FilaVistaPreviaDto Comparar(FilaLote fila, params CandidaturaExistente[] existentes) =>
        Assert.Single(ReglasImportacion.Comparar([fila], existentes));

    [Fact]
    public void Clave_TieneEmpresaYPuestoNormalizadosYLaFecha()
    {
        Assert.Equal("acme|desarrollador .net|2026-09-01", ReglasImportacion.Clave("ACME, S.L.", "Desarrollador .NET", Dia));
    }

    [Theory]
    [InlineData("Consultoría Pérez, S.L.", "Técnico  de  Sistemas")]
    [InlineData("consultoria perez", "tecnico de sistemas")]
    [InlineData("CONSULTORIA PEREZ SL", "TÉCNICO DE SISTEMAS")]
    public void Clave_ConTildesMayusculasYFormaJuridica_NoCambia(string empresa, string puesto)
    {
        Assert.Equal("consultoria perez|tecnico de sistemas|2026-09-01", ReglasImportacion.Clave(empresa, puesto, Dia));
    }

    [Fact]
    public void Clave_ConLaEnie_DistingueComoElRestoDeLaApp()
    {
        Assert.NotEqual(ReglasImportacion.Clave("Peña", "Backend", Dia), ReglasImportacion.Clave("Pena", "Backend", Dia));
    }

    [Fact]
    public void Comparar_SinNadaParecido_EsNueva()
    {
        var fila = Comparar(Fila());

        Assert.Equal(ResultadoFila.Nueva, fila.Resultado);
        Assert.Equal("acme|backend|2026-09-01", fila.Clave);
        Assert.Null(fila.EstadoActual);
        Assert.Null(fila.CandidaturaId);
        Assert.Null(fila.Motivo);
        Assert.Empty(fila.Avisos);
    }

    [Theory]
    [InlineData(EstadoCandidatura.Detectada, EstadoCandidatura.Enviada)]
    [InlineData(EstadoCandidatura.Enviada, EstadoCandidatura.Entrevista)]
    [InlineData(EstadoCandidatura.Enviada, EstadoCandidatura.Descartada)]
    [InlineData(EstadoCandidatura.Oferta, EstadoCandidatura.Cerrada)]
    public void Comparar_MismaClaveConEstadoPosterior_AvanzaEstado(EstadoCandidatura actual, EstadoCandidatura registro)
    {
        var fila = Comparar(Fila(estado: registro), Existente(actual));

        Assert.Equal(ResultadoFila.AvanzaEstado, fila.Resultado);
        Assert.Equal(actual, fila.EstadoActual);
        Assert.Equal(registro, fila.EstadoRegistro);
        Assert.Equal(7, fila.CandidaturaId);
    }

    [Theory]
    [InlineData(EstadoCandidatura.Enviada, EstadoCandidatura.Enviada)]
    [InlineData(EstadoCandidatura.Entrevista, EstadoCandidatura.Enviada)]
    [InlineData(EstadoCandidatura.Oferta, EstadoCandidatura.Detectada)]
    [InlineData(EstadoCandidatura.Descartada, EstadoCandidatura.Descartada)]
    [InlineData(EstadoCandidatura.Cerrada, EstadoCandidatura.Cerrada)]
    public void Comparar_MismaClaveConEstadoIgualOAnterior_YaExisteYNuncaRetrocede(EstadoCandidatura actual, EstadoCandidatura registro)
    {
        var fila = Comparar(Fila(estado: registro), Existente(actual));

        Assert.Equal(ResultadoFila.YaExiste, fila.Resultado);
    }

    [Theory]
    [InlineData(EstadoCandidatura.Descartada, EstadoCandidatura.Enviada)]
    [InlineData(EstadoCandidatura.Descartada, EstadoCandidatura.Cerrada)]
    [InlineData(EstadoCandidatura.Cerrada, EstadoCandidatura.Detectada)]
    [InlineData(EstadoCandidatura.Cerrada, EstadoCandidatura.Oferta)]
    public void Comparar_EnLaBdEnEstadoFinalYOtroEnElRegistro_ConflictoSinReabrir(EstadoCandidatura actual, EstadoCandidatura registro)
    {
        var fila = Comparar(Fila(estado: registro), Existente(actual));

        Assert.Equal(ResultadoFila.Conflicto, fila.Resultado);
        Assert.Contains("no se reabre", fila.Motivo);
        Assert.Equal(7, fila.CandidaturaId);
    }

    [Fact]
    public void Comparar_RepetidaEnElLote_LaPrimeraValeYLaOtraEsConflicto()
    {
        var filas = ReglasImportacion.Comparar([Fila("Acme"), Fila("ACME, S.L.")], []);

        Assert.Equal(ResultadoFila.Nueva, filas[0].Resultado);
        Assert.Equal(ResultadoFila.Conflicto, filas[1].Resultado);
        Assert.Equal(ReglasImportacion.MotivoRepetida, filas[1].Motivo);
        Assert.Equal(filas[0].Clave, filas[1].Clave);
    }

    [Fact]
    public void Comparar_MismaEmpresaYPuestoEnUnaManual_ConflictoPorParecida()
    {
        var fila = Comparar(Fila(), new CandidaturaExistente(3, null, "acme", "BACKEND", EstadoCandidatura.Detectada));

        Assert.Equal(ResultadoFila.Conflicto, fila.Resultado);
        Assert.Equal(ReglasImportacion.MotivoParecida, fila.Motivo);
        Assert.Equal(3, fila.CandidaturaId);
    }

    [Fact]
    public void Comparar_MismaEmpresaYPuestoDeOtraFecha_ConflictoPorParecida()
    {
        var otraFecha = ReglasImportacion.Clave("Acme", "Backend", Dia.AddDays(-30));

        var fila = Comparar(Fila(), Existente(EstadoCandidatura.Enviada, clave: otraFecha));

        Assert.Equal(ResultadoFila.Conflicto, fila.Resultado);
        Assert.Equal(ReglasImportacion.MotivoParecida, fila.Motivo);
    }

    [Fact]
    public void Comparar_MismaEmpresaConOtroPuesto_EsNueva()
    {
        var fila = Comparar(Fila(puesto: "Frontend"), Existente(EstadoCandidatura.Enviada));

        Assert.Equal(ResultadoFila.Nueva, fila.Resultado);
    }

    [Fact]
    public void Comparar_CandidaturaEditadaTrasImportar_SeSigueEncontrandoPorSuClave()
    {
        var clave = ReglasImportacion.Clave("Acme", "Backend", Dia);
        var editada = new CandidaturaExistente(9, clave, "acme", "Puesto cambiado a mano", EstadoCandidatura.Enviada);

        var fila = Comparar(Fila(), editada);

        Assert.Equal(ResultadoFila.YaExiste, fila.Resultado);
        Assert.Equal(9, fila.CandidaturaId);
    }

    [Fact]
    public void Comparar_MismaEmpresaYPuestoEnDosFechasDelLote_LaPrimeraEsNuevaYLaOtraParaRevisar()
    {
        var filas = ReglasImportacion.Comparar(
            [Fila("Mu", "Dev"), Fila("MU S.L.", "dev", fecha: Dia.AddDays(2), estado: EstadoCandidatura.Entrevista)], []);

        Assert.Equal(ResultadoFila.Nueva, filas[0].Resultado);
        Assert.Equal(ResultadoFila.Conflicto, filas[1].Resultado);
        Assert.Equal(ReglasImportacion.MotivoParecidaEnElRegistro, filas[1].Motivo);
        Assert.Null(filas[1].CandidaturaId);
        Assert.NotEqual(filas[0].Clave, filas[1].Clave);
    }

    [Fact]
    public void Comparar_ParecidaEnElLote_DaLoMismoQueSiLaPrimeraYaEstuvieraGuardada()
    {
        var segunda = Fila("Mu", "Dev", fecha: Dia.AddDays(2), estado: EstadoCandidatura.Entrevista);
        var guardada = new CandidaturaExistente(5, ReglasImportacion.Clave("Mu", "Dev", Dia), "mu", "Dev", EstadoCandidatura.Enviada);

        var juntas = ReglasImportacion.Comparar([Fila("Mu", "Dev"), segunda], [])[1];
        var porSeparado = Comparar(segunda, guardada);

        Assert.Equal(porSeparado.Resultado, juntas.Resultado);
        Assert.Equal(ReglasImportacion.MotivoParecida, porSeparado.Motivo);
    }

    [Fact]
    public void Comparar_FechaAproximada_LlevaAviso()
    {
        Assert.Equal([ReglasImportacion.AvisoFechaAproximada], Comparar(Fila(aproximada: true)).Avisos);
    }

    [Fact]
    public void Comparar_EstadoDeducidoDelTexto_LlevaAvisoComoLaFechaAproximada()
    {
        var fila = Comparar(Fila(aproximada: true) with { EstadoDeducido = true });

        Assert.Equal([ReglasImportacion.AvisoFechaAproximada, ReglasImportacion.AvisoEstadoDeducido], fila.Avisos);
        Assert.Equal(ResultadoFila.Nueva, fila.Resultado);
    }

    [Theory]
    [InlineData("mailto:rrhh@ejemplo.com")]
    [InlineData("no es un enlace")]
    public void Comparar_EnlaceNoValido_LlevaAvisoYLaFilaSigueSiendoNueva(string enlace)
    {
        var fila = Comparar(Fila(enlace: enlace));

        Assert.Equal(ResultadoFila.Nueva, fila.Resultado);
        Assert.Equal([ReglasImportacion.AvisoEnlaceNoValido], fila.Avisos);
    }

    [Theory]
    [InlineData("www.ejemplo.com/1", "https://www.ejemplo.com/1")]
    [InlineData("https://ejemplo.com/1", "https://ejemplo.com/1")]
    [InlineData("mailto:rrhh@ejemplo.com", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void EnlaceParaGuardar_NormalizaOQuitaElEnlace(string? enlace, string? esperado)
    {
        Assert.Equal(esperado, ReglasImportacion.EnlaceParaGuardar(enlace));
    }

    [Fact]
    public void EnlaceParaGuardar_QueSePasaDelLimiteAlPonerleHttps_SeQuita()
    {
        Assert.Null(ReglasImportacion.EnlaceParaGuardar($"ejemplo.com/{new string('a', LimitesLote.Enlace - 12)}"));
    }

    [Fact]
    public void Resumir_CuentaCadaResultado()
    {
        var filas = ReglasImportacion.Comparar(
            [
                Fila(puesto: "Nueva"),
                Fila(estado: EstadoCandidatura.Entrevista),
                Fila(puesto: "Igual"),
                Fila(puesto: "Igual")
            ],
            [
                Existente(EstadoCandidatura.Enviada),
                Existente(EstadoCandidatura.Enviada, clave: ReglasImportacion.Clave("Acme", "Igual", Dia), id: 8, puesto: "Igual")
            ]);

        Assert.Equal(new ResumenImportacionDto(1, 1, 1, 1, 2, 3), ReglasImportacion.Resumir(filas, sinLeer: 2, enviosEnTexto: 3));
    }
}
