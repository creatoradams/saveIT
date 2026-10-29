using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaveIT.Search;

namespace SaveIT.Api;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var conflict = exception is DbUpdateConcurrencyException || exception is DbUpdateException { InnerException: PostgresException { SqlState: "23505" } };
        var status = exception switch { SearchValidationException => 400, SearchUnavailableException => 503, _ => conflict ? 409 : 500 };
        var title = status switch {
            400 => "Use 2–120 characters, at most 12 search terms, and valid filters/cursor.",
            503 => "Search is temporarily unavailable. Try again shortly.",
            409 => "This record changed. Reload it before saving again.",
            _ => "The request could not be completed."
        };
        // Do not log exception messages/SQL parameters or request bodies containing documentation.
        logger.LogWarning("Request failed: {ExceptionType}; correlation {CorrelationId}", exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new() { HttpContext = context, ProblemDetails = new() {
            Status = status, Title = title,
            Extensions = { ["correlationId"] = context.TraceIdentifier }
        } });
    }
}
