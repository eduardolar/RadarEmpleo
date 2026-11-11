using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RadarEmpleo.Api.Configuracion;
using RadarEmpleo.Api.Errores;
using RadarEmpleo.Data;
using RadarEmpleo.Services;
using RadarEmpleo.Services.Curriculum;
using RadarEmpleo.Services.Importacion;
using RadarEmpleo.Services.Rastreos;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json (opcional y fuera de git): rutas personales y, en la F7, el token del móvil.
// Va después de appsettings.{Entorno}.json para poder pisarlo, pero ANTES de las variables de entorno
// y de la línea de comandos, para que «--RadarEmpleo:RutaBaseDatos=…» siempre mande.
ConfiguracionLocal.Anadir(builder.Configuration);

// --- Opciones ---
builder.Services.Configure<OpcionesRadar>(builder.Configuration.GetSection(OpcionesRadar.Seccion));
builder.Services.Configure<OpcionesAgentes>(builder.Configuration.GetSection(OpcionesAgentes.Seccion));

// --- Base de datos: SQLite en %LOCALAPPDATA%\RadarEmpleo\radar.db (configurable) ---
// La ruta se lee al resolver las opciones (no al crear el builder) para que los tests puedan
// cambiarla con la configuración de WebApplicationFactory.
// RegistroErroresBaseDatos: las violaciones de un índice único (ya gestionadas: reintento o 409) bajan
// a Debug en el log en vez de salir como «fail:» con traza; cualquier otro error de BD sigue siendo error.
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) =>
{
    var ruta = sp.GetRequiredService<IOptions<OpcionesRadar>>().Value.RutaBaseDatosEfectiva();
    Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
    o.UseSqlite($"Data Source={ruta}");
    RegistroErroresBaseDatos.Configurar(o, sp.GetRequiredService<ILoggerFactory>());
});

// --- Servicios de la aplicación ---
builder.Services.AddSingleton<IClock, RelojSistema>();
builder.Services.AddScoped<CandidaturasService>();
builder.Services.AddScoped<EmpresasService>();
builder.Services.AddScoped<ImportacionService>();
builder.Services.AddScoped<DocumentosService>();
builder.Services.AddScoped<PerfilService>();
builder.Services.AddScoped<RastreosService>();
// Services no referencia a Api: las rutas se construyen aquí a partir de las opciones.
builder.Services.AddSingleton(sp =>
    new RutasCurriculum(sp.GetRequiredService<IOptions<OpcionesRadar>>().Value.CarpetaCurriculum));
builder.Services.AddSingleton<ILectorCurriculum, LectorCurriculum>();
builder.Services.AddSingleton<IExportadorRadar, ExportadorRadarMd>();

// --- API: controladores, enums como texto en JSON, ProblemDetails, 400 en español y 409 para reglas de negocio ---
// Enums SOLO como texto: con allowIntegerValues: false, {"estado":99} o "modalidad":42 dan 400 en vez
// de colarse en la BD. Se configura en los dos sitios: MVC (controladores) y ConfigureHttpJsonOptions
// (el generador de OpenAPI toma estas; sin ellas, los enums saldrían como enteros en el esquema).
builder.Services.AddControllers(o =>
    {
        // Claves de los errores con el nombre del JSON («estado», no «Estado»).
        o.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
        RespuestasValidacion.TraducirMensajes(o.ModelBindingMessageProvider);
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        // Los mensajes de System.Text.Json traen nombres de clases internas: no se exponen.
        o.AllowInputFormatterExceptionMessages = false;
    })
    .ConfigureApiBehaviorOptions(o =>
    {
        o.InvalidModelStateResponseFactory = RespuestasValidacion.CrearRespuesta;
        o.ClientErrorMapping[StatusCodes.Status404NotFound].Title = "No se ha encontrado.";
        o.ClientErrorMapping[StatusCodes.Status400BadRequest].Title = "La petición no es correcta.";
    });
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = RespuestasValidacion.TraducirTitulo);
builder.Services.AddExceptionHandler<ManejadorExcepciones>();
builder.Services.AddHealthChecks().AddCheck<ComprobacionBaseDatos>("base-datos");
builder.Services.AddOpenApi();

var app = builder.Build();

// Crea la base de datos si no existe y aplica las migraciones pendientes.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();

    // Temporales del radar que dejó una exportación cortada. Nunca debe impedir el arranque.
    try
    {
        scope.ServiceProvider.GetRequiredService<IExportadorRadar>().LimpiarTemporalesHuerfanos();
    }
    catch (Exception error)
    {
        app.Logger.LogWarning(error, "No se han podido limpiar los temporales del radar.");
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// Documentación de la API: /openapi/v1.json y la interfaz de Scalar en /scalar.
app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("RadarEmpleo API"));

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

/// <summary>Visible para <c>WebApplicationFactory&lt;Program&gt;</c> en los tests de integración.</summary>
public partial class Program;
