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
    /// path with all messages (localized when a localizer is given), and an
    /// <c>errorCodes</c> extension with the parallel codes.
    /// </summary>
    public static HttpValidationProblemDetails ToProblemDetails(
        DomainValidationException exception,
        IValidationMessageLocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var messages = new Dictionary<string, List<string>>();
        var codes = new Dictionary<string, List<string>>();
        foreach (var error in exception.Errors)
        {
            if (!messages.TryGetValue(error.Path, out var pathMessages))
            {
                messages[error.Path] = pathMessages = [];
                codes[error.Path] = [];
            }

            pathMessages.Add(localizer?.Localize(error) ?? error.Message);
            codes[error.Path].Add(error.Code);
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
    /// <see cref="IProblemDetailsService"/> and <see cref="IValidationMessageLocalizer"/>
    /// when present.
    /// </summary>
    public static async Task WriteAsync(HttpContext context, DomainValidationException exception)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = ToProblemDetails(
            exception, context.RequestServices.GetService<IValidationMessageLocalizer>());
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
