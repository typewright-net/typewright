using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typewright.Validation.Json;

/// <summary>
/// Serializes a domain model as its DTO shape and deserializes by validating through
/// <c>Create</c>. Intended for trusted data (storage, log replay, tests); untrusted HTTP
/// input flows through the DTO shape instead so all errors accumulate.
/// </summary>
public sealed class DomainModelJsonConverter<TModel, TDto> : JsonConverter<TModel>
    where TModel : class, IDomainModel<TModel, TDto>
    where TDto : class
{
    /// <inheritdoc />
    public override TModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<TDto>(ref reader, options);
        return TModel.Create(dto);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TModel value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value.ToDto(), options);
}
