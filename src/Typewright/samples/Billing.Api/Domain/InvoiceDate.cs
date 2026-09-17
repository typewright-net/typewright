using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>The date an invoice was issued.</summary>
[DomainValue<DateOnly>(Comparable = true)]
public readonly partial record struct InvoiceDate;
