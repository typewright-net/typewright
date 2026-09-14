namespace Typewright.Validation.Generators;

internal enum ToStringKind
{
    StringRaw = 0,
    Temporal = 1,
    Formattable = 2,
    Plain = 3,
}

/// <summary>
/// Everything the emitter needs to generate one domain value, in equatable primitives so
/// the incremental pipeline caches correctly.
/// </summary>
internal sealed record ValueSpec
{
    public string Namespace { get; init; } = "";

    public string Name { get; init; } = "";

    public string HintName { get; init; } = "";

    /// <summary>Fully qualified display of the raw type (keywords for special types).</summary>
    public string RawDisplay { get; init; } = "";

    public bool RawIsString { get; init; }

    public bool RawIsReference { get; init; }

    public bool RawIsParsable { get; init; }

    public bool RawIsSpanParsable { get; init; }

    /// <summary>The user wrote their own static BindAsync; never emit one over it.</summary>
    public bool UserDeclaredBindAsync { get; init; }

    /// <summary>Set at output time when the compilation references the ASP.NET integration.</summary>
    public bool EmitBindAsync { get; init; }

    public bool Comparable { get; init; }

    public ToStringKind ToStringKind { get; init; }

    public bool NormalizeTrim { get; init; }

    /// <summary>0 = none, 1 = upper, 2 = lower (mirrors <c>Typewright.Validation.Casing</c>).</summary>
    public int NormalizeCase { get; init; }

    public string? Pattern { get; init; }

    public int? LengthMin { get; init; }

    public int? LengthMax { get; init; }

    public bool NotEmpty { get; init; }

    public bool HasRange { get; init; }

    /// <summary>C# expressions for the range bounds (a literal, or the parsed static field).</summary>
    public string? RangeMinExpr { get; init; }

    public string? RangeMaxExpr { get; init; }

    /// <summary>Human-readable bounds for the error message.</summary>
    public string? RangeMinDisplay { get; init; }

    public string? RangeMaxDisplay { get; init; }

    /// <summary>Invariant decimal bounds for schema metadata; null when not numeric.</summary>
    public string? RangeMinSchema { get; init; }

    public string? RangeMaxSchema { get; init; }

    /// <summary>When set, bounds are parsed from these strings into static fields.</summary>
    public string? RangeMinParseText { get; init; }

    public string? RangeMaxParseText { get; init; }
}
