using System.Reflection;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;

namespace Billing.Api.Domain;

/// <summary>An invoice number: the letter <c>I</c> followed by nine digits.</summary>
[DomainValue<string>]
[Pattern(@"^I\d{9}$")]
public readonly partial record struct InvoiceNumber
{
    /// <summary>Binds from route, query, or header (hand-written until phase 4 moves binding into the generator).</summary>
    public static ValueTask<InvoiceNumber?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainValueAsync<InvoiceNumber, string>(context, parameter);
}
