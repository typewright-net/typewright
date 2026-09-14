using Microsoft.OpenApi;

namespace Typewright.Validation.AspNetCore.OpenApi;

/// <summary>Maps a domain value's raw type and rules onto an OpenAPI schema.</summary>
internal static class OpenApiSchemaMapping
{
    /// <summary>Describes a field value as its raw primitive plus rule metadata.</summary>
    public static void ApplyDomainValue(OpenApiSchema schema, DomainTypeInfo info)
    {
        var raw = Nullable.GetUnderlyingType(info.RawType) ?? info.RawType;
        var (type, format) = MapRawType(raw);
        schema.Type = type;
        schema.Format = format;

        foreach (var rule in info.Rules)
        {
            switch (rule.Kind)
            {
                case "pattern":
                    schema.Pattern = rule.Pattern;
                    break;

                case "length":
                    schema.MinLength = rule.MinLength;
                    schema.MaxLength = rule.MaxLength;
                    break;

                case "not_empty":
                    schema.MinLength = Math.Max(schema.MinLength ?? 0, 1);
                    break;

                case "range":
                    if (rule.Minimum is { } minimum)
                    {
                        schema.Minimum = minimum.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    if (rule.Maximum is { } maximum)
                    {
                        schema.Maximum = maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    break;
            }
        }
    }

    private static (JsonSchemaType Type, string? Format) MapRawType(Type raw)
    {
        if (raw == typeof(string))
        {
            return (JsonSchemaType.String, null);
        }

        if (raw == typeof(int) || raw == typeof(short) || raw == typeof(byte) || raw == typeof(sbyte)
            || raw == typeof(ushort) || raw == typeof(uint))
        {
            return (JsonSchemaType.Integer, "int32");
        }

        if (raw == typeof(long) || raw == typeof(ulong))
        {
            return (JsonSchemaType.Integer, "int64");
        }

        if (raw == typeof(decimal) || raw == typeof(double))
        {
            return (JsonSchemaType.Number, "double");
        }

        if (raw == typeof(float))
        {
            return (JsonSchemaType.Number, "float");
        }

        if (raw == typeof(bool))
        {
            return (JsonSchemaType.Boolean, null);
        }

        if (raw == typeof(DateOnly))
        {
            return (JsonSchemaType.String, "date");
        }

        if (raw == typeof(TimeOnly))
        {
            return (JsonSchemaType.String, "time");
        }

        if (raw == typeof(DateTime) || raw == typeof(DateTimeOffset))
        {
            return (JsonSchemaType.String, "date-time");
        }

        if (raw == typeof(Guid))
        {
            return (JsonSchemaType.String, "uuid");
        }

        return (JsonSchemaType.String, null);
    }
}
