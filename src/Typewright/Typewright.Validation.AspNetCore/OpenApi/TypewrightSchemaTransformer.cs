using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Typewright.Validation.AspNetCore.OpenApi;

/// <summary>
/// Describes domain types in OpenAPI using their generated <see cref="DomainTypeInfo"/>:
/// field values become their raw primitive plus rule metadata (<c>pattern</c>,
/// <c>minLength</c>, ...), domain models become their DTO schema, and DTO property
/// schemas are enriched with the composed fields' rules.
/// </summary>
public sealed class TypewrightSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <inheritdoc />
    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        if (DomainTypeRegistry.Find(type) is { } info)
        {
            if (info.IsModel)
            {
                // The model serializes through its DTO, so it is documented as the DTO.
                var dtoSchema = await context.GetOrCreateSchemaAsync(info.RawType, cancellationToken: cancellationToken);
                CopyInto(schema, dtoSchema);
            }
            else
            {
                OpenApiSchemaMapping.ApplyDomainValue(schema, info);
            }

            return;
        }

        // A model's DTO: enrich the raw property schemas with the fields' rules.
        if (DomainTypeRegistry.FindModelByDto(type) is { } modelInfo && schema.Properties is { } properties)
        {
            foreach (var field in modelInfo.Fields)
            {
                if (!properties.TryGetValue(field.Name, out var propertySchema)
                    || DomainTypeRegistry.Find(field.FieldType) is not { IsModel: false } fieldInfo)
                {
                    continue;
                }

                var target = field.IsCollection
                    ? (propertySchema as OpenApiSchema)?.Items as OpenApiSchema
                    : propertySchema as OpenApiSchema;
                if (target is not null)
                {
                    OpenApiSchemaMapping.ApplyDomainValue(target, fieldInfo);
                }
            }
        }
    }

    private static void CopyInto(OpenApiSchema target, IOpenApiSchema source)
    {
        if (source is not OpenApiSchema inline)
        {
            return;
        }

        target.Type = inline.Type;
        target.Format = inline.Format;
        target.Properties = inline.Properties;
        target.Required = inline.Required;
        target.Items = inline.Items;
        target.AdditionalProperties = inline.AdditionalProperties;
        target.AdditionalPropertiesAllowed = inline.AdditionalPropertiesAllowed;
        target.Description = inline.Description;
        target.Pattern = inline.Pattern;
        target.MinLength = inline.MinLength;
        target.MaxLength = inline.MaxLength;
        target.Minimum = inline.Minimum;
        target.Maximum = inline.Maximum;
        target.Enum = inline.Enum;
    }
}
