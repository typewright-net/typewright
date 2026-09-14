using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typewright.Validation.Json;

/// <summary>
/// Serializes a domain value as its raw value and deserializes by validating through
/// <c>Create</c>. Intended for trusted data (storage, log replay, tests); untrusted HTTP
/// input flows through the DTO shape instead so all errors accumulate.
/// </summary>
public sealed class DomainValueJsonConverter<TValue, TRaw> : JsonConverter<TValue>
    where TValue : struct, IDomainValue<TValue, TRaw>
{
    /// <inheritdoc />
    /// <remarks>Null tokens are handled here so they surface as a <c>required</c> validation error.</remarks>
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override TValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = JsonSerializer.Deserialize<TRaw>(ref reader, options);
        return TValue.Create(raw);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TValue value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value.Value, options);
}
