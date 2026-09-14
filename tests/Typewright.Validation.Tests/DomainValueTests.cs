using System.Text.Json;
using Typewright.Validation;
using Xunit;

namespace Typewright.Validation.Tests;

public class DomainValueTests
{
    [Fact]
    public void TryCreate_accepts_valid_input()
    {
        var result = TestCode.TryCreate("ABC");

        Assert.True(result.IsValid);
        Assert.Equal("ABC", result.Value.Value);
    }

    [Theory]
    [InlineData(null, ValidationErrorCodes.Required)]
    [InlineData("ab", ValidationErrorCodes.Pattern)]
    [InlineData("ABCD", ValidationErrorCodes.Pattern)]
    public void TryCreate_rejects_invalid_input(string? input, string expectedCode)
    {
        var result = TestCode.TryCreate(input);

        Assert.False(result.IsValid);
        Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Create_throws_with_the_validation_errors()
    {
        var exception = Assert.Throws<DomainValidationException>(() => TestCode.Create("nope"));

        Assert.Equal(ValidationErrorCodes.Pattern, Assert.Single(exception.Errors).Code);
    }

    [Fact]
    public void Default_instance_value_fails_loudly()
        => Assert.Throws<InvalidOperationException>(() => default(TestCode).Value);
}

public class DomainValueJsonConverterTests
{
    [Fact]
    public void Writes_the_raw_value()
        => Assert.Equal("\"ABC\"", JsonSerializer.Serialize(TestCode.Create("ABC")));

    [Fact]
    public void Reads_by_validating()
        => Assert.Equal("ABC", JsonSerializer.Deserialize<TestCode>("\"ABC\"").Value);

    [Fact]
    public void Invalid_raw_value_throws_domain_validation()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => JsonSerializer.Deserialize<TestCode>("\"nope\""));

        Assert.Equal(ValidationErrorCodes.Pattern, Assert.Single(exception.Errors).Code);
    }

    [Fact]
    public void Null_token_surfaces_as_required()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => JsonSerializer.Deserialize<TestCode>("null"));

        Assert.Equal(ValidationErrorCodes.Required, Assert.Single(exception.Errors).Code);
    }

    [Fact]
    public void Round_trips_inside_a_container()
    {
        var container = new Container { Code = TestCode.Create("XYZ") };

        var json = JsonSerializer.Serialize(container);
        Assert.Equal("""{"Code":"XYZ"}""", json);

        var back = JsonSerializer.Deserialize<Container>(json);
        Assert.Equal(container, back);
    }

    private sealed record Container
    {
        public TestCode Code { get; init; }
    }
}
