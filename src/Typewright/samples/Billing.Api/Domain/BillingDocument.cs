using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>An invoice to be billed against an account.</summary>
/// <remarks>Nullability drives required-ness: the nullable <see cref="Memo"/> maps null
/// through, every other property yields a <c>required</c> error when missing.</remarks>
[DomainModel]
public sealed partial record BillingDocument
{
    /// <summary>The invoice's own number.</summary>
    public required InvoiceNumber InvoiceNumber { get; init; }

    /// <summary>The date the invoice was issued.</summary>
    public required InvoiceDate InvoiceDate { get; init; }

    /// <summary>The account being billed.</summary>
    public required AccountNumber AccountNumber { get; init; }

    /// <summary>An optional free-form note.</summary>
    public Memo? Memo { get; init; }

    /// <summary>The invoice lines.</summary>
    public required IReadOnlyList<LineItem> LineItems { get; init; }
}
