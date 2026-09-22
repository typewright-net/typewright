using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Fluent;
using Typewright.Validation.Json;

namespace Billing.Fluent.Domain;

/// <summary>A free-form note on a billing document.</summary>
[JsonConverter(typeof(DomainValueJsonConverter<Memo, string>))]
public readonly record struct Memo : IDomainValue<Memo, string>
{
    private static readonly DomainValueDefinition<Memo, string> Definition =
        DomainValue.Define<Memo, string>(static value => new(value))
            .Length(1, 512)
            .Build();

    private readonly string _value;

    private Memo(string value) => _value = value;

    public string Value => _value ?? throw new InvalidOperationException(
        "This Memo was not created through Create or TryCreate (default instance).");

    public static ValidationResult<Memo> TryCreate(string? value) => Definition.TryCreate(value);

    public static Memo Create(string? value) => Definition.Create(value);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public static explicit operator string(Memo value) => value.Value;

    public override string ToString() => _value ?? string.Empty;
}
