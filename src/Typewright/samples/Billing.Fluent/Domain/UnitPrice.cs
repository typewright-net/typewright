using System.Globalization;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Fluent;
using Typewright.Validation.Json;

namespace Billing.Fluent.Domain;

/// <summary>The price of one unit of a line item.</summary>
[JsonConverter(typeof(DomainValueJsonConverter<UnitPrice, decimal>))]
public readonly record struct UnitPrice : IDomainValue<UnitPrice, decimal>
{
    private static readonly DomainValueDefinition<UnitPrice, decimal> Definition =
        DomainValue.Define<UnitPrice, decimal>(static value => new(value))
            .Range(0m, 1_000_000m)
            .Build();

    private readonly decimal _value;

    private UnitPrice(decimal value) => _value = value;

    public decimal Value => _value;

    public static ValidationResult<UnitPrice> TryCreate(decimal value) => Definition.TryCreate(value);

    public static ValidationResult<UnitPrice> TryCreate(decimal? value) => Definition.TryCreate(value);

    public static UnitPrice Create(decimal value) => Definition.Create(value);

    public static UnitPrice Create(decimal? value) => Definition.Create(value);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public static explicit operator decimal(UnitPrice value) => value.Value;

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}
