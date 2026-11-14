using System.Globalization;
using System.Linq.Expressions;

namespace RadarEmpleo.Domain;

/// <summary>
/// Reglas de los recordatorios (lógica pura, sin BD ni reloj). El día y la hora que escribe el usuario
/// son siempre de Madrid; lo que se guarda es el instante en UTC.
/// </summary>
public static class ReglasRecordatorio
{
    /// <summary>Hora de Madrid de un recordatorio sin hora: las 09:00, como los seguimientos.</summary>
    public static readonly TimeOnly HoraPorDefecto = TimeOnly.FromTimeSpan(ReglasEstado.HoraSeguimiento);

    public const string MensajeHoraEntrevista = "Pon la hora de la entrevista.";

    public const string MensajeHoraInexistente = "Esa hora no existe ese día: el reloj se adelanta de 02:00 a 03:00.";

    public const int LargoTitulo = 200;
    public const int LargoNotas = 2000;

    /// <summary>Días que abarca «Próximos» en Hoy, contando desde mañana.</summary>
    public const int DiasProximos = 7;

    /// <summary>Años entre los que se admite el día de un recordatorio (lo demás es un error al teclear).</summary>
    public const int AnioMinimo = 2000;
    public const int AnioMaximo = 2100;

    /// <summary>
    /// ¿Se enseña (en Hoy y en sus cifras)? Los sueltos siempre; los de una candidatura, mientras no esté
    /// descartada ni cerrada (si se reabre, vuelven solos). Como expresión, para usarla en las consultas.
    /// </summary>
    public static readonly Expression<Func<Recordatorio, bool>> SeEnsena = r =>
        r.Candidatura == null
        || (r.Candidatura.Estado != EstadoCandidatura.Descartada && r.Candidatura.Estado != EstadoCandidatura.Cerrada);

    /// <summary>Instante UTC de ese día y esa hora de Madrid (sin hora, <see cref="HoraPorDefecto"/>).</summary>
    /// <exception cref="ValidacionException">Esa hora no existe ese día, por el cambio de hora de marzo (campo <c>hora</c>).</exception>
    public static DateTime FechaHoraUtc(DateOnly dia, TimeOnly? hora = null)
    {
        var reloj = dia.ToDateTime(hora ?? HoraPorDefecto);
        if (Fechas.EsHoraInexistente(reloj))
            throw new ValidacionException(MensajeHoraInexistente, "hora");
        return Fechas.MadridAUtc(reloj);
    }

    /// <summary>
    /// El mismo reloj de Madrid en otro día: de las 15:00 del 23/10 a las 15:00 del 27/10, aunque entre
    /// medias cambie la hora (en UTC pasa de las 13:00 a las 14:00). Si esa hora no existe el día nuevo
    /// (las 02:30 del día que se adelanta el reloj), pasa a la primera que sí: las 03:00.
    /// </summary>
    public static DateTime Pospuesta(DateTime fechaHoraUtc, DateOnly dia)
    {
        var reloj = dia.ToDateTime(TimeOnly.FromDateTime(Fechas.UtcAMadrid(fechaHoraUtc)));
        return Fechas.MadridAUtc(Fechas.PrimeraHoraValida(reloj));
    }

    /// <summary>Día de Madrid en el que cae un instante UTC.</summary>
    public static DateOnly DiaMadrid(DateTime utc) => DateOnly.FromDateTime(Fechas.UtcAMadrid(utc));

    /// <summary>
    /// Lee una hora «HH:mm» (de 00:00 a 23:59), que es como la da un campo de hora del navegador.
    /// Vacía es <see cref="HoraPorDefecto"/>.
    /// </summary>
    /// <exception cref="ValidacionException">No es una hora «HH:mm» (campo <c>hora</c>).</exception>
    public static TimeOnly LeerHora(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return HoraPorDefecto;
        return TimeOnly.TryParseExact(texto.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora)
            ? hora
            : throw new ValidacionException("La hora tiene que ser como 09:30.", "hora");
    }

    /// <summary>
    /// Como <see cref="LeerHora(string?)"/>, pero una entrevista no admite la hora vacía: apuntarla a las
    /// 09:00 por no mirar la hora es el error que más cuesta.
    /// </summary>
    /// <exception cref="ValidacionException">Es una entrevista sin hora, o la hora no es «HH:mm» (campo <c>hora</c>).</exception>
    public static TimeOnly LeerHora(TipoRecordatorio tipo, string? texto)
    {
        if (tipo == TipoRecordatorio.Entrevista && string.IsNullOrWhiteSpace(texto))
            throw new ValidacionException(MensajeHoraEntrevista, "hora");
        return LeerHora(texto);
    }

    /// <summary>Comprueba que el día esté entre <see cref="AnioMinimo"/> y <see cref="AnioMaximo"/>.</summary>
    /// <exception cref="ValidacionException">El año queda fuera (campo <c>dia</c>).</exception>
    public static void ValidarDia(DateOnly dia)
    {
        if (dia.Year is < AnioMinimo or > AnioMaximo)
            throw new ValidacionException($"El día tiene que estar entre {AnioMinimo} y {AnioMaximo}.", "dia");
    }
}
