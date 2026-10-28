using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SaveIT.Api;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var conflict = exception is DbUpdateConcurrencyException || exception is DbUpdateException { InnerException: PostgresException { SqlState: "23505" } };
        var status = conflict ? 409 : 500;
        // Do not log exception messages/SQL parameters or request bodies containing documentation.
        logger.LogWarning("Request failed: {ExceptionType}; correlation {CorrelationId}", exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new() { HttpContext = context, ProblemDetails = new() {
            Status = status, Title = conflict ? "This record changed. Reload it before saving again." : "The request could not be completed.",
            Extensions = { ["correlationId"] = context.TraceIdentifier }
        } });
    }
}
