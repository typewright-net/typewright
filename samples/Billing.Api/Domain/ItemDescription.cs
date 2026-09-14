using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>The description of a line item.</summary>
[DomainValue<string>]
[Length(1, 128)]
[NotEmpty]
public readonly partial record struct ItemDescription;
