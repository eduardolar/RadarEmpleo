namespace RadarEmpleo.Api.Seguridad;

/// <summary>Por qué endpoint de Kestrel ha llegado la petición.</summary>
public enum CanalPeticion
{
    /// <summary>El endpoint «Local» (5180): el PC de Eduardo.</summary>
    Local,
    /// <summary>El endpoint «Remoto» (5181): el móvil.</summary>
    Remoto,
    /// <summary>Sin marca: se trata como <see cref="Remoto"/> (falla cerrado).</summary>
    Desconocido
}

/// <summary>Decide el canal de una petición y, con él, el <see cref="Domain.Origen"/> de lo que crea.</summary>
public interface IClasificadorCanal
{
    CanalPeticion Canal(HttpContext http);

    /// <summary>Local da <see cref="Domain.Origen.Manual"/>; cualquier otro canal, <see cref="Domain.Origen.Movil"/>.</summary>
    Domain.Origen Origen(HttpContext http) => Canal(http) == CanalPeticion.Local ? Domain.Origen.Manual : Domain.Origen.Movil;
}
