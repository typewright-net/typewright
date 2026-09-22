using System.Globalization;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Fluent;
using Typewright.Validation.Json;

namespace Billing.Fluent.Domain;

/// <summary>How many units a line item covers.</summary>
[JsonConverter(typeof(DomainValueJsonConverter<Quantity, int>))]
public readonly record struct Quantity : IDomainValue<Quantity, int>
{
    private static readonly DomainValueDefinition<Quantity, int> Definition =
        DomainValue.Define<Quantity, int>(static value => new(value))
            .Range(1, 10_000)
            .Build();

    private readonly int _value;

    private Quantity(int value) => _value = value;

    public int Value => _value;

    public static ValidationResult<Quantity> TryCreate(int value) => Definition.TryCreate(value);

    public static ValidationResult<Quantity> TryCreate(int? value) => Definition.TryCreate(value);

    public static Quantity Create(int value) => Definition.Create(value);

    public static Quantity Create(int? value) => Definition.Create(value);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public static explicit operator int(Quantity value) => value.Value;

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}
