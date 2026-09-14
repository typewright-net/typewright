using Typewright.Validation;
using Xunit;

namespace Typewright.Validation.Tests;

public class ValidationResultTests
{
    [Fact]
    public void Success_exposes_value_and_hides_errors()
    {
        var result = ValidationResult<int>.Success(42);

        Assert.True(result.IsValid);
        Assert.Equal(42, result.Value);
        Assert.Equal(42, result.OrThrow());
        Assert.True(result.TryGet(out var value));
        Assert.Equal(42, value);
        Assert.Throws<InvalidOperationException>(() => result.Errors);
    }

    [Fact]
    public void Failure_exposes_errors_and_hides_value()
    {
        var result = ValidationResult<int>.Failure("path", new RequiredError());

        Assert.False(result.IsValid);
        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.False(result.TryGet(out _));
        var failure = Assert.Single(result.Errors);
        Assert.Equal("path", failure.Path);
        Assert.IsType<RequiredError>(failure.Error);
    }

    [Fact]
    public void OrThrow_on_failure_carries_all_errors()
    {
        var errors = new ValidationErrors();
        errors.AddAt("one", new RequiredError());
        errors.AddAt("two", new NotEmptyError());

        var exception = Assert.Throws<DomainValidationException>(
            () => ValidationResult<int>.Failure(errors).OrThrow());

        Assert.Same(errors, exception.Errors);
    }

    [Fact]
    public void Failure_requires_at_least_one_error()
        => Assert.Throws<ArgumentException>(() => ValidationResult<int>.Failure(new ValidationErrors()));

    [Fact]
    public void Default_instance_fails_loudly()
    {
        var result = default(ValidationResult<int>);

        Assert.False(result.IsValid);
        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Errors);
    }
}
