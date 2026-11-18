using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Data;
using RadarEmpleo.Domain;
using RadarEmpleo.Services.Contratos;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Ofertas;

namespace RadarEmpleo.Services.Agentes;

/// <summary>
/// AnalizarEncaje (apartado 6 del plan y añadidos de la F7b). Antes de lanzar Claude comprueba si la oferta sigue
/// publicada con una sola descarga de la página, que es la que va al prompt (<see cref="ComprobadorOfertaAbierta"/>): si
/// está cerrada, termina sin Claude y sin gastar; si no se sabe, analiza igual y lo dice. Al terminar valida la salida y la
/// aplica con <see cref="CandidaturasService.AplicarAnalisisAgenteAsync"/>. Es scoped: el procesador usa la misma
/// instancia para preparar y aplicar una ejecución, y entre medias guarda aquí lo que se supo de la oferta.
/// </summary>
public sealed class ManejadorAnalizarEncaje(IDbContextFactory<AppDbContext> factory, ComprobadorOfertaAbierta comprobador,
    ILectorCurriculum lector, CandidaturasService candidaturas) : IManejadorTarea
{
    public const string MensajeCandidaturaBorrada = "La candidatura ya no existe: no se ha guardado nada.";
    public const string MensajeOfertaCerrada = "La oferta ya no está publicada: no he analizado nada.";
    public const string MensajeCandidaturaFinal = "La candidatura está descartada o cerrada: no hace falta analizar su encaje.";

    /// <summary>Largo máximo del resumen que se enseña en la lista.</summary>
    public const int LargoResumen = 120;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private ComprobacionOferta _oferta = new(EstadoOferta.NoSeSabe, "No se ha comprobado la oferta.");
    private bool _ofertaLeida;

    public DefinicionTarea Definicion => DefinicionesTareas.AnalizarEncaje;

    public async Task<bool> ValidarAsync(int? candidaturaId, CancellationToken cancelacion)
    {
        if (candidaturaId is not { } id) throw new ValidacionException("Falta la candidatura.", "candidaturaId");
        await using var db = await factory.CreateDbContextAsync(cancelacion);
        var estado = await db.Candidaturas.Where(c => c.Id == id).Select(c => (EstadoCandidatura?)c.Estado).FirstOrDefaultAsync(cancelacion);
        if (estado is not { } actual) return false;
        if (ReglasEstado.EsFinal(actual)) throw new ReglaNegocioException(MensajeCandidaturaFinal);
        return true;
    }

    public async Task<PreparacionTarea> ConstruirAsync(EjecucionAgente ejecucion, ContextoTarea contexto, CancellationToken cancelacion)
    {
        Candidatura? candidatura = null;
        if (ejecucion.CandidaturaId is { } id)
        {
            await using var db = await factory.CreateDbContextAsync(cancelacion);
            candidatura = await db.Candidaturas.AsNoTracking().Include(c => c.Empresa).FirstOrDefaultAsync(c => c.Id == id, cancelacion);
        }
        if (candidatura is null) return PreparacionTarea.Terminada(new ResultadoTarea(MensajeCandidaturaBorrada, null));

        var (comprobacion, pagina) = await comprobador.ComprobarAsync(candidatura.Enlace, cancelacion);
        _oferta = comprobacion;
        // Un muro de inicio de sesión, una portada u otra web no son la oferta: se analiza con la ficha.
        _ofertaLeida = comprobacion.TextoDeLaOferta && !string.IsNullOrWhiteSpace(pagina.Texto);
        if (comprobacion.Estado == EstadoOferta.Cerrada)
            return PreparacionTarea.Terminada(new ResultadoTarea(MensajeOfertaCerrada, Guardado(null)));

        var perfil = await lector.LeerPerfilAsync(cancelacion);
        var prompt = PromptAnalizarEncaje.Montar(new DatosAnalisisEncaje(candidatura.Empresa.Nombre, candidatura.Puesto,
            candidatura.Enlace, candidatura.Ubicacion, TextoModalidad(candidatura), _ofertaLeida ? pagina.Texto : null, perfil.Texto,
            pagina.Recortada, comprobacion.Estado == EstadoOferta.NoSeSabe ? comprobacion.Motivo : null));
        var argumentos = ConstructorArgumentos.Construir(Definicion, Prompts.Esquema(Definicion), new ValoresArgumentos(contexto.CarpetaDatos));
        return PreparacionTarea.ConClaude(contexto.Peticion(Definicion, argumentos, prompt));
    }

    public async Task<ResultadoTarea> AplicarAsync(EjecucionAgente ejecucion, RespuestaClaude respuesta, CancellationToken cancelacion)
    {
        var resultado = Validar(respuesta.SalidaEstructurada);
        var json = Guardado(resultado);
        if (ejecucion.CandidaturaId is not { } id || !await candidaturas.AplicarAnalisisAgenteAsync(id, resultado))
            return new ResultadoTarea(MensajeCandidaturaBorrada, json);
        return new ResultadoTarea(Resumen(resultado), json);
    }

    /// <summary>La salida de Claude con los límites de la tabla 4.4; si no los cumple, <see cref="MensajesEjecucion.SalidaNoValida"/>.</summary>
    private static ResultadoAnalisisEncaje Validar(JsonElement? salida)
    {
        try
        {
            if (salida is { ValueKind: JsonValueKind.Object } objeto && objeto.Deserialize<ResultadoAnalisisEncaje>(Json) is { } resultado
                && Validator.TryValidateObject(resultado, new ValidationContext(resultado), null, validateAllProperties: true))
                return resultado;
        }
        catch (JsonException)
        {
            // Un tipo que no es el del esquema o un encaje que no existe: no se entiende, como lo que falta.
        }
        throw new ReglaNegocioException(MensajesEjecucion.SalidaNoValida);
    }

    private string Guardado(ResultadoAnalisisEncaje? r) => JsonSerializer.Serialize(
        new AnalisisEncajeGuardado(_oferta.Estado, _oferta.Motivo, _ofertaLeida, r?.Encaje, r?.Pega, r?.Analisis, r?.Resumen), Json);

    /// <summary>«Encaje medio. Pega: …», con el aviso si la oferta no se ha leído o no se sabe si sigue abierta, en 120 caracteres.</summary>
    private string Resumen(ResultadoAnalisisEncaje r)
    {
        var encaje = r.Encaje switch
        {
            Encaje.Alto => "Encaje alto.",
            Encaje.Medio => "Encaje medio.",
            Encaje.TiroLargo => "Tiro largo.",
            _ => "No encaja."
        };
        var aviso = !_ofertaLeida ? " No he podido leer la oferta: analizo con los datos de la ficha."
            : _oferta.Estado == EstadoOferta.NoSeSabe ? " No sé si sigue abierta." : "";
        var pega = string.Join(' ', r.Pega.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var texto = $"{encaje}{aviso} " + (pega.Length > 0 ? $"Pega: {pega}" : "Sin pega.");
        return texto.Length <= LargoResumen ? texto : texto[..(LargoResumen - 1)].TrimEnd() + "…";
    }

    private static string TextoModalidad(Candidatura c)
    {
        var tipo = c.Modalidad switch
        {
            Modalidad.Hibrido => "Híbrido",
            Modalidad.NoIndicada => "No indicada",
            var otra => otra.ToString()
        };
        return string.IsNullOrWhiteSpace(c.ModalidadDetalle) ? tipo : $"{tipo} ({c.ModalidadDetalle.Trim()})";
    }
}
