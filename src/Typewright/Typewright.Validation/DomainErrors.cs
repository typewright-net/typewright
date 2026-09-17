namespace Typewright.Validation;

// The built-in errors produced by generated rule checks and the binding layer. Each
// carries the structured facts of the failure; DefaultMessage is only the invariant
// fallback used by the default renderer.

/// <summary>A required value was null or missing. Code <c>required</c>.</summary>
public sealed record RequiredError : IDomainError
{
    /// <inheritdoc cref="IDomainError.DefaultMessage" />
    public string DefaultMessage => "A value is required.";
}

/// <summary>A raw value could not be parsed into the field's raw type. Code <c>format</c>.</summary>
/// <param name="Value">The raw text that failed to parse.</param>
/// <param name="RawType">The simple name of the raw type it was parsed as.</param>
public sealed record FormatError(string Value, string RawType) : IDomainError
{
    /// <inheritdoc cref="IDomainError.DefaultMessage" />
    public string DefaultMessage => $"The value '{Value}' could not be parsed as {RawType}.";
}

/// <summary>A string value did not match the required pattern. Code <c>pattern</c>.</summary>
/// <param name="Pattern">The regular expression the whole value must match.</param>
public sealed record PatternError(string Pattern) : IDomainError
{
    /// <inheritdoc cref="IDomainError.DefaultMessage" />
    public string DefaultMessage => $"The value must match the pattern '{Pattern}'.";
}

/// <summary>A string value's length fell outside the allowed range. Code <c>length</c>.</summary>
/// <param name="Min">The minimum length, inclusive.</param>
/// <param name="Max">The maximum length, inclusive.</param>
/// <param name="Actual">The actual length.</param>
public sealed record LengthError(int Min, int Max, int Actual) : IDomainError
{
    /// <inheritdoc cref="IDomainError.DefaultMessage" />
    public string DefaultMessage => $"The value must be between {Min} and {Max} characters long.";
}

/// <summary>A value fell outside the allowed range. Code <c>range</c>.</summary>
/// <param name="Min">The inclusive minimum, in invariant form.</param>
/// <param name="Max">The inclusive maximum, in invariant form.</param>
/// <param name="Actual">The actual value, in invariant form.</param>
public sealed record RangeError(string Min, string Max, string Actual) : IDomainError
{
    /// <inheritdoc cref="IDomainError.DefaultMessage" />
    public string DefaultMessage => $"The value must be between {Min} and {Max}.";
}

/// <summary>A value was empty or whitespace where content is required. Code <c>not_empty</c>.</summary>
public sealed record NotEmptyError : IDomainError
{
    /// <inheritdoc cref="IDomainError.DefaultMessage" />
    public string DefaultMessage => "The value must not be blank.";
}

/// <summary>A request body could not be parsed as JSON. Code <c>json_invalid</c>.</summary>
/// <param name="Detail">The parser's description of the failure.</param>
public sealed record JsonInvalidError(string Detail) : IDomainError
{
    /// <inheritdoc cref="IDomainError.DefaultMessage" />
    public string DefaultMessage => $"The request body is not valid JSON: {Detail}";
}
