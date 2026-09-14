using System.Reflection;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;

namespace Billing.Api.Domain;

/// <summary>An invoice to be billed against an account.</summary>
/// <remarks>Nullability drives required-ness: the nullable <see cref="Memo"/> maps null
/// through, every other property yields a <c>required</c> error when missing.</remarks>
[DomainModel]
public sealed partial record BillingDocument : IBindableFromHttpContext<BillingDocument>
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

    /// <summary>Binds from the JSON request body via the DTO shape, accumulating every error
    /// (hand-written until phase 4 moves binding into the generator).</summary>
    public static ValueTask<BillingDocument?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainModelAsync<BillingDocument, Dto>(context, parameter);
}
