using RadarEmpleo.Domain;

namespace RadarEmpleo.Tests;

/// <summary>
/// <see cref="ReglasDocumento"/> con nombres de archivo reales de <c>Curriculum\Candidaturas\</c> y la
/// regla de vigente de <see cref="Candidatura.VincularDocumento"/> y <see cref="Candidatura.QuitarDocumento"/>.
/// </summary>
public class ReglasDocumentoTests
{
    private static readonly DateTime Ahora = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf", TipoDocumento.CV)]
    [InlineData("cv_minusculas.docx", TipoDocumento.CV)]
    [InlineData("Carta_Presentacion_Eduardo_Lara_Navarro_ClubIbericoLibro.pdf", TipoDocumento.Carta)]
    [InlineData("Carta_Motivacion_Eduardo_Lara_Navarro_nomadix.docx", TipoDocumento.Carta)]
    [InlineData("Entrevista_NovaIT_NET_11nov.md", TipoDocumento.GuiaEntrevista)]
    [InlineData("Guia_Entrevista_Ad7.pdf", TipoDocumento.GuiaEntrevista)]
    [InlineData("Correo_Vectech.pdf", TipoDocumento.Correo)]
    [InlineData("Mensaje_Vectech.txt", TipoDocumento.Correo)]
    [InlineData("Dosier_Competencias_NovaIT_Eduardo_Lara_Navarro.pdf", TipoDocumento.Otro)]
    [InlineData("CV 2022.docx", TipoDocumento.Otro)]
    [InlineData("", TipoDocumento.Otro)]
    public void TipoDesdeNombre_NombresReales_DeduceElTipo(string nombre, TipoDocumento esperado)
    {
        Assert.Equal(esperado, ReglasDocumento.TipoDesdeNombre(nombre));
    }

    [Theory]
    [InlineData("cv.pdf", true)]
    [InlineData("cv.PDF", true)]
    [InlineData("cv.docx", true)]
    [InlineData("cv.DocX", true)]
    [InlineData("cv.doc", false)]
    [InlineData("Dosier.pptx", false)]
    [InlineData("Entrevista.md", false)]
    [InlineData("Mensaje.txt", false)]
    [InlineData("cv.pdf.exe", false)]
    [InlineData("cv", false)]
    [InlineData(null, false)]
    public void EsExtensionPermitida_SoloPdfYDocx(string? nombre, bool esperado)
    {
        Assert.Equal(esperado, ReglasDocumento.EsExtensionPermitida(nombre));
    }

    [Theory]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf", "Marco Talento")]
    [InlineData("CV_Eduardo_Lara_Navarro_Java_Angular_ClubIbericoLibro.pdf", "Club Ibérico del Libro")]
    [InlineData("Carta_Presentacion_Eduardo_Lara_Navarro_ClubIbericoLibro.pdf", "Club Ibérico del Libro")]
    [InlineData("CV_Eduardo_Lara_Navarro_Java_GrupoNS.pdf", "Grupo NS")]
    [InlineData("CV_Eduardo_Lara_Navarro_Python_NET_UVT.pdf", "UVT")]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_Angular_ABM.pdf", "ABM")]
    [InlineData("CV_Eduardo_Lara_Navarro_Frontend_React_nomadix.pdf", "Nomadix")]
    [InlineData("Carta_Motivacion_Eduardo_Lara_Navarro_nomadix.pdf", "nomadix")]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_Angular_AgilGlobal.pdf", "Agil Global")]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_NovaIT.pdf", "NovaIT")]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_ERP_OrbiSystem.pdf", "OrbiSystem")]
    [InlineData("CV_Eduardo_Lara_Navarro_Python_IA_DATALUX.pdf", "Datalux")]
    // Lo de entre paréntesis no cuenta: es una aclaración, no parte del nombre.
    [InlineData("CV_Eduardo_Lara_Navarro_NET_ERP_OrbiSystem.pdf", "OrbiSystem (Silver Soft)")]
    [InlineData("Carta_Presentacion_Eduardo_Lara_Navarro_OrbiSystem.pdf", "OrbiSystem (Silver Soft)")]
    [InlineData("CV_Eduardo_Lara_Navarro_Operario_Aerolux_Daimiel.pdf", "Aerolux (vía Workpower)")]
    [InlineData("CV_Eduardo_Lara_Navarro_Mozo_Almacen_IMPULSA.pdf", "IMPULSA (Ayto. de ejemplo)")]
    [InlineData("CV_Eduardo_Lara_Navarro_Tecnico_Informatico_AlamedaEmplea.pdf", "Alameda Emplea (Centro Especial de Empleo)")]
    [InlineData("Carta_Presentacion_Eduardo_Lara_Navarro_AlamedaEmplea.pdf", "Alameda Emplea (Centro Especial de Empleo)")]
    // Sin nada fuera del paréntesis, vale lo de dentro.
    [InlineData("CV_Eduardo_Lara_Navarro_NET_Quentra.pdf", "(Quentra)")]
    public void Coincidencia_TodasLasPalabras_EsCompleta(string archivo, string empresa)
    {
        Assert.Equal(CoincidenciaEmpresa.Completa,
            ReglasDocumento.CoincidenciaConEmpresa(archivo, NombresEmpresa.Normalizar(empresa)));
    }

    [Theory]
    [InlineData("CV_Eduardo_Lara_Navarro_Python_IA_DATALUX.pdf", "Grupo DATALUX")]
    [InlineData("CV_Eduardo_Lara_Navarro_Python_Angular_IA_Vectech.pdf", "Vectech Servicios Informáticos")]
    [InlineData("CV_Eduardo_Lara_Navarro_FullStack_Inditec.pdf", "Inditec Sistemas, S.A.")]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_Angular_ABM.pdf", "ABM Grupo Venturi")]
    [InlineData("Carta_Presentacion_Eduardo_Lara_Navarro_ABM.pdf", "ABM Grupo Venturi")]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_Ad7.pdf", "Ad7 Hexágono")]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_NovaIT.pdf", "NovaIT España")]
    [InlineData("CV_Eduardo_Lara_Navarro_Soporte_TI_NEWLINE.pdf", "NEWLINE CONSULTING")]
    [InlineData("CV_Eduardo_Lara_Navarro_FullStack_Norvalia.pdf", "Norvalia Consulting")]
    public void Coincidencia_SoloAlgunaPalabraDistintiva_EsParcial(string archivo, string empresa)
    {
        Assert.Equal(CoincidenciaEmpresa.Parcial,
            ReglasDocumento.CoincidenciaConEmpresa(archivo, NombresEmpresa.Normalizar(empresa)));
    }

    [Theory]
    // «NS» dentro de otra palabra no es la empresa NS.
    [InlineData("CV_Eduardo_Lara_Navarro_Soporte_Transportes.pdf", "NS")]
    [InlineData("CV_Eduardo_Lara_Navarro_Consultoria_Insur.pdf", "NS")]
    // «Grupo» es genérica y «NS» es corta: ninguna de las dos basta sola.
    [InlineData("CV_Eduardo_Lara_Navarro_Java_sqtech.pdf", "Grupo NS")]
    [InlineData("CV_Eduardo_Lara_Navarro_Grupo_Lacteria.pdf", "Grupo NS")]
    // Las palabras de la tecnología están en muchos CV: «Net Consulting» no es cualquier CV de .NET.
    [InlineData("CV_Eduardo_Lara_Navarro_NET_Quentra.pdf", "Net Consulting")]
    [InlineData("CV_Eduardo_Lara_Navarro_Python_IA_UNIVA.pdf", "IA Global Solutions")]
    [InlineData("CV_Eduardo_Lara_Navarro_Data_Delvitte.pdf", "Data Systems")]
    // «Soft» está dentro de «Logisoft», pero no es una palabra suya.
    [InlineData("CV_Eduardo_Lara_Navarro_ERP_Logisoft.pdf", "OrbiSystem (Silver Soft)")]
    [InlineData("Carta_Presentacion_Eduardo_Lara_Navarro_Logisoft.pdf", "OrbiSystem (Silver Soft)")]
    [InlineData("CV_Eduardo_Lara_Navarro_Gestion_Zenova.pdf", "Nova Consulting")]
    // Lo de entre paréntesis no cuenta: «cliente cárnico» no es la empresa.
    [InlineData("CV_Eduardo_Lara_Navarro_Cajero_Reponedor.pdf", "Ranstek (cliente cárnico)")]
    [InlineData("CV_Eduardo_Lara_Navarro_Mozo_Almacen_IMPULSA.pdf", "Ranstek (vía IMPULSA)")]
    // Las palabras de puesto o de tecnología están en muchos CV.
    [InlineData("CV_Eduardo_Lara_Navarro_Python_IA_UNIVA.pdf", "Python Factory")]
    [InlineData("CV_Eduardo_Lara_Navarro_Soporte_IT_Softrek.pdf", "Soporte Total")]
    [InlineData("CV_Eduardo_Lara_Navarro_Java_Angular_Iridio.pdf", "Angular Labs")]
    [InlineData("CV_Eduardo_Lara_Navarro_Operario_Aerolux_Daimiel.pdf", "Operario Plus ETT")]
    [InlineData("CV_Eduardo_Lara_Navarro_Dependiente_Deporte.pdf", "Dependiente Express")]
    [InlineData("CV_Eduardo_Lara_Navarro_ERP_Logisoft.pdf", "ERP Partners")]
    [InlineData("CV_Eduardo_Lara_Navarro_Frontend_Seguridad_Softrek.pdf", "Seguridad Integral")]
    [InlineData("CV_Eduardo_Lara_Navarro_Gestion_Zenova.pdf", "Gestión Laboral")]
    [InlineData("CV_Eduardo_Lara_Navarro_Tecnico_IT_Lacteria.pdf", "Centro Técnico")]
    // El nombre de la persona no cuenta.
    [InlineData("CV_Eduardo_Lara_Navarro_Operario.pdf", "Navarro Logística")]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf", "Hollis")]
    [InlineData("CV_Eduardo_Lara_Navarro_NET_MarcoTalento.pdf", "")]
    public void Coincidencia_SinLaEmpresa_EsNinguna(string archivo, string empresa)
    {
        Assert.Equal(CoincidenciaEmpresa.Ninguna,
            ReglasDocumento.CoincidenciaConEmpresa(archivo, NombresEmpresa.Normalizar(empresa)));
    }

    // ---------------------------------------------------------------- vigente

    private static Candidatura Nueva() =>
        Candidatura.Nueva(new Empresa { Nombre = "Marco Talento", NombreNormalizado = "marco talento" },
            "Desarrollador .NET", Ahora, Origen.Manual);

    [Fact]
    public void Vincular_UnCvNuevo_PasaASerElVigenteYElAnteriorDejaDeSerlo()
    {
        var c = Nueva();
        var carta = c.VincularDocumento(TipoDocumento.Carta, "Carta.pdf", "Candidaturas/S/Carta.pdf", OrigenDocumento.Manual, Ahora);
        var primero = c.VincularDocumento(TipoDocumento.CV, "CV_1.pdf", "Candidaturas/S/CV_1.pdf", OrigenDocumento.Manual, Ahora);

        var segundo = c.VincularDocumento(TipoDocumento.CV, "CV_2.pdf", "Candidaturas/S/CV_2.pdf", OrigenDocumento.Manual, Ahora.AddMinutes(1));

        Assert.True(segundo.Vigente);
        Assert.False(primero.Vigente);
        Assert.True(carta.Vigente); // otro tipo: no se toca
        Assert.Equal(3, c.Documentos.Count);
        Assert.True(segundo.Encontrado);
        Assert.Equal(OrigenDocumento.Manual, segundo.Origen);
        Assert.Equal(Ahora.AddMinutes(1), segundo.FechaAlta);
    }

    [Fact]
    public void Vincular_LaMismaRutaDosVeces_LanzaReglaNegocio()
    {
        var c = Nueva();
        c.VincularDocumento(TipoDocumento.CV, "CV.pdf", "Candidaturas/S/CV.pdf", OrigenDocumento.Manual, Ahora);

        Assert.Throws<ReglaNegocioException>(() =>
            c.VincularDocumento(TipoDocumento.CV, "cv.pdf", "candidaturas/s/cv.pdf", OrigenDocumento.Manual, Ahora));
        Assert.Single(c.Documentos);
    }

    [Fact]
    public void Quitar_ElVigente_PasaASerloElMasRecienteDeSuTipo()
    {
        var c = Nueva();
        var antiguo = c.VincularDocumento(TipoDocumento.CV, "A.pdf", "A.pdf", OrigenDocumento.Manual, Ahora);
        var medio = c.VincularDocumento(TipoDocumento.CV, "B.pdf", "B.pdf", OrigenDocumento.Manual, Ahora.AddDays(1));
        var carta = c.VincularDocumento(TipoDocumento.Carta, "C.pdf", "C.pdf", OrigenDocumento.Manual, Ahora.AddDays(2));
        var vigente = c.VincularDocumento(TipoDocumento.CV, "D.pdf", "D.pdf", OrigenDocumento.Manual, Ahora.AddDays(3));

        Assert.True(c.QuitarDocumento(vigente));

        Assert.True(medio.Vigente);
        Assert.False(antiguo.Vigente);
        Assert.True(carta.Vigente);
        Assert.DoesNotContain(vigente, c.Documentos);
    }

    [Fact]
    public void Quitar_UnoQueNoEsVigente_NoCambiaElVigente()
    {
        var c = Nueva();
        var antiguo = c.VincularDocumento(TipoDocumento.CV, "A.pdf", "A.pdf", OrigenDocumento.Manual, Ahora);
        var vigente = c.VincularDocumento(TipoDocumento.CV, "B.pdf", "B.pdf", OrigenDocumento.Manual, Ahora.AddDays(1));

        Assert.True(c.QuitarDocumento(antiguo));

        Assert.True(vigente.Vigente);
    }

    [Fact]
    public void Quitar_ElUnicoDeSuTipo_NoDejaNingunoVigenteDeEseTipo()
    {
        var c = Nueva();
        var cv = c.VincularDocumento(TipoDocumento.CV, "A.pdf", "A.pdf", OrigenDocumento.Manual, Ahora);

        Assert.True(c.QuitarDocumento(cv));

        Assert.Empty(c.Documentos);
    }

    [Fact]
    public void Quitar_UnDocumentoDeOtraCandidatura_DevuelveFalse()
    {
        var c = Nueva();
        var otro = Nueva().VincularDocumento(TipoDocumento.CV, "A.pdf", "A.pdf", OrigenDocumento.Manual, Ahora);

        Assert.False(c.QuitarDocumento(otro));
    }
}
