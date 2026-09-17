using Microsoft.AspNetCore.Http;

namespace Typewright.Validation.AspNetCore;

/// <summary>
/// Catches <see cref="DomainValidationException"/> thrown by binding or by application code
/// and writes an RFC 9457 <c>HttpValidationProblemDetails</c> response with status 400.
/// Also turns <see cref="BadHttpRequestException"/> from Typewright binders (for example an
/// unsupported content type) into its status code rather than an unhandled exception.
/// Register with <c>app.UseTypewright()</c>.
/// </summary>
public sealed class TypewrightValidationMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    public TypewrightValidationMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    /// <summary>Invokes the rest of the pipeline, converting validation failures into responses.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainValidationException exception) when (!context.Response.HasStarted)
        {
            await ValidationProblems.WriteAsync(context, exception);
        }
        catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = exception.StatusCode;
        }
    }
}
