using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>The price of one unit of a line item.</summary>
[DomainValue<decimal>]
[Range(0, 1_000_000)]
public readonly partial record struct UnitPrice;
