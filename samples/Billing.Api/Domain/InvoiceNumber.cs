using Typewright.Validation;

namespace Billing.Api.Domain;

/// <summary>An invoice number: the letter <c>I</c> followed by nine digits.</summary>
[DomainValue<string>]
[Pattern(@"^I\d{9}$")]
public readonly partial record struct InvoiceNumber;
