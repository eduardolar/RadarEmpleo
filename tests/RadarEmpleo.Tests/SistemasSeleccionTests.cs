using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary><see cref="SistemasSeleccion"/>: qué enlaces son de Greenhouse, Lever, Ashby o Workable, y su listado público.</summary>
public class SistemasSeleccionTests
{
    [Theory]
    [InlineData("https://boards.greenhouse.io/empresaficticia/jobs/4000001", SistemaSeleccion.Greenhouse, "empresaficticia", "4000001")]
    [InlineData("https://job-boards.greenhouse.io/empresa-ficticia/jobs/4000001?gh_src=abc#app", SistemaSeleccion.Greenhouse, "empresa-ficticia", "4000001")]
    [InlineData("boards.greenhouse.io/empresaficticia/jobs/12", SistemaSeleccion.Greenhouse, "empresaficticia", "12")]
    [InlineData("https://jobs.lever.co/empresa-ficticia/00000000-0000-4000-8000-000000000001", SistemaSeleccion.Lever, "empresa-ficticia", "00000000-0000-4000-8000-000000000001")]
    [InlineData("https://jobs.lever.co/empresa-ficticia/00000000-0000-4000-8000-000000000001/apply", SistemaSeleccion.Lever, "empresa-ficticia", "00000000-0000-4000-8000-000000000001")]
    [InlineData("https://JOBS.ASHBYHQ.COM/EmpresaFicticia/11111111-2222-4333-8444-555555555555", SistemaSeleccion.Ashby, "EmpresaFicticia", "11111111-2222-4333-8444-555555555555")]
    [InlineData("https://apply.workable.com/empresa-ficticia/j/AB12CD34EF/", SistemaSeleccion.Workable, "empresa-ficticia", "AB12CD34EF")]
    [InlineData("http://apply.workable.com/empresa-ficticia/j/AB12CD34EF/apply/", SistemaSeleccion.Workable, "empresa-ficticia", "AB12CD34EF")]
    public void Detectar_UnEnlaceDeUnSistema_DaLaCuentaYElId(string enlace, SistemaSeleccion sistema, string cuenta, string id)
    {
        Assert.Equal(new OfertaEnSistema(sistema, cuenta, id), SistemasSeleccion.Detectar(enlace));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es un enlace")]
    [InlineData("https://www.linkedin.com/jobs/view/1000000001")]
    [InlineData("https://boards.greenhouse.io/empresaficticia")]
    [InlineData("https://boards.greenhouse.io/empresaficticia/jobs/abc")]
    [InlineData("https://boards.greenhouse.io.ejemplo.es/empresaficticia/jobs/1")]
    [InlineData("https://greenhouse.io.trampa.es/empresaficticia/jobs/1")]
    [InlineData("https://jobs.lever.co/empresa-ficticia")]
    [InlineData("https://jobs.lever.co:8443/empresa-ficticia/00000000-0000-4000-8000-000000000001")]
    [InlineData("https://yo@jobs.lever.co/empresa-ficticia/00000000-0000-4000-8000-000000000001")]
    [InlineData("https://jobs.lever.co/empresa%2Fotra/00000000-0000-4000-8000-000000000001")]
    [InlineData("https://jobs.lever.co/empresa-ficticia/%3Fmode=html")]
    [InlineData("https://jobs.lever.co/empresa-ficticia/analista-datos")]
    [InlineData("https://jobs.lever.co/empresa-ficticia/00000000-0000-4000-8000-00000000000")]
    [InlineData("https://jobs.ashbyhq.com/empresa-ficticia/12345")]
    [InlineData("https://jobs.ashbyhq.com/empresa-ficticia/zzzzzzzz-2222-4333-8444-555555555555")]
    [InlineData("https://jobs.lever.co.ejemplo.es/empresa-ficticia/00000000-0000-4000-8000-000000000001")]
    [InlineData("https://eu.jobs.ashbyhq.com/empresa-ficticia/11111111-2222-4333-8444-555555555555")]
    [InlineData("https://jobs.ashbyhq.com/empresa-ficticia")]
    [InlineData("https://apply.workable.com/empresa-ficticia/AB12CD34EF")]
    [InlineData("mailto:rrhh@ejemplo.es")]
    public void Detectar_OtroEnlace_DaNull(string? enlace)
    {
        Assert.Null(SistemasSeleccion.Detectar(enlace));
    }

    [Theory]
    [InlineData(SistemaSeleccion.Greenhouse, "https://boards-api.greenhouse.io/v1/boards/empresa-ficticia/jobs")]
    [InlineData(SistemaSeleccion.Lever, "https://api.lever.co/v0/postings/empresa-ficticia?mode=json")]
    [InlineData(SistemaSeleccion.Ashby, "https://api.ashbyhq.com/posting-api/job-board/empresa-ficticia")]
    [InlineData(SistemaSeleccion.Workable, "https://apply.workable.com/api/v1/widget/accounts/empresa-ficticia")]
    public void UrlListado_EsElHostFijoDeCadaSistema(SistemaSeleccion sistema, string url)
    {
        Assert.Equal(url, new OfertaEnSistema(sistema, "empresa-ficticia", "1").UrlListado);
    }
}
