using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>One line of a billing document.</summary>
/// <remarks>No cross-field rules, so any combination of valid fields is a valid line item
/// and the constructor stays public.</remarks>
[DomainModel]
public sealed partial record LineItem
{
    /// <summary>What the line covers.</summary>
    public required ItemDescription Description { get; init; }

    /// <summary>How many units.</summary>
    public required Quantity Quantity { get; init; }

    /// <summary>The price per unit.</summary>
    public required UnitPrice UnitPrice { get; init; }
}
