using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;
using Typewright.Validation.Json;

namespace Billing.Api.Domain;

// ── User-authored part. Once the generator exists this is all that remains, plus:
//
//   [DomainValue<string>]
//   [Length(1, 64), Normalize(Trim = true, Case = Casing.Upper)]

/// <summary>A billing account number, normalized to trimmed upper case, with a Luhn checksum.</summary>
public readonly partial record struct AccountNumber
{
    // Custom rule; runs after the attribute rules and only when they passed.
    static partial void Validate(string value, ValidationErrors errors)
    {
        if (!Luhn.IsValid(value))
        {
            errors.Add("checksum", "The account number checksum is invalid.");
        }
    }
}

// ── Phase 0 hand-written reference for what the generator will emit. ──
[JsonConverter(typeof(DomainValueJsonConverter<AccountNumber, string>))]
public readonly partial record struct AccountNumber : IDomainValue<AccountNumber, string>, ISpanParsable<AccountNumber>
{
    private readonly string _value;

    private AccountNumber(string value) => _value = value;

    /// <summary>The validated raw value.</summary>
    public string Value => _value ?? throw new InvalidOperationException(
        "This AccountNumber was not created through Create or TryCreate (default instance).");

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.TryCreate" />
    public static ValidationResult<AccountNumber> TryCreate(string? value)
    {
        var errors = new ValidationErrors();
        if (value is null)
        {
            errors.Add(ValidationErrorCodes.Required, "A value is required.");
            return ValidationResult<AccountNumber>.Failure(errors);
        }

        // Normalize(Trim = true, Case = Casing.Upper)
        value = value.Trim().ToUpperInvariant();

        // Length(1, 64)
        if (value.Length is < 1 or > 64)
        {
            errors.Add(ValidationErrorCodes.Length, "The value must be between 1 and 64 characters long.");
        }

        if (errors.Count == 0)
        {
            Validate(value, errors);
        }

        return errors.Count > 0
            ? ValidationResult<AccountNumber>.Failure(errors)
            : ValidationResult<AccountNumber>.Success(new AccountNumber(value));
    }

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.Create" />
    public static AccountNumber Create(string? value) => TryCreate(value).OrThrow();

    static partial void Validate(string value, ValidationErrors errors);

    /// <summary>Unwraps the raw value. Explicit so the domain type does not silently decay.</summary>
    public static explicit operator string(AccountNumber value) => value.Value;

    /// <inheritdoc />
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc />
    public static AccountNumber Parse(string s, IFormatProvider? provider) => Create(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AccountNumber result)
    {
        var validation = TryCreate(s);
        result = validation.IsValid ? validation.Value : default;
        return validation.IsValid;
    }

    /// <inheritdoc />
    public static AccountNumber Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Create(s.ToString());

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out AccountNumber result)
        => TryParse(s.ToString(), provider, out result);

    /// <summary>Binds from route, query, or header; a failed bind throws with errors scoped to the parameter name.</summary>
    public static ValueTask<AccountNumber?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainValueAsync<AccountNumber, string>(context, parameter);
}
