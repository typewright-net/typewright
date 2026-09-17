using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>The search range ends before it begins. Code <c>date_range</c>.</summary>
/// <param name="Begin">The requested begin date.</param>
/// <param name="End">The requested end date.</param>
public sealed record DateRangeError(DateOnly Begin, DateOnly End) : IDomainError
{
    /// <inheritdoc cref="IDomainError.DefaultMessage" />
    public string DefaultMessage => "The end date must be on or after the begin date.";
}

/// <summary>A date-range search for the invoices of one account.</summary>
/// <remarks>The cross-field rule makes the generated constructor private: instances can
/// only be built through the factories, so the date-range invariant always holds.
/// (<c>with</c> expressions still bypass it; analyzer TW0002 closes that hole.)</remarks>
[DomainModel]
[Reports<DateRangeError>]
public sealed partial record BillingDocumentSearch
{
    /// <summary>The first invoice date included in the search.</summary>
    public required InvoiceDate BeginDate { get; init; }

    /// <summary>The last invoice date included in the search.</summary>
    public required InvoiceDate EndDate { get; init; }

    /// <summary>The account whose invoices are searched.</summary>
    public required AccountNumber AccountNumber { get; init; }

    // Cross-field rule; runs only after every field validated individually, never on a
    // partially valid candidate.
    static partial void Validate(BillingDocumentSearch candidate, ValidationErrors errors)
    {
        if (candidate.EndDate < candidate.BeginDate)
        {
            errors.Add(new DateRangeError(candidate.BeginDate.Value, candidate.EndDate.Value));
        }
    }
}
