using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Typewright.Validation.AspNetCore;

/// <summary>
/// An <see cref="IExceptionHandler"/> alternative to <c>app.UseTypewright()</c> for
/// applications that already use <c>UseExceptionHandler</c>. Register with
/// <c>services.AddTypewrightExceptionHandler()</c>.
/// </summary>
public sealed class TypewrightValidationExceptionHandler : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainValidationException validationException)
        {
            return false;
        }

        await ValidationProblems.WriteAsync(httpContext, validationException);
        return true;
    }
}
