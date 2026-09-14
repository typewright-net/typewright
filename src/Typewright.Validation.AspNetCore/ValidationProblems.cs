using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Typewright.Validation.AspNetCore;

/// <summary>
/// Turns a <see cref="DomainValidationException"/> into an RFC 9457
/// <see cref="HttpValidationProblemDetails"/> response.
/// </summary>
public static class ValidationProblems
{
    /// <summary>
    /// The extension member on the problem-details body that carries the stable error codes,
    /// keyed by path parallel to <c>errors</c>.
    /// </summary>
    public const string ErrorCodesExtensionName = "errorCodes";

    /// <summary>
    /// Builds the problem details for an exception: status 400, one <c>errors</c> entry per
    /// path with all messages (rendered by <paramref name="renderer"/>, or the errors'
    /// default text), and an <c>errorCodes</c> extension with the parallel typed codes.
    /// </summary>
    public static HttpValidationProblemDetails ToProblemDetails(
        DomainValidationException exception,
        IDomainErrorRenderer? renderer = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        renderer ??= DefaultDomainErrorRenderer.Instance;

        var messages = new Dictionary<string, List<string>>();
        var codes = new Dictionary<string, List<string>>();
        foreach (var failure in exception.Errors)
        {
            if (!messages.TryGetValue(failure.Path, out var pathMessages))
            {
                messages[failure.Path] = pathMessages = [];
                codes[failure.Path] = [];
            }

            pathMessages.Add(renderer.Render(failure));
            codes[failure.Path].Add(failure.Error.Code);
        }

        var problem = new HttpValidationProblemDetails(
            messages.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()))
        {
            Status = StatusCodes.Status400BadRequest,
        };
        problem.Extensions[ErrorCodesExtensionName] =
            codes.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        return problem;
    }

    /// <summary>
    /// Writes the problem details to the response, composing with a registered
    /// <see cref="IProblemDetailsService"/> and <see cref="IDomainErrorRenderer"/>
    /// when present.
    /// </summary>
    public static async Task WriteAsync(HttpContext context, DomainValidationException exception)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = ToProblemDetails(
            exception, context.RequestServices.GetService<IDomainErrorRenderer>());
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        var problemDetailsService = context.RequestServices.GetService<IProblemDetailsService>();
        if (problemDetailsService is not null
            && await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problem,
            }))
        {
            return;
        }

        await context.Response.WriteAsJsonAsync(
            (object)problem,
            problem.GetType(),
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }
}
