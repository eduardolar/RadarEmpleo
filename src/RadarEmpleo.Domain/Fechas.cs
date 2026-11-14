namespace RadarEmpleo.Domain;

/// <summary>
/// Utilidades de fechas. Regla general: todo se guarda y viaja en UTC; la hora de Madrid solo se usa
/// para decidir «qué día es» y a qué hora local se programa algo (seguimientos a las 09:00, filtros
/// por día).
/// </summary>
public static class Fechas
{
    /// <summary>
    /// Zona horaria de Madrid (península). En .NET 10 el identificador IANA «Europe/Madrid» funciona en
    /// Windows y en Linux gracias a ICU; si el sistema no lo resolviera (Windows sin ICU o con
    /// globalización invariante), se usa el identificador de Windows «Romance Standard Time».
    /// </summary>
    public static TimeZoneInfo Madrid { get; } = BuscarZonaMadrid();

    /// <summary>
    /// Pasa a UTC una fecha que llega de fuera: las locales se convierten y las que no tienen zona
    /// (<see cref="DateTimeKind.Unspecified"/>) se dan por UTC. La usan los servicios y el conversor de EF.
    /// </summary>
    public static DateTime AUtc(DateTime fecha) => fecha.Kind switch
    {
        DateTimeKind.Utc => fecha,
        DateTimeKind.Local => fecha.ToUniversalTime(),
        _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
    };

    /// <summary>Hora de Madrid (sin zona) correspondiente a un instante UTC.</summary>
    public static DateTime UtcAMadrid(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(AUtc(utc), Madrid);

    /// <summary>
    /// Instante UTC correspondiente a una hora de reloj de Madrid (se ignora el Kind de la entrada).
    /// Lanza <see cref="ArgumentException"/> con una hora que no existe (<see cref="EsHoraInexistente"/>):
    /// quien la reciba de fuera tiene que comprobarla antes.
    /// </summary>
    public static DateTime MadridAUtc(DateTime horaMadrid) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(horaMadrid, DateTimeKind.Unspecified), Madrid);

    /// <summary>
    /// ¿Esa hora de reloj no existe en Madrid? Pasa la noche del último domingo de marzo, cuando el reloj
    /// salta de las 02:00 a las 03:00 (de 02:00 a 02:59 no hay nada).
    /// </summary>
    public static bool EsHoraInexistente(DateTime horaMadrid) =>
        Madrid.IsInvalidTime(DateTime.SpecifyKind(horaMadrid, DateTimeKind.Unspecified));

    /// <summary>
    /// La misma hora si existe; si cae en el salto de marzo, el primer minuto que sí existe (las 03:00).
    /// </summary>
    public static DateTime PrimeraHoraValida(DateTime horaMadrid)
    {
        if (!EsHoraInexistente(horaMadrid)) return horaMadrid;
        var minuto = new DateTime(horaMadrid.Year, horaMadrid.Month, horaMadrid.Day, horaMadrid.Hour, horaMadrid.Minute, 0);
        // El salto es de una hora: como mucho 60 vueltas
        while (EsHoraInexistente(minuto)) minuto = minuto.AddMinutes(1);
        return minuto;
    }

    /// <summary>Medianoche (00:00 en Madrid) del día indicado, en UTC.</summary>
    public static DateTime MedianocheMadridEnUtc(DateTime dia) => MadridAUtc(dia.Date);

    private static TimeZoneInfo BuscarZonaMadrid()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        }
    }
}
