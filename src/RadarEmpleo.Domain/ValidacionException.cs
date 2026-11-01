namespace RadarEmpleo.Domain;

/// <summary>
/// Datos de entrada que no son válidos (un campo obligatorio vacío, un nombre de empresa que se queda
/// en nada al normalizarlo, un valor de enum que no existe…). La API la traduce a un 400 con el mensaje
/// en español, a diferencia de <see cref="ReglaNegocioException"/>, que es un 409: los datos están bien
/// formados, pero la operación no se permite en el estado actual.
/// </summary>
public sealed class ValidacionException : ArgumentException
{
    /// <param name="mensaje">Mensaje para el usuario, en español.</param>
    /// <param name="campo">Campo afectado, como se llama en el JSON (<c>empresa</c>, <c>estado</c>…), si lo hay.</param>
    public ValidacionException(string mensaje, string? campo = null) : base(mensaje)
    {
        Campo = campo;
    }

    /// <summary>Campo afectado (nombre en el JSON), o null si el error no es de un campo concreto.</summary>
    public string? Campo { get; }

    /// <summary>
    /// Lanza una <see cref="ValidacionException"/> si <paramref name="valor"/> no es uno de los valores
    /// declarados del enum (por ejemplo, un <c>99</c> que se haya colado como número).
    /// </summary>
    public static void ExigirDefinido<TEnum>(TEnum valor, string campo) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(valor))
            throw new ValidacionException($"El valor «{valor}» no es válido para «{campo}».", campo);
    }
}
