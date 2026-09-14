using Microsoft.AspNetCore.OpenApi;

namespace Typewright.Validation.AspNetCore.OpenApi;

/// <summary>OpenAPI registration for Typewright validation.</summary>
public static class TypewrightOpenApiExtensions
{
    /// <summary>
    /// Registers the Typewright schema and operation transformers so domain types are
    /// documented from their generated <see cref="DomainTypeInfo"/> descriptors:
    /// <code>
    /// builder.Services.AddOpenApi(options => options.AddTypewright());
    /// </code>
    /// </summary>
    public static OpenApiOptions AddTypewright(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddSchemaTransformer<TypewrightSchemaTransformer>();
        options.AddOperationTransformer<TypewrightOperationTransformer>();
        return options;
    }
}
