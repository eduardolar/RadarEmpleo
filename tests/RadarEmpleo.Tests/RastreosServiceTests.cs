using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Domain;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Rastreos;
using RadarEmpleo.Tests.Api;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="RastreosService"/> sobre SQLite en memoria y una carpeta Curriculum temporal con los datos de
/// ejemplo: la sección «## Rastreo 03/11» de <c>registro-ejemplo.md</c> y <c>rastreos\rastreo_2026-11-09.md</c>.
/// </summary>
public sealed class RastreosServiceTests : ArnesSqliteTests, IDisposable
{
    private const string EnlaceUno = "https://ejemplo.com/ofertas/2001";
    private const string EnlaceTalleres = "https://ejemplo.com/ofertas/1001";
    private const string EnlaceTres = "https://ejemplo.com/ofertas/2004";
    private const string EnlaceLunesUno = "https://ejemplo.com/ofertas/3001";

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"radar-rastreos-{Guid.NewGuid():N}");
    private readonly RutasCurriculum _rutas;
    private readonly CandidaturasService _candidaturas;
    private readonly RastreosService _rastreos;

    public RastreosServiceTests()
    {
        // Domingo 08/11/2026 por la mañana en Madrid: «03/11» es de este año.
        Reloj.AhoraUtc = new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
        FabricaApi.CopiarDatosEjemplo(_carpeta);
        _rutas = new RutasCurriculum(_carpeta);
        File.SetLastWriteTimeUtc(Registro, Reloj.AhoraUtc);
        _candidaturas = new CandidaturasService(Factory, Reloj, Exportador, _rutas);
        _rastreos = NuevoServicio(Factory);
    }

    void IDisposable.Dispose()
    {
        Directory.Delete(_carpeta, recursive: true);
        Dispose();
    }

    private RastreosService NuevoServicio(TestDbContextFactory factory) =>
        new(factory, new LectorCurriculum(_rutas), _rutas, new CandidaturasService(factory, Reloj, Exportador, _rutas), Reloj);

    private string Registro => _rutas.Registro!;

    private async Task<OfertaRastreo> OfertaAsync(string enlace)
    {
        await using var db = CreateContext();
        return await db.OfertasRastreo.Include(o => o.Rastreo).SingleAsync(o => o.Enlace == enlace);
    }

    private static (int Nuevos, int Actualizados, int Ofertas, int YaEnElTablero, int SinLeer) Cuentas(ResultadoEscaneoDto r) =>
        (r.RastreosNuevos, r.RastreosActualizados, r.OfertasNuevas, r.YaEnElTablero, r.LineasSinLeer);

    private static string HashArchivo(string ruta) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ruta)));

    // ---------------------------------------------------------------- escanear

    [Fact]
    public async Task Escanear_DatosDeEjemplo_CreaLosDosRastreosYSusOfertas()
    {
        var resultado = await _rastreos.EscanearAsync();

        // 6 del registro y 2 del informe del lunes (su tercera oferta es la 2001, que ya estaba).
        Assert.Equal((2, 0, 8, 0, 3), Cuentas(resultado));
        Assert.Empty(resultado.Avisos);

        await using var db = CreateContext();
        var rastreos = await db.Rastreos.Include(r => r.Ofertas).OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(["registro:2026-11-03:rastreo 03/11 (martes, ejemplo inventado)", "rastreos/rastreo_2026-11-09.md"],
            rastreos.Select(r => r.Clave));
        Assert.Equal(new DateOnly(2026, 11, 3), rastreos[0].Fecha);
        Assert.Equal(OrigenRastreo.Registro, rastreos[0].Origen);
        Assert.Equal(RutasCurriculum.ArchivoRegistro, rastreos[0].RutaInforme);
        Assert.Equal(OrigenRastreo.TareaLunes, rastreos[1].Origen);
        Assert.Equal(Reloj.AhoraUtc, rastreos[0].FechaEscaneo);
        Assert.StartsWith("Rastreo de prueba", rastreos[0].TextoInforme);
        Assert.Equal(6, rastreos[0].Ofertas.Count);
        Assert.Equal(2, rastreos[1].Ofertas.Count);

        var uno = rastreos[0].Ofertas.Single(o => o.Enlace == EnlaceUno);
        Assert.Equal(EstadoOfertaRastreo.Pendiente, uno.Estado);
        Assert.Equal(Encaje.Alto, uno.Encaje);
        Assert.Equal("piden algo de Azure.", uno.Pega);
        Assert.Equal(EstadoOfertaRastreo.Ignorada, rastreos[0].Ofertas.Single(o => o.Enlace == EnlaceTres).Estado);
    }

    [Fact]
    public async Task Escanear_DosVeces_LaSegundaNoCreaNiCambiaNada()
    {
        await _rastreos.EscanearAsync();
        Reloj.Avanzar(TimeSpan.FromHours(1));

        var segundo = await _rastreos.EscanearAsync();

        Assert.Equal((0, 0, 0, 0, 3), Cuentas(segundo));
        await using var db = CreateContext();
        Assert.Equal(2, await db.Rastreos.CountAsync());
        Assert.Equal(8, await db.OfertasRastreo.CountAsync());
        Assert.All(await db.Rastreos.ToListAsync(), r => Assert.Equal(AhoraEscaneo, r.FechaEscaneo));
    }

    private static readonly DateTime AhoraEscaneo = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Escanear_OfertaConElEnlaceDeUnaCandidatura_SaleConvertidaYEnlazada()
    {
        var candidatura = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Talleres Ficticios S.L.", "Desarrollador .NET",
            "https://EJEMPLO.com/ofertas/1001/"));

        var resultado = await _rastreos.EscanearAsync();

        Assert.Equal(1, resultado.YaEnElTablero);
        var oferta = await OfertaAsync(EnlaceTalleres);
        Assert.Equal(EstadoOfertaRastreo.Convertida, oferta.Estado);
        Assert.Equal(candidatura.Id, oferta.CandidaturaId);
    }

    [Fact]
    public async Task Escanear_SeccionQueCrece_AnadeSoloLoNuevoYNoTocaElEstadoDeLasDemas()
    {
        await _rastreos.EscanearAsync();
        var uno = await OfertaAsync(EnlaceUno);
        await _rastreos.IgnorarAsync(uno.Id);
        File.AppendAllText(Registro, "- **Alto · Empresa Nueva — Backend Junior** https://ejemplo.com/ofertas/2099\n");
        Reloj.Avanzar(TimeSpan.FromHours(2));

        var resultado = await _rastreos.EscanearAsync();

        Assert.Equal((0, 1, 1, 0, 3), Cuentas(resultado));
        Assert.Equal(EstadoOfertaRastreo.Ignorada, (await OfertaAsync(EnlaceUno)).Estado);
        var nueva = await OfertaAsync("https://ejemplo.com/ofertas/2099");
        Assert.Equal(uno.RastreoId, nueva.RastreoId);
        Assert.Contains("Empresa Nueva", nueva.Rastreo.TextoInforme);
        Assert.Equal(Reloj.AhoraUtc, nueva.Rastreo.FechaEscaneo);
        // La fecha se fija la primera vez: no cambia aunque cambie el día en que se lee.
        Assert.Equal(new DateOnly(2026, 11, 3), nueva.Rastreo.Fecha);
    }

    [Fact]
    public async Task Escanear_OfertaBorradaDelRegistro_SeQuedaEnLaBandeja()
    {
        await _rastreos.EscanearAsync();
        File.WriteAllText(Registro, File.ReadAllText(Registro).Replace(EnlaceUno, "https://ejemplo.com/ofertas/otra"));

        await _rastreos.EscanearAsync();

        await using var db = CreateContext();
        Assert.True(await db.OfertasRastreo.AnyAsync(o => o.Enlace == EnlaceUno));
    }

    [Fact]
    public async Task Escanear_NoCambiaElRegistroNiLosInformes()
    {
        var informe = Path.Combine(_carpeta, "rastreos", "rastreo_2026-11-09.md");
        var antes = (HashArchivo(Registro), HashArchivo(informe), File.GetLastWriteTimeUtc(Registro));

        await _rastreos.EscanearAsync();
        await _rastreos.DetectarAsync((await OfertaAsync(EnlaceUno)).Id);

        Assert.Equal(antes, (HashArchivo(Registro), HashArchivo(informe), File.GetLastWriteTimeUtc(Registro)));
    }

    [Fact]
    public async Task Escanear_SinCarpeta_LanzaReglaNegocioConElMensajeDeSiempre()
    {
        var rutas = new RutasCurriculum(null);
        var servicio = new RastreosService(Factory, new LectorCurriculum(rutas), rutas, _candidaturas, Reloj);

        var error = await Assert.ThrowsAsync<ReglaNegocioException>(servicio.EscanearAsync);

        Assert.Equal(RutasCurriculum.MensajeSinCarpeta, error.Message);
    }

    [Fact]
    public async Task Escanear_SinRegistro_AvisaYLeeLosInformesDelLunes()
    {
        File.Delete(Registro);

        var resultado = await _rastreos.EscanearAsync();

        Assert.Equal(1, resultado.RastreosNuevos);
        Assert.Equal(3, resultado.OfertasNuevas);
        Assert.Contains(resultado.Avisos, a => a.Contains(RutasCurriculum.ArchivoRegistro));
    }

    [Fact]
    public async Task Escanear_InformeJson_SeAvisaYNoSeLee()
    {
        File.WriteAllText(Path.Combine(_carpeta, "rastreos", "rastreo_2026-11-16.json"), "{}");

        var resultado = await _rastreos.EscanearAsync();

        Assert.Equal(2, resultado.RastreosNuevos);
        Assert.Equal("Todavía no sé leer «rastreo_2026-11-16.json»: lo he dejado sin leer.", Assert.Single(resultado.Avisos));
    }

    // ---------------------------------------------------------------- listar y ver

    [Fact]
    public async Task Listar_DelMasRecienteAlMasAntiguoConSusCuentas()
    {
        await _rastreos.EscanearAsync();
        await _rastreos.DetectarAsync((await OfertaAsync(EnlaceUno)).Id);

        var lista = await _rastreos.ListarAsync();

        Assert.Equal([new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 3)], lista.Select(r => r.Fecha));
        Assert.Equal("Rastreo del lunes 09/11 (ejemplo inventado)", lista[0].Titulo);
        Assert.Equal((2, 2), (lista[0].Total, lista[0].Pendientes));
        // 6 ofertas: una pasada al tablero y otra que ya venía descartada.
        Assert.Equal((6, 4), (lista[1].Total, lista[1].Pendientes));
    }

    [Fact]
    public async Task Listar_SinEscanear_Vacio()
    {
        Assert.Empty(await _rastreos.ListarAsync());
    }

    [Fact]
    public async Task Obtener_DevuelveElInformeYLasOfertasEnSuOrden()
    {
        await _rastreos.EscanearAsync();
        var id = (await _rastreos.ListarAsync())[1].Id;

        var detalle = await _rastreos.ObtenerAsync(id);

        Assert.NotNull(detalle);
        Assert.Equal(RutasCurriculum.ArchivoRegistro, detalle.RutaInforme);
        Assert.StartsWith("Rastreo de prueba con el formato del equipo.", detalle.Informe);
        Assert.Equal(detalle.Ofertas.OrderBy(o => o.Linea).Select(o => o.Id), detalle.Ofertas.Select(o => o.Id));
        var primera = detalle.Ofertas[0];
        Assert.Equal(("Empresa Rastreo Uno", "Desarrollador/a .NET Junior", EnlaceUno), (primera.Empresa, primera.Puesto, primera.Enlace));
        Assert.StartsWith("- **Alto · Empresa Rastreo Uno", primera.TextoOriginal);
    }

    [Fact]
    public async Task Obtener_QueNoExiste_Null()
    {
        Assert.Null(await _rastreos.ObtenerAsync(999));
    }

    // ---------------------------------------------------------------- detectar

    [Fact]
    public async Task Detectar_CreaLaCandidaturaEnDetectadaConLosDatosDeLaOferta()
    {
        await _rastreos.EscanearAsync();
        var oferta = await OfertaAsync(EnlaceUno);
        var exportacionesAntes = Exportador.Llamadas;

        var resultado = await _rastreos.DetectarAsync(oferta.Id);

        Assert.NotNull(resultado);
        Assert.False(resultado.YaExistia);
        Assert.Equal(EstadoOfertaRastreo.Convertida, resultado.Oferta.Estado);
        Assert.Equal(resultado.CandidaturaId, resultado.Oferta.CandidaturaId);
        var ficha = (await _candidaturas.ObtenerAsync(resultado.CandidaturaId))!;
        Assert.Equal(EstadoCandidatura.Detectada, ficha.Estado);
        Assert.Equal(Origen.Rastreo, ficha.Origen);
        Assert.Equal(("Empresa Rastreo Uno", "Desarrollador/a .NET Junior", EnlaceUno), (ficha.Empresa, ficha.Puesto, ficha.Enlace));
        Assert.Equal(Encaje.Alto, ficha.Encaje);
        Assert.Equal("piden algo de Azure.", ficha.Pega);
        Assert.StartsWith("Alto · Empresa Rastreo Uno — Desarrollador/a .NET Junior (remoto", ficha.Analisis);
        // Medianoche del 03/11 en Madrid (en invierno, UTC+1).
        Assert.Equal(new DateTime(2026, 11, 2, 23, 0, 0, DateTimeKind.Utc), ficha.FechaDeteccion);
        Assert.Equal(exportacionesAntes + 1, Exportador.Llamadas);
        Assert.Equal(EstadoOfertaRastreo.Convertida, (await OfertaAsync(EnlaceUno)).Estado);
    }

    [Fact]
    public async Task Detectar_DosVeces_LaSegundaDevuelveLaMismaSinCrearOtra()
    {
        await _rastreos.EscanearAsync();
        var oferta = await OfertaAsync(EnlaceUno);

        var primera = await _rastreos.DetectarAsync(oferta.Id);
        var segunda = await _rastreos.DetectarAsync(oferta.Id);

        Assert.True(segunda!.YaExistia);
        Assert.Equal(primera!.CandidaturaId, segunda.CandidaturaId);
        await using var db = CreateContext();
        Assert.Equal(1, await db.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task Detectar_DobleClicALaVez_SoloUnaCandidatura()
    {
        await _rastreos.EscanearAsync();
        var oferta = await OfertaAsync(EnlaceLunesUno);
        var otro = NuevoServicio(Factory);

        var resultados = await Task.WhenAll(_rastreos.DetectarAsync(oferta.Id), otro.DetectarAsync(oferta.Id));

        Assert.Single(resultados.Select(r => r!.CandidaturaId).Distinct());
        Assert.Single(resultados, r => !r!.YaExistia);
        await using var db = CreateContext();
        Assert.Equal(1, await db.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task Detectar_YaHabiaUnaCandidaturaConEseEnlace_LaEnlazaSinCrearOtra()
    {
        await _rastreos.EscanearAsync();
        var existente = await _candidaturas.CrearAsync(new GuardarCandidaturaRequest("Empresa Rastreo Uno", "Otro puesto", "http://ejemplo.com/ofertas/2001"));

        var resultado = await _rastreos.DetectarAsync((await OfertaAsync(EnlaceUno)).Id);

        Assert.True(resultado!.YaExistia);
        Assert.Equal(existente.Id, resultado.CandidaturaId);
        await using var db = CreateContext();
        Assert.Equal(1, await db.Candidaturas.CountAsync());
    }

    [Fact]
    public async Task Detectar_DesdeIgnorada_LaPasaAlTablero()
    {
        await _rastreos.EscanearAsync();
        var oferta = await OfertaAsync(EnlaceTres);

        var resultado = await _rastreos.DetectarAsync(oferta.Id);

        Assert.Equal(EstadoOfertaRastreo.Convertida, resultado!.Oferta.Estado);
        Assert.Equal("Camarero/a", (await _candidaturas.ObtenerAsync(resultado.CandidaturaId))!.Puesto);
    }

    [Fact]
    public async Task Detectar_QueNoExiste_Null()
    {
        Assert.Null(await _rastreos.DetectarAsync(999));
    }

    [Fact]
    public async Task Detectar_CandidaturaBorrada_LaOfertaVuelveAPendienteYSePuedePasarOtraVez()
    {
        await _rastreos.EscanearAsync();
        var oferta = await OfertaAsync(EnlaceUno);
        var primera = await _rastreos.DetectarAsync(oferta.Id);
        await _candidaturas.BorrarAsync(primera!.CandidaturaId);

        var guardada = await OfertaAsync(EnlaceUno);
        Assert.Null(guardada.CandidaturaId);
        var detalle = await _rastreos.ObtenerAsync(oferta.RastreoId);
        Assert.Equal(EstadoOfertaRastreo.Pendiente, detalle!.Ofertas.Single(o => o.Id == oferta.Id).Estado);

        var segunda = await _rastreos.DetectarAsync(oferta.Id);
        Assert.False(segunda!.YaExistia);
        Assert.NotEqual(primera.CandidaturaId, segunda.CandidaturaId);
    }

    // ---------------------------------------------------------------- ignorar

    [Fact]
    public async Task Ignorar_Pendiente_LaDejaIgnoradaConLaFecha()
    {
        await _rastreos.EscanearAsync();
        Reloj.Avanzar(TimeSpan.FromMinutes(5));

        var oferta = await _rastreos.IgnorarAsync((await OfertaAsync(EnlaceUno)).Id);

        Assert.Equal(EstadoOfertaRastreo.Ignorada, oferta!.Estado);
        Assert.Equal(Reloj.AhoraUtc, oferta.FechaEstado);
        Assert.Equal(EstadoOfertaRastreo.Ignorada, (await OfertaAsync(EnlaceUno)).Estado);
    }

    [Fact]
    public async Task Ignorar_YaIgnorada_NoCambiaLaFecha()
    {
        await _rastreos.EscanearAsync();
        var antes = await OfertaAsync(EnlaceTres);
        Reloj.Avanzar(TimeSpan.FromMinutes(5));

        var oferta = await _rastreos.IgnorarAsync(antes.Id);

        Assert.Equal(antes.FechaEstado, oferta!.FechaEstado);
    }

    [Fact]
    public async Task Ignorar_Convertida_LanzaReglaNegocio()
    {
        await _rastreos.EscanearAsync();
        var oferta = await OfertaAsync(EnlaceUno);
        await _rastreos.DetectarAsync(oferta.Id);

        await Assert.ThrowsAsync<ReglaNegocioException>(() => _rastreos.IgnorarAsync(oferta.Id));
        Assert.Equal(EstadoOfertaRastreo.Convertida, (await OfertaAsync(EnlaceUno)).Estado);
    }

    [Fact]
    public async Task Ignorar_QueNoExiste_Null()
    {
        Assert.Null(await _rastreos.IgnorarAsync(999));
    }

    // ---------------------------------------------------------------- clave y año de las secciones del registro

    private const string ClaveSeccion = "registro:2026-11-03:rastreo 03/11 (martes, ejemplo inventado)";

    private async Task<List<Rastreo>> RastreosDelRegistroAsync()
    {
        await using var db = CreateContext();
        return await db.Rastreos.AsNoTracking().Where(r => r.Origen == OrigenRastreo.Registro).OrderBy(r => r.Id).ToListAsync();
    }

    [Fact]
    public async Task Escanear_RastreoGuardadoConLaClaveDeAntes_NoSeDuplicaYPasaALaNueva()
    {
        await _rastreos.EscanearAsync();
        await using (var db = CreateContext())
        {
            var guardado = await db.Rastreos.SingleAsync(r => r.Clave == ClaveSeccion);
            guardado.Clave = "registro:rastreo 03/11 (martes, ejemplo inventado)";
            await db.SaveChangesAsync();
        }

        var resultado = await _rastreos.EscanearAsync();

        Assert.Equal((0, 0, 0, 0, 3), Cuentas(resultado));
        Assert.Equal(ClaveSeccion, Assert.Single(await RastreosDelRegistroAsync()).Clave);
    }

    [Fact]
    public async Task Escanear_ElAnioSaleDeLaFechaDelRegistroYNoDeHoy()
    {
        // Un año después sin tocar el registro: «03/11» sigue siendo de 2026, no de 2027.
        Reloj.AhoraUtc = new DateTime(2027, 11, 20, 9, 0, 0, DateTimeKind.Utc);

        await _rastreos.EscanearAsync();

        var rastreo = Assert.Single(await RastreosDelRegistroAsync());
        Assert.Equal(new DateOnly(2026, 11, 3), rastreo.Fecha);
        Assert.Equal(ClaveSeccion, rastreo.Clave);
    }

    [Fact]
    public async Task Escanear_AlAnioSiguienteLaMismaSeccionSinCambios_NoSeDuplica()
    {
        await _rastreos.EscanearAsync();
        // Se edita el registro un año después (otra sección): la de «03/11» ya no cambia de año en la clave,
        // pero su texto es el mismo.
        Reloj.AhoraUtc = new DateTime(2027, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Registro, Reloj.AhoraUtc);

        var resultado = await _rastreos.EscanearAsync();

        Assert.Equal(0, resultado.RastreosNuevos);
        Assert.Equal(ClaveSeccion, Assert.Single(await RastreosDelRegistroAsync()).Clave);
    }

    [Fact]
    public async Task Escanear_SeccionDelAnioSiguienteConElMismoTitulo_EsOtroRastreoYNoTocaElViejo()
    {
        await _rastreos.EscanearAsync();
        var viejo = Assert.Single(await RastreosDelRegistroAsync());
        File.WriteAllText(Registro, "# Registro\n\n## Rastreo 03/11 (martes, ejemplo inventado)\n" +
            "- **Alto · Empresa del Año Siguiente — Backend** https://ejemplo.com/ofertas/2701\n");
        Reloj.AhoraUtc = new DateTime(2027, 11, 8, 9, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Registro, Reloj.AhoraUtc);

        var resultado = await _rastreos.EscanearAsync();

        Assert.Equal(1, resultado.RastreosNuevos);
        Assert.Equal(0, resultado.RastreosActualizados);
        var rastreos = await RastreosDelRegistroAsync();
        Assert.Equal(2, rastreos.Count);
        Assert.Equal(viejo.Hash, rastreos[0].Hash);
        Assert.Equal(new DateOnly(2026, 11, 3), rastreos[0].Fecha);
        Assert.Equal("registro:2027-11-03:rastreo 03/11 (martes, ejemplo inventado)", rastreos[1].Clave);
        Assert.Equal(new DateOnly(2027, 11, 3), rastreos[1].Fecha);
    }
}
