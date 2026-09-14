using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Json;

namespace Billing.Api.Domain;

// Phase 0 hand-written reference for what the generator will emit from:
//
//   [DomainValue<decimal>]
//   [Range(0, 1_000_000)]
//   public readonly partial record struct UnitPrice;

/// <summary>The price of one unit of a line item.</summary>
[JsonConverter(typeof(DomainValueJsonConverter<UnitPrice, decimal>))]
public readonly partial record struct UnitPrice : IDomainValue<UnitPrice, decimal>, ISpanParsable<UnitPrice>
{
    private readonly decimal _value;

    private UnitPrice(decimal value) => _value = value;

    /// <summary>The validated raw value.</summary>
    public decimal Value => _value;

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.TryCreate" />
    public static ValidationResult<UnitPrice> TryCreate(decimal value)
    {
        var errors = new ValidationErrors();

        // Range(0, 1_000_000)
        if (value is < 0 or > 1_000_000)
        {
            errors.Add(ValidationErrorCodes.Range, "The value must be between 0 and 1000000.");
        }

        return errors.Count > 0
            ? ValidationResult<UnitPrice>.Failure(errors)
            : ValidationResult<UnitPrice>.Success(new UnitPrice(value));
    }

    /// <summary>Nullable-raw overload so nullable DTO properties bind directly; null is a <c>required</c> error.</summary>
    public static ValidationResult<UnitPrice> TryCreate(decimal? value)
        => value is null
            ? ValidationResult<UnitPrice>.Failure(string.Empty, ValidationErrorCodes.Required, "A value is required.")
            : TryCreate(value.Value);

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.Create" />
    public static UnitPrice Create(decimal value) => TryCreate(value).OrThrow();

    /// <summary>Nullable-raw overload; null throws with a <c>required</c> error.</summary>
    public static UnitPrice Create(decimal? value) => TryCreate(value).OrThrow();

    /// <summary>Unwraps the raw value. Explicit so the domain type does not silently decay.</summary>
    public static explicit operator decimal(UnitPrice value) => value.Value;

    /// <inheritdoc />
    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public static UnitPrice Parse(string s, IFormatProvider? provider)
        => Create(decimal.Parse(s, provider ?? CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out UnitPrice result)
    {
        if (decimal.TryParse(s, NumberStyles.Number, provider ?? CultureInfo.InvariantCulture, out var raw))
        {
            var validation = TryCreate(raw);
            result = validation.IsValid ? validation.Value : default;
            return validation.IsValid;
        }

        result = default;
        return false;
    }

    /// <inheritdoc />
    public static UnitPrice Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => Create(decimal.Parse(s, provider ?? CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out UnitPrice result)
        => TryParse(s.ToString(), provider, out result);
}
