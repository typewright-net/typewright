namespace Typewright.Validation.Generators;

internal enum PropertyKind
{
    Value = 0,
    Model = 1,
    ValueCollection = 2,
    ModelCollection = 3,

    /// <summary>Not a domain type: copied between model and DTO without validation.</summary>
    Raw = 4,
}

/// <summary>One composed property of a domain model.</summary>
internal sealed record PropertySpec
{
    /// <summary>The CLR property name (PascalCase).</summary>
    public string Name { get; init; } = "";

    /// <summary>Camel-cased local/parameter form of <see cref="Name"/>, escaped if a keyword.</summary>
    public string CamelName { get; init; } = "";

    /// <summary>The JSON path segment: an explicit [JsonPropertyName] value, else camelCase.</summary>
    public string JsonName { get; init; } = "";

    /// <summary>Explicit [JsonPropertyName] value to carry onto the DTO property, or null.</summary>
    public string? ExplicitJsonName { get; init; }

    public PropertyKind Kind { get; init; }

    /// <summary>Nullable property: missing raw input maps through as null instead of erroring.</summary>
    public bool Optional { get; init; }

    /// <summary>Fully qualified domain type (the element type for collections), non-nullable form.</summary>
    public string DomainDisplay { get; init; } = "";

    /// <summary>Whether the domain type is a value type (affects the nullable spelling).</summary>
    public bool DomainIsValueType { get; init; }

    /// <summary>
    /// Fully qualified raw shape backing the domain type, non-nullable form: the raw type
    /// for values, the DTO type for models (element types for collections).
    /// </summary>
    public string RawOrDtoDisplay { get; init; } = "";

    /// <summary>Fully qualified DTO property type, nullability included.</summary>
    public string DtoTypeDisplay { get; init; } = "";

    /// <summary>Fully qualified declared property type, non-nullable form (for factory parameters).</summary>
    public string DeclaredDisplay { get; init; } = "";
}

/// <summary>Everything the emitter needs to generate one domain model.</summary>
internal sealed record ModelSpec
{
    public string Namespace { get; init; } = "";

    public string Name { get; init; } = "";

    public string HintName { get; init; } = "";

    /// <summary>Whether the user declared the cross-field Validate hook (drives constructor accessibility).</summary>
    public bool HasValidateHook { get; init; }

    public EquatableArray<PropertySpec> Properties { get; init; } = EquatableArray<PropertySpec>.Empty;
}
