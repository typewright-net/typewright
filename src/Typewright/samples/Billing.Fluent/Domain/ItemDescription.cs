using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Fluent;
using Typewright.Validation.Json;

namespace Billing.Fluent.Domain;

/// <summary>The description of a line item.</summary>
[JsonConverter(typeof(DomainValueJsonConverter<ItemDescription, string>))]
public readonly record struct ItemDescription : IDomainValue<ItemDescription, string>
{
    private static readonly DomainValueDefinition<ItemDescription, string> Definition =
        DomainValue.Define<ItemDescription, string>(static value => new(value))
            .Length(1, 128)
            .NotEmpty()
            .Build();

    private readonly string _value;

    private ItemDescription(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException(
        "This ItemDescription was not created through Create or TryCreate (default instance).");

    public static ValidationResult<ItemDescription> TryCreate(string? value) => Definition.TryCreate(value);

    public static ItemDescription Create(string? value) => Definition.Create(value);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public static explicit operator string(ItemDescription value) => value.Value;

    public override string ToString() => _value ?? string.Empty;
}
