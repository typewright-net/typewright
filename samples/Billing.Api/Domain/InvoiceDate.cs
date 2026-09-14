using System.Reflection;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;

namespace Billing.Api.Domain;

/// <summary>The date an invoice was issued.</summary>
[DomainValue<DateOnly>(Comparable = true)]
public readonly partial record struct InvoiceDate
{
    /// <summary>Binds from route, query, or header (hand-written until phase 4 moves binding into the generator).</summary>
    public static ValueTask<InvoiceDate?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainValueAsync<InvoiceDate, DateOnly>(context, parameter);
}
