using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadarEmpleo.Api.Errores;

namespace RadarEmpleo.Tests.Api;

/// <summary>
/// <see cref="ManejadorExcepciones"/> directamente, para los errores que por HTTP no se pueden provocar
/// a voluntad (dos peticiones que chocan justo entre la lectura y el guardado).
/// </summary>
public class ManejadorExcepcionesTests
{
    [Fact]
    public async Task Concurrencia_Da409ConUnMensajeQueDiceQuePasa()
    {
        var escritor = new EscritorFalso();
        var http = new DefaultHttpContext();

        var gestionada = await new ManejadorExcepciones(escritor).TryHandleAsync(http, new DbUpdateConcurrencyException("x"), default);

        Assert.True(gestionada);
        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        Assert.Equal(ManejadorExcepciones.MensajeCambiadaEnOtraPestana, escritor.Problema?.Detail);
    }

    /// <summary>Se queda con el ProblemDetails que se iba a escribir.</summary>
    private sealed class EscritorFalso : IProblemDetailsService
    {
        public ProblemDetails? Problema { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Problema = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Problema = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }
}
