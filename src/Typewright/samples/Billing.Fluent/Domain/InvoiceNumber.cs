using System.Reflection;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;
using Typewright.Validation.Fluent;
using Typewright.Validation.Json;

namespace Billing.Fluent.Domain;

/// <summary>An invoice number: the letter <c>I</c> followed by nine digits.</summary>
/// <remarks>Fluent equivalent of Billing.Api's <c>[DomainValue&lt;string&gt;] [Pattern]</c> declaration.</remarks>
[JsonConverter(typeof(DomainValueJsonConverter<InvoiceNumber, string>))]
public readonly record struct InvoiceNumber : IDomainValue<InvoiceNumber, string>, IParsable<InvoiceNumber>
{
    private static readonly DomainValueDefinition<InvoiceNumber, string> Definition =
        DomainValue.Define<InvoiceNumber, string>(static value => new(value))
            .Pattern(@"^I\d{9}$")
            .Build();

    private readonly string _value;

    private InvoiceNumber(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException(
        "This InvoiceNumber was not created through Create or TryCreate (default instance).");

    public static ValidationResult<InvoiceNumber> TryCreate(string? value) => Definition.TryCreate(value);

    public static InvoiceNumber Create(string? value) => Definition.Create(value);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public static explicit operator string(InvoiceNumber value) => value.Value;

    public override string ToString() => _value ?? string.Empty;

    public static InvoiceNumber Parse(string s, IFormatProvider? provider) => Definition.Parse(s, provider);

    public static bool TryParse(string? s, IFormatProvider? provider, out InvoiceNumber result)
        => Definition.TryParse(s, provider, out result);

    public static ValueTask<InvoiceNumber?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainValueAsync<InvoiceNumber, string>(context, parameter);
}
