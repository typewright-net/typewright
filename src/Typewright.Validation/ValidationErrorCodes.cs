namespace Typewright.Validation;

/// <summary>
/// Error codes used by the built-in validation rules. Codes are stable and machine-readable;
/// custom rules may introduce their own codes (dotted suffixes are conventional, for example
/// <c>date_range.invalid</c>).
/// </summary>
public static class ValidationErrorCodes
{
    /// <summary>A required value was null or missing.</summary>
    public const string Required = "required";

    /// <summary>A raw value could not be parsed into the field's raw type.</summary>
    public const string Format = "format";

    /// <summary>A string value did not match the required pattern.</summary>
    public const string Pattern = "pattern";

    /// <summary>A string value was shorter or longer than allowed.</summary>
    public const string Length = "length";

    /// <summary>A value fell outside the allowed range.</summary>
    public const string Range = "range";

    /// <summary>A value was empty or whitespace where content is required.</summary>
    public const string NotEmpty = "not_empty";

    /// <summary>A request body could not be parsed as JSON.</summary>
    public const string JsonInvalid = "json.invalid";
}
