using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Json;

namespace Billing.Api.Domain;

// Phase 0 hand-written reference for what the generator will emit from:
//
//   [DomainValue<string>]
//   [Length(1, 512)]
//   public readonly partial record struct Memo;

/// <summary>A free-form note on a billing document.</summary>
[JsonConverter(typeof(DomainValueJsonConverter<Memo, string>))]
public readonly partial record struct Memo : IDomainValue<Memo, string>, ISpanParsable<Memo>
{
    private readonly string _value;

    private Memo(string value) => _value = value;

    /// <summary>The validated raw value.</summary>
    public string Value => _value ?? throw new InvalidOperationException(
        "This Memo was not created through Create or TryCreate (default instance).");

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.TryCreate" />
    public static ValidationResult<Memo> TryCreate(string? value)
    {
        var errors = new ValidationErrors();
        if (value is null)
        {
            errors.Add(ValidationErrorCodes.Required, "A value is required.");
            return ValidationResult<Memo>.Failure(errors);
        }

        // Length(1, 512)
        if (value.Length is < 1 or > 512)
        {
            errors.Add(ValidationErrorCodes.Length, "The value must be between 1 and 512 characters long.");
        }

        return errors.Count > 0
            ? ValidationResult<Memo>.Failure(errors)
            : ValidationResult<Memo>.Success(new Memo(value));
    }

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.Create" />
    public static Memo Create(string? value) => TryCreate(value).OrThrow();

    /// <summary>Unwraps the raw value. Explicit so the domain type does not silently decay.</summary>
    public static explicit operator string(Memo value) => value.Value;

    /// <inheritdoc />
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc />
    public static Memo Parse(string s, IFormatProvider? provider) => Create(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Memo result)
    {
        var validation = TryCreate(s);
        result = validation.IsValid ? validation.Value : default;
        return validation.IsValid;
    }

    /// <inheritdoc />
    public static Memo Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Create(s.ToString());

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Memo result)
        => TryParse(s.ToString(), provider, out result);
}
