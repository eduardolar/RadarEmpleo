using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary>Nombre de la carpeta semanal de <c>Candidaturas\</c> (<see cref="CarpetasSemana"/>).</summary>
public class CarpetasSemanaTests
{
    /// <summary>Las 9 carpetas que había en <c>Curriculum\Candidaturas\</c> el 06/11/2026, copiadas tal cual.</summary>
    public static readonly TheoryData<string> NombresReales =
    [
        "Semana 2026-08-31 (31 ago - 6 sep)",
        "Semana 2026-09-07 (7 - 13 sep)",
        "Semana 2026-09-14 (14 - 20 sep)",
        "Semana 2026-09-28 (28 sep - 4 oct)",
        "Semana 2026-10-05 (5 - 11 oct)",
        "Semana 2026-10-12 (12 - 18 oct)",
        "Semana 2026-10-19 (19 - 25 oct)",
        "Semana 2026-10-26 (26 oct - 1 nov)",
        "Semana 2026-11-02 (2 - 8 nov)"
    ];

    [Theory]
    [InlineData(2026, 11, 6, "Semana 2026-11-02 (2 - 8 nov)")]
    [InlineData(2026, 10, 29, "Semana 2026-10-26 (26 oct - 1 nov)")]
    [InlineData(2026, 9, 4, "Semana 2026-08-31 (31 ago - 6 sep)")]
    public void Nombre_DiaCualquiera_DevuelveLaCarpetaDeSuSemana(int anio, int mes, int dia, string esperado)
    {
        Assert.Equal(esperado, CarpetasSemana.Nombre(new DateOnly(anio, mes, dia)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public void Nombre_LunesYDomingo_DanLaMismaCarpeta(int dia)
    {
        Assert.Equal("Semana 2026-11-02 (2 - 8 nov)", CarpetasSemana.Nombre(new DateOnly(2026, 11, dia)));
    }

    [Theory]
    [InlineData(2026, 12, 31)]
    [InlineData(2027, 1, 3)]
    public void Nombre_CambioDeAnio_UsaElAnioDelLunesYSinAnioEntreParentesis(int anio, int mes, int dia)
    {
        Assert.Equal("Semana 2026-12-28 (28 dic - 3 ene)", CarpetasSemana.Nombre(new DateOnly(anio, mes, dia)));
    }

    [Fact]
    public void Nombre_LunesTreintaYUnoDeDiciembre_EmpiezaEseLunes()
    {
        Assert.Equal("Semana 2029-12-31 (31 dic - 6 ene)", CarpetasSemana.Nombre(new DateOnly(2029, 12, 31)));
    }

    [Fact]
    public void Nombre_Septiembre_SeAbreviaSepYNoSept()
    {
        Assert.Equal("Semana 2026-09-07 (7 - 13 sep)", CarpetasSemana.Nombre(new DateOnly(2026, 9, 10)));
    }

    [Fact]
    public void Nombre_InstanteUtcQueEnMadridYaEsLunes_DaLaSemanaSiguiente()
    {
        // Domingo 08/11 a las 23:30 UTC: en Madrid (UTC+1) ya es el lunes 09/11 a las 00:30.
        var utc = new DateTime(2026, 11, 8, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal("Semana 2026-11-09 (9 - 15 nov)", CarpetasSemana.Nombre(utc));
    }

    [Fact]
    public void Nombre_InstanteUtcDelDomingoPorLaTarde_SigueEnSuSemana()
    {
        var utc = new DateTime(2026, 11, 8, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Semana 2026-11-02 (2 - 8 nov)", CarpetasSemana.Nombre(utc));
    }

    [Theory]
    [MemberData(nameof(NombresReales))]
    public void Nombre_CarpetasReales_ReconstruyeExactamenteElMismoNombre(string nombreReal)
    {
        var lunes = DateOnly.ParseExact(nombreReal.Substring("Semana ".Length, 10), "yyyy-MM-dd");

        Assert.Equal(nombreReal, CarpetasSemana.Nombre(lunes));
        Assert.Equal(nombreReal, CarpetasSemana.Nombre(lunes.AddDays(6)));
    }

    [Theory]
    [MemberData(nameof(NombresReales))]
    public void Lunes_CarpetasReales_DevuelveElLunesDelNombre(string nombreReal)
    {
        var lunes = CarpetasSemana.Lunes(nombreReal);

        Assert.NotNull(lunes);
        Assert.Equal(DayOfWeek.Monday, lunes.Value.DayOfWeek);
        Assert.Equal(nombreReal, CarpetasSemana.Nombre(lunes.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("_estilo_antiguo")]
    [InlineData("Semana pasada")]
    [InlineData("Semana 2026-13-45 (x)")]
    public void Lunes_NombreQueNoEsDeSemana_DevuelveNull(string? nombre)
    {
        Assert.Null(CarpetasSemana.Lunes(nombre));
    }
}
