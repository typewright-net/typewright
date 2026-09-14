using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Json;

namespace Billing.Api.Domain;

// Phase 0 hand-written reference for what the generator will emit from:
//
//   [DomainValue<int>]
//   [Range(1, 10_000)]
//   public readonly partial record struct Quantity;

/// <summary>How many units a line item covers.</summary>
[JsonConverter(typeof(DomainValueJsonConverter<Quantity, int>))]
public readonly partial record struct Quantity : IDomainValue<Quantity, int>, ISpanParsable<Quantity>
{
    private readonly int _value;

    private Quantity(int value) => _value = value;

    /// <summary>The validated raw value.</summary>
    public int Value => _value;

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.TryCreate" />
    public static ValidationResult<Quantity> TryCreate(int value)
    {
        var errors = new ValidationErrors();

        // Range(1, 10_000)
        if (value is < 1 or > 10_000)
        {
            errors.Add(ValidationErrorCodes.Range, "The value must be between 1 and 10000.");
        }

        return errors.Count > 0
            ? ValidationResult<Quantity>.Failure(errors)
            : ValidationResult<Quantity>.Success(new Quantity(value));
    }

    /// <summary>Nullable-raw overload so nullable DTO properties bind directly; null is a <c>required</c> error.</summary>
    public static ValidationResult<Quantity> TryCreate(int? value)
        => value is null
            ? ValidationResult<Quantity>.Failure(string.Empty, ValidationErrorCodes.Required, "A value is required.")
            : TryCreate(value.Value);

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.Create" />
    public static Quantity Create(int value) => TryCreate(value).OrThrow();

    /// <summary>Nullable-raw overload; null throws with a <c>required</c> error.</summary>
    public static Quantity Create(int? value) => TryCreate(value).OrThrow();

    /// <summary>Unwraps the raw value. Explicit so the domain type does not silently decay.</summary>
    public static explicit operator int(Quantity value) => value.Value;

    /// <inheritdoc />
    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public static Quantity Parse(string s, IFormatProvider? provider)
        => Create(int.Parse(s, provider ?? CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Quantity result)
    {
        if (int.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var raw))
        {
            var validation = TryCreate(raw);
            result = validation.IsValid ? validation.Value : default;
            return validation.IsValid;
        }

        result = default;
        return false;
    }

    /// <inheritdoc />
    public static Quantity Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => Create(int.Parse(s, provider ?? CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Quantity result)
        => TryParse(s.ToString(), provider, out result);
}
