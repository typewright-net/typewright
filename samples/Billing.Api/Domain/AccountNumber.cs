using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>The account number's Luhn checksum did not verify. Code <c>checksum</c>.</summary>
public sealed record ChecksumError : IDomainError
{
    /// <inheritdoc cref="IDomainError.DefaultMessage" />
    public string DefaultMessage => "The account number checksum is invalid.";
}

/// <summary>A billing account number, normalized to trimmed upper case, with a Luhn checksum.</summary>
[DomainValue<string>]
[Length(1, 64)]
[Normalize(Trim = true, Case = Casing.Upper)]
[Reports<ChecksumError>]
public readonly partial record struct AccountNumber
{
    // Custom rule; runs after the attribute rules and only when they passed.
    static partial void Validate(string value, ValidationErrors errors)
    {
        if (!Luhn.IsValid(value))
        {
            errors.Add(new ChecksumError());
        }
    }
}
