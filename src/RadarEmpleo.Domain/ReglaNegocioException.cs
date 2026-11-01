namespace RadarEmpleo.Domain;

/// <summary>
/// Error de regla de negocio: una validación o una transición de estado no permitida
/// cuyo mensaje está pensado para mostrarse al usuario como AVISO (no como error técnico).
///
/// Hereda de <see cref="InvalidOperationException"/> para no romper a quien capture ese tipo,
/// pero permite a la API distinguir un fallo de negocio (409 con un mensaje amable en español)
/// de un fallo inesperado de infraestructura (500).
///
/// Vive en Domain (en Siessta estaba en Services) porque la lanzan las propias reglas del
/// dominio (<see cref="ReglasEstado"/>), y Services y Api ya dependen de Domain.
/// </summary>
public sealed class ReglaNegocioException : InvalidOperationException
{
    public ReglaNegocioException(string mensaje) : base(mensaje) { }
}
