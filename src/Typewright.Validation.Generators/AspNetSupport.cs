using Microsoft.CodeAnalysis;

namespace Typewright.Validation.Generators;

/// <summary>
/// Detects whether the consuming compilation references the ASP.NET Core integration,
/// which turns on BindAsync emission for domain values and models.
/// </summary>
internal static class AspNetSupport
{
    public static IncrementalValueProvider<bool> Available(IncrementalGeneratorInitializationContext context)
        => context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Typewright.Validation.AspNetCore.TypewrightBinding") is not null
            && compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.HttpContext") is not null);
}
