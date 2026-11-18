using System.ComponentModel.DataAnnotations;
using RadarEmpleo.Domain;

namespace RadarEmpleo.Services.Contratos;

/// <summary>Una ejecución de agente para enseñarla.</summary>
/// <param name="CosteEstimado">Terminó sin coste conocido: en el gasto cuenta su presupuesto entero.</param>
/// <param name="RegistroCambiado">Aviso rojo: <c>ofertas-enviadas.md</c> cambió mientras trabajaba.</param>
/// <param name="PerfilCambiado">Aviso rojo: <c>perfil-busqueda.md</c> cambió mientras trabajaba.</param>
/// <param name="RastreoId">El rastreo que generó (tarea Rastreo), si lo hay.</param>
/// <param name="PuedeCancelar">En cola, o en curso sin la cancelación pedida todavía.</param>
/// <param name="PuedeReintentar">Ha terminado en Error o Cancelada y su tarea se puede volver a lanzar.</param>
public sealed record EjecucionAgenteDto(
    int Id, TareaAgente Tarea, EstadoEjecucion Estado, MotivoEspera? Espera, string? QuienBloquea,
    DateTime FechaAlta, DateTime? Inicio, DateTime? Fin,
    decimal PresupuestoUsd, int TiempoMaximoSegundos, decimal? CosteUsd, bool CosteEstimado,
    string? Resumen, string? Error, bool RegistroCambiado, bool PerfilCambiado,
    int? CandidaturaId, CandidaturaBreveDto? Candidatura, int? RastreoId,
    bool PuedeCancelar, bool PuedeReintentar);

/// <summary>Lo gastado hoy (día de Madrid) con los agentes.</summary>
/// <param name="GastadoUsd">Lo de las ejecuciones terminadas (las que no saben su coste cuentan su presupuesto).</param>
/// <param name="ReservadoUsd">El presupuesto de lo que está en curso o en cola: cuenta para el tope.</param>
/// <param name="DisponibleUsd">Lo que queda hasta el tope (nunca negativo).</param>
public sealed record GastoHoyDto(DateOnly Hoy, decimal GastadoUsd, decimal ReservadoUsd, decimal TopeUsd, decimal DisponibleUsd, int Ejecuciones);

/// <summary>Estado de los agentes para Ajustes y la página Agentes.</summary>
/// <param name="MotivoNoDisponibles">«Solo desde el PC» o «Apagados en la configuración»; null si están disponibles.</param>
/// <param name="VersionClaude">La de <c>claude --version</c>, o null si no se sabe.</param>
/// <param name="VersionDistinta">La versión instalada no es la probada (se avisa, pero no se bloquea).</param>
/// <param name="LlevaLaCola">Si esta copia de la API procesa la cola. Si no, no se puede encolar nada desde aquí.</param>
/// <param name="MotivoCola">Por qué no la lleva, en español; null si la lleva.</param>
public sealed record EstadoAgentesDto(
    bool Disponibles, string? MotivoNoDisponibles,
    string? VersionClaude, string VersionProbada, bool VersionDistinta,
    GastoHoyDto Gasto, EjecucionAgenteDto? EnCurso, int EnCola,
    bool LlevaLaCola, string? MotivoCola);

/// <summary>
/// La salida de AnalizarEncaje (tabla 4.4 del plan), validada otra vez en C# con los mismos límites que el esquema: lo que
/// no los cumple es <see cref="Agentes.MensajesEjecucion.SalidaNoValida"/>.
/// </summary>
public sealed record ResultadoAnalisisEncaje(
    [property: Required, AllowedValues(Encaje.Alto, Encaje.Medio, Encaje.TiroLargo, Encaje.No)] Encaje? Encaje,
    [property: Required(AllowEmptyStrings = true), MaxLength(2000)] string Pega,
    [property: Required(AllowEmptyStrings = true), MaxLength(8000)] string Analisis,
    [property: Required(AllowEmptyStrings = true), MaxLength(300)] string Resumen);
