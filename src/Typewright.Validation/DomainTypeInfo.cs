namespace Typewright.Validation;

/// <summary>
/// A generated, reflection-free description of a domain type: its name, raw shape, and
/// declared rules. OpenAPI transformers (and later, Lattice schema validation) introspect
/// this instead of the type system.
/// </summary>
public sealed class DomainTypeInfo
{
    /// <summary>The domain type's simple name.</summary>
    public required string Name { get; init; }

    /// <summary>The domain type itself.</summary>
    public required Type DomainType { get; init; }

    /// <summary>The raw type the domain type wraps (for models, the DTO type).</summary>
    public required Type RawType { get; init; }

    /// <summary>True for domain models (raw shape is a DTO), false for field values.</summary>
    public bool IsModel { get; init; }

    /// <summary>The declared validation rules, in the order they run.</summary>
    public IReadOnlyList<DomainRuleInfo> Rules { get; init; } = [];

    /// <summary>For domain models: the composed fields, in declaration order.</summary>
    public IReadOnlyList<DomainFieldInfo> Fields { get; init; } = [];
}

/// <summary>One composed field of a domain model.</summary>
public sealed record DomainFieldInfo
{
    /// <summary>The field's name in the JSON convention of the input shape.</summary>
    public required string Name { get; init; }

    /// <summary>The CLR property name on the model.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The domain type of the field (the element type for collections).</summary>
    public required Type FieldType { get; init; }

    /// <summary>Whether a missing raw value is an error.</summary>
    public required bool Required { get; init; }

    /// <summary>Whether the field is a collection of domain types.</summary>
    public bool IsCollection { get; init; }
}

/// <summary>
/// One declared rule of a domain type, carrying the metadata needed to describe the rule
/// as JSON schema (<c>pattern</c>, <c>minLength</c>, <c>minimum</c>, ...).
/// </summary>
public sealed record DomainRuleInfo
{
    /// <summary>The rule kind: <c>pattern</c>, <c>length</c>, <c>range</c>, or <c>not_empty</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The regular expression, for <c>pattern</c> rules.</summary>
    public string? Pattern { get; init; }

    /// <summary>The minimum string length, for <c>length</c> rules.</summary>
    public int? MinLength { get; init; }

    /// <summary>The maximum string length, for <c>length</c> rules.</summary>
    public int? MaxLength { get; init; }

    /// <summary>The inclusive numeric minimum, for numeric <c>range</c> rules.</summary>
    public decimal? Minimum { get; init; }

    /// <summary>The inclusive numeric maximum, for numeric <c>range</c> rules.</summary>
    public decimal? Maximum { get; init; }
}
