using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Typewright.Validation.AspNetCore;

/// <summary>Pipeline registration for Typewright validation.</summary>
public static class TypewrightApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the middleware that turns <see cref="DomainValidationException"/> into an
    /// RFC 9457 validation problem response. Add it before the endpoints so binding
    /// failures are caught.
    /// </summary>
    public static IApplicationBuilder UseTypewright(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<TypewrightValidationMiddleware>();
    }
}

/// <summary>Service registration for Typewright validation.</summary>
public static class TypewrightServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TypewrightValidationExceptionHandler"/>, an alternative to
    /// <c>app.UseTypewright()</c> for applications that already use <c>UseExceptionHandler</c>.
    /// </summary>
    public static IServiceCollection AddTypewrightExceptionHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddExceptionHandler<TypewrightValidationExceptionHandler>();
    }
}
