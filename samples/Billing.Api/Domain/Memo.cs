using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>A free-form note on a billing document.</summary>
[DomainValue<string>]
[Length(1, 512)]
public readonly partial record struct Memo;
