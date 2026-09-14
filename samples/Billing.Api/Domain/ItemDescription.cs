using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Json;

namespace Billing.Api.Domain;

// Phase 0 hand-written reference for what the generator will emit from:
//
//   [DomainValue<string>]
//   [Length(1, 128), NotEmpty]
//   public readonly partial record struct ItemDescription;

/// <summary>The description of a line item.</summary>
[JsonConverter(typeof(DomainValueJsonConverter<ItemDescription, string>))]
public readonly partial record struct ItemDescription : IDomainValue<ItemDescription, string>, ISpanParsable<ItemDescription>
{
    private readonly string _value;

    private ItemDescription(string value) => _value = value;

    /// <summary>The validated raw value.</summary>
    public string Value => _value ?? throw new InvalidOperationException(
        "This ItemDescription was not created through Create or TryCreate (default instance).");

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.TryCreate" />
    public static ValidationResult<ItemDescription> TryCreate(string? value)
    {
        var errors = new ValidationErrors();
        if (value is null)
        {
            errors.Add(ValidationErrorCodes.Required, "A value is required.");
            return ValidationResult<ItemDescription>.Failure(errors);
        }

        // Length(1, 128)
        if (value.Length is < 1 or > 128)
        {
            errors.Add(ValidationErrorCodes.Length, "The value must be between 1 and 128 characters long.");
        }

        // NotEmpty
        if (value.Length > 0 && string.IsNullOrWhiteSpace(value))
        {
            errors.Add(ValidationErrorCodes.NotEmpty, "The value must not be blank.");
        }

        return errors.Count > 0
            ? ValidationResult<ItemDescription>.Failure(errors)
            : ValidationResult<ItemDescription>.Success(new ItemDescription(value));
    }

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.Create" />
    public static ItemDescription Create(string? value) => TryCreate(value).OrThrow();

    /// <summary>Unwraps the raw value. Explicit so the domain type does not silently decay.</summary>
    public static explicit operator string(ItemDescription value) => value.Value;

    /// <inheritdoc />
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc />
    public static ItemDescription Parse(string s, IFormatProvider? provider) => Create(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ItemDescription result)
    {
        var validation = TryCreate(s);
        result = validation.IsValid ? validation.Value : default;
        return validation.IsValid;
    }

    /// <inheritdoc />
    public static ItemDescription Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Create(s.ToString());

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ItemDescription result)
        => TryParse(s.ToString(), provider, out result);
}
