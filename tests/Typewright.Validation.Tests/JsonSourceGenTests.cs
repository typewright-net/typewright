using System.Text.Json;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Xunit;

namespace Typewright.Validation.Tests;

/// <summary>
/// The DTO-routed converters work with System.Text.Json source generation: the domain
/// converter resolves the raw type through the options, so registering the container and
/// raw types in a <see cref="JsonSerializerContext"/> is all AOT-friendly serialization
/// needs.
/// </summary>
public class JsonSourceGenTests
{
    public sealed record SourceGenContainer
    {
        public TestCode Code { get; init; }
    }

    [Fact]
    public void Round_trips_with_source_generated_metadata()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = TestJsonContext.Default };

        var json = JsonSerializer.Serialize(new SourceGenContainer { Code = TestCode.Create("XYZ") }, options);
        Assert.Equal("""{"Code":"XYZ"}""", json);

        var back = JsonSerializer.Deserialize<SourceGenContainer>(json, options);
        Assert.Equal("XYZ", back!.Code.Value);
    }

    [Fact]
    public void Invalid_raw_input_still_surfaces_domain_errors()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = TestJsonContext.Default };

        var exception = Assert.Throws<DomainValidationException>(
            () => JsonSerializer.Deserialize<SourceGenContainer>("""{"Code":"nope"}""", options));

        Assert.IsType<PatternError>(Assert.Single(exception.Errors).Error);
    }
}

[JsonSerializable(typeof(JsonSourceGenTests.SourceGenContainer))]
[JsonSerializable(typeof(string))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
