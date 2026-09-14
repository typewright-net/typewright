using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;
using Typewright.Validation.Json;

namespace Billing.Api.Domain;

// Phase 0 hand-written reference for what the generator will emit from:
//
//   [DomainValue<string>]
//   [Pattern(@"^I\d{9}$")]
//   public readonly partial record struct InvoiceNumber;

/// <summary>An invoice number: the letter <c>I</c> followed by nine digits.</summary>
[JsonConverter(typeof(DomainValueJsonConverter<InvoiceNumber, string>))]
public readonly partial record struct InvoiceNumber : IDomainValue<InvoiceNumber, string>, ISpanParsable<InvoiceNumber>
{
    private readonly string _value;

    private InvoiceNumber(string value) => _value = value;

    /// <summary>The validated raw value.</summary>
    public string Value => _value ?? throw new InvalidOperationException(
        "This InvoiceNumber was not created through Create or TryCreate (default instance).");

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.TryCreate" />
    public static ValidationResult<InvoiceNumber> TryCreate(string? value)
    {
        var errors = new ValidationErrors();
        if (value is null)
        {
            errors.Add(ValidationErrorCodes.Required, "A value is required.");
            return ValidationResult<InvoiceNumber>.Failure(errors);
        }

        if (!ValidationPattern().IsMatch(value))
        {
            errors.Add(ValidationErrorCodes.Pattern, @"The value must match the pattern '^I\d{9}$'.");
        }

        return errors.Count > 0
            ? ValidationResult<InvoiceNumber>.Failure(errors)
            : ValidationResult<InvoiceNumber>.Success(new InvoiceNumber(value));
    }

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.Create" />
    public static InvoiceNumber Create(string? value) => TryCreate(value).OrThrow();

    /// <summary>Unwraps the raw value. Explicit so the domain type does not silently decay.</summary>
    public static explicit operator string(InvoiceNumber value) => value.Value;

    /// <inheritdoc />
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc />
    public static InvoiceNumber Parse(string s, IFormatProvider? provider) => Create(s);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out InvoiceNumber result)
    {
        var validation = TryCreate(s);
        result = validation.IsValid ? validation.Value : default;
        return validation.IsValid;
    }

    /// <inheritdoc />
    public static InvoiceNumber Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Create(s.ToString());

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out InvoiceNumber result)
        => TryParse(s.ToString(), provider, out result);

    /// <summary>Binds from route, query, or header; a failed bind throws with errors scoped to the parameter name.</summary>
    public static ValueTask<InvoiceNumber?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainValueAsync<InvoiceNumber, string>(context, parameter);

    [GeneratedRegex(@"^I\d{9}$")]
    private static partial Regex ValidationPattern();
}
