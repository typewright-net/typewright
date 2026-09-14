namespace Typewright.Validation;

/// <summary>
/// Marks a <c>readonly partial record struct</c> as a domain field value wrapping
/// <typeparamref name="TRaw"/>. The Typewright generator emits the value's storage,
/// factories, parsing, JSON support, and descriptor; rule attributes on the same type
/// declare its invariants, and an optional
/// <c>static partial void Validate(TRaw value, ValidationErrors errors)</c> hook runs
/// after the attribute rules pass.
/// </summary>
/// <typeparam name="TRaw">The raw type the value wraps.</typeparam>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class DomainValueAttribute<TRaw> : Attribute
{
    /// <summary>
    /// When true, the generator emits <see cref="IComparable{T}"/> and the comparison
    /// operators, delegating to the raw value.
    /// </summary>
    public bool Comparable { get; set; }
}

/// <summary>
/// Marks a partial record as a domain model composed of domain values, other domain
/// models, and collections of them. The Typewright generator emits the model's nested
/// <c>Dto</c> record (raw, all-nullable, serialization-friendly), <c>TryCreate</c> /
/// <c>Create</c> / <c>ToDto</c>, JSON support, and descriptor. Nullability drives
/// required-ness: a non-nullable property yields a <c>required</c> error when its raw
/// value is missing, a nullable property maps null through. An optional
/// <c>static partial void Validate(TSelf candidate, ValidationErrors errors)</c> hook adds
/// cross-field rules; declaring it makes the constructor private so instances can only be
/// built through the factories.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DomainModelAttribute : Attribute;

/// <summary>
/// Marks a domain model property as intentionally raw: it is copied between the model and
/// its DTO without validation. Without this attribute, a non-domain-typed property is
/// still copied through but analyzer TW0004 warns about it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RawAttribute : Attribute;

/// <summary>Requires a string value to match a regular expression.</summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class PatternAttribute(string pattern) : Attribute
{
    /// <summary>The regular expression the whole value must match.</summary>
    public string Pattern { get; } = pattern;
}

/// <summary>Requires a string value's length to fall within an inclusive range.</summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class LengthAttribute(int min, int max) : Attribute
{
    /// <summary>The minimum length, inclusive.</summary>
    public int Min { get; } = min;

    /// <summary>The maximum length, inclusive.</summary>
    public int Max { get; } = max;
}

/// <summary>Requires a value to fall within an inclusive range.</summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class RangeAttribute : Attribute
{
    /// <summary>Integer bounds, for integer and decimal raw types.</summary>
    public RangeAttribute(int min, int max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Floating-point bounds, for floating-point and decimal raw types.</summary>
    public RangeAttribute(double min, double max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Bounds parsed from invariant-culture strings with the raw type's <c>Parse</c>,
    /// for raw types such as <see cref="DateOnly"/> or <see cref="decimal"/> whose
    /// values cannot be attribute constants.
    /// </summary>
    public RangeAttribute(string min, string max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>The minimum value, inclusive.</summary>
    public object Min { get; }

    /// <summary>The maximum value, inclusive.</summary>
    public object Max { get; }
}

/// <summary>Requires a string value to contain at least one non-whitespace character.</summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class NotEmptyAttribute : Attribute;

/// <summary>How <see cref="NormalizeAttribute"/> adjusts casing.</summary>
public enum Casing
{
    /// <summary>Leave casing as provided.</summary>
    None,

    /// <summary>Uppercase invariant.</summary>
    Upper,

    /// <summary>Lowercase invariant.</summary>
    Lower,
}

/// <summary>
/// Normalizes a string value before any rule runs; the normalized form is what the
/// domain value stores.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class NormalizeAttribute : Attribute
{
    /// <summary>Trim leading and trailing whitespace.</summary>
    public bool Trim { get; set; }

    /// <summary>Adjust casing.</summary>
    public Casing Case { get; set; }
}
