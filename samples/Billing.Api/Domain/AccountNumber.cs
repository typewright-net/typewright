using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>A billing account number, normalized to trimmed upper case, with a Luhn checksum.</summary>
[DomainValue<string>]
[Length(1, 64)]
[Normalize(Trim = true, Case = Casing.Upper)]
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
