using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;
using Typewright.Validation.Fluent;
using Typewright.Validation.Json;

namespace Billing.Fluent.Domain;

/// <summary>The date an invoice was issued.</summary>
/// <remarks>Fluent equivalent of Billing.Api's <c>[DomainValue&lt;DateOnly&gt;(Comparable = true)]</c>;
/// comparability is ordinary hand-written members here.</remarks>
[JsonConverter(typeof(DomainValueJsonConverter<InvoiceDate, DateOnly>))]
public readonly record struct InvoiceDate
    : IDomainValue<InvoiceDate, DateOnly>, IComparable<InvoiceDate>, IParsable<InvoiceDate>
{
    private static readonly DomainValueDefinition<InvoiceDate, DateOnly> Definition =
        DomainValue.Define<InvoiceDate, DateOnly>(static value => new(value))
            .Build();

    private readonly DateOnly _value;

    private InvoiceDate(DateOnly value) => _value = value;

    public DateOnly Value => _value;

    public static ValidationResult<InvoiceDate> TryCreate(DateOnly value) => Definition.TryCreate(value);

    public static ValidationResult<InvoiceDate> TryCreate(DateOnly? value) => Definition.TryCreate(value);

    public static InvoiceDate Create(DateOnly value) => Definition.Create(value);

    public static InvoiceDate Create(DateOnly? value) => Definition.Create(value);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public static explicit operator DateOnly(InvoiceDate value) => value.Value;

    public override string ToString() => _value.ToString("O", CultureInfo.InvariantCulture);

    public static InvoiceDate Parse(string s, IFormatProvider? provider) => Definition.Parse(s, provider);

    public static bool TryParse(string? s, IFormatProvider? provider, out InvoiceDate result)
        => Definition.TryParse(s, provider, out result);

    public int CompareTo(InvoiceDate other) => _value.CompareTo(other._value);

    public static bool operator <(InvoiceDate left, InvoiceDate right) => left.CompareTo(right) < 0;

    public static bool operator >(InvoiceDate left, InvoiceDate right) => left.CompareTo(right) > 0;

    public static bool operator <=(InvoiceDate left, InvoiceDate right) => left.CompareTo(right) <= 0;

    public static bool operator >=(InvoiceDate left, InvoiceDate right) => left.CompareTo(right) >= 0;

    public static ValueTask<InvoiceDate?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainValueAsync<InvoiceDate, DateOnly>(context, parameter);
}
