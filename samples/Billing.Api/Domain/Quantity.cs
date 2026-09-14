using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>How many units a line item covers.</summary>
[DomainValue<int>]
[Range(1, 10_000)]
public readonly partial record struct Quantity;
