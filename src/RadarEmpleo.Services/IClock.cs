namespace RadarEmpleo.Services;

/// <summary>
/// Reloj inyectable: abstrae <see cref="DateTime.UtcNow"/> para que los servicios sean
/// testeables (poder fijar «ahora» en pruebas) sin depender del reloj del sistema.
/// A diferencia de Siessta, aquí todo se guarda en UTC, así que el reloj da la hora UTC.
/// </summary>
public interface IClock
{
    DateTime AhoraUtc { get; }
}

/// <summary>Implementación de producción: delega en el reloj real del sistema.</summary>
public sealed class RelojSistema : IClock
{
    public DateTime AhoraUtc => DateTime.UtcNow;
}
