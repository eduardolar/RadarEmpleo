using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Contratos;

/// <summary>Un rastreo en la lista (del más reciente al más antiguo).</summary>
/// <param name="Fecha">Día del rastreo (AAAA-MM-DD).</param>
/// <param name="Total">Ofertas que se han leído de él.</param>
/// <param name="Pendientes">Las que aún no se han pasado al tablero ni se han dejado de lado.</param>
/// <param name="FechaEscaneo">Última vez que se leyó con cambios (UTC).</param>
public sealed record RastreoResumenDto(
    int Id,
    DateOnly Fecha,
    string Titulo,
    OrigenRastreo Origen,
    int Total,
    int Pendientes,
    DateTime FechaEscaneo);

/// <summary>Un rastreo con su informe (Markdown) y sus ofertas en el orden del informe.</summary>
/// <param name="RutaInforme">Archivo del que sale, dentro de Curriculum («ofertas-enviadas.md» o «rastreos/…»).</param>
/// <param name="Informe">Copia del Markdown del informe, sin el título.</param>
public sealed record RastreoDetalleDto(
    int Id,
    DateOnly Fecha,
    string Titulo,
    OrigenRastreo Origen,
    int Total,
    int Pendientes,
    DateTime FechaEscaneo,
    string RutaInforme,
    string Informe,
    IReadOnlyList<OfertaRastreoDto> Ofertas);

/// <summary>Una oferta de un rastreo.</summary>
/// <param name="Linea">Línea del archivo en la que estaba.</param>
/// <param name="Estado">Una oferta pasada al tablero cuya candidatura se ha borrado sale como Pendiente.</param>
/// <param name="CandidaturaId">Su candidatura del tablero, si se ha pasado.</param>
/// <param name="TextoOriginal">La línea del informe tal cual, con su Markdown.</param>
public sealed record OfertaRastreoDto(
    int Id,
    int RastreoId,
    int Linea,
    string Empresa,
    string Puesto,
    string Enlace,
    Encaje Encaje,
    string? Pega,
    EstadoOfertaRastreo Estado,
    int? CandidaturaId,
    DateTime FechaEstado,
    string TextoOriginal);

/// <summary>Lo que ha encontrado un escaneo.</summary>
/// <param name="OfertasNuevas">Ofertas que no estaban, contando las que ya estaban en el tablero.</param>
/// <param name="YaEnElTablero">De las nuevas, las que ya estaban en el tablero (con el mismo enlace).</param>
/// <param name="LineasSinLeer">Líneas de todos los informes que parecen una oferta pero no se han podido leer.</param>
/// <param name="Avisos">Lo que no se ha podido leer, explicado para Eduardo.</param>
public sealed record ResultadoEscaneoDto(
    int RastreosNuevos,
    int RastreosActualizados,
    int OfertasNuevas,
    int YaEnElTablero,
    int LineasSinLeer,
    IReadOnlyList<string> Avisos);

/// <summary>Resultado de pasar una oferta al tablero.</summary>
/// <param name="YaExistia">Ya había una candidatura con ese enlace: se ha enlazado en vez de crear otra.</param>
public sealed record ResultadoDeteccionDto(OfertaRastreoDto Oferta, int CandidaturaId, bool YaExistia);

/// <summary>
/// Cuerpo de los POST que no necesitan datos: tiene que llegar <c>{}</c> como JSON. Así, un formulario de
/// otra web abierta en el navegador recibe un 415 en vez de cambiar nada.
/// </summary>
public sealed record PeticionVacia;
