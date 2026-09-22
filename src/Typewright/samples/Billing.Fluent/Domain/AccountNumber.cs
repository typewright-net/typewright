using System.Reflection;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;
using Typewright.Validation.Fluent;
using Typewright.Validation.Json;

namespace Billing.Fluent.Domain;

/// <summary>The account number's Luhn checksum did not verify. Code <c>checksum</c>.</summary>
public sealed record ChecksumError : IDomainError
{
    public string DefaultMessage => "The account number checksum is invalid.";
}

/// <summary>A billing account number, normalized to trimmed upper case, with a Luhn checksum.</summary>
/// <remarks>Fluent equivalent of Billing.Api's attribute declaration; the custom rule uses
/// <c>Must</c>, which declares <see cref="ChecksumError"/> in the error set by construction.</remarks>
[JsonConverter(typeof(DomainValueJsonConverter<AccountNumber, string>))]
public readonly record struct AccountNumber : IDomainValue<AccountNumber, string>, IParsable<AccountNumber>
{
    private static readonly DomainValueDefinition<AccountNumber, string> Definition =
        DomainValue.Define<AccountNumber, string>(static value => new(value))
            .Normalize(trim: true, casing: Casing.Upper)
            .Length(1, 64)
            .Must(Luhn.IsValid, static _ => new ChecksumError())
            .Build();

    private readonly string _value;

    private AccountNumber(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException(
        "This AccountNumber was not created through Create or TryCreate (default instance).");

    public static ValidationResult<AccountNumber> TryCreate(string? value) => Definition.TryCreate(value);

    public static AccountNumber Create(string? value) => Definition.Create(value);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public static explicit operator string(AccountNumber value) => value.Value;

    public override string ToString() => _value ?? string.Empty;

    public static AccountNumber Parse(string s, IFormatProvider? provider) => Definition.Parse(s, provider);

    public static bool TryParse(string? s, IFormatProvider? provider, out AccountNumber result)
        => Definition.TryParse(s, provider, out result);

    public static ValueTask<AccountNumber?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainValueAsync<AccountNumber, string>(context, parameter);
}
