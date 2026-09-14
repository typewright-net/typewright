using Typewright.Validation;
using Xunit;

namespace Typewright.Validation.Tests;

public class DomainValidationExceptionTests
{
    [Fact]
    public void Single_failure_message_names_path_code_and_default_text()
    {
        var exception = new DomainValidationException("invoiceNumber", new PatternError(@"^I\d{9}$"));

        Assert.Equal(
            @"Validation failed: invoiceNumber: [pattern] The value must match the pattern '^I\d{9}$'.",
            exception.Message);
        var failure = Assert.Single(exception.Errors);
        Assert.Equal("invoiceNumber", failure.Path);
        Assert.Equal(new PatternError(@"^I\d{9}$"), failure.Error);
    }

    [Fact]
    public void Root_level_failure_omits_the_path()
    {
        var errors = new ValidationErrors();
        errors.Add(new RequiredError());

        var exception = new DomainValidationException(errors);

        Assert.Equal("Validation failed: [required] A value is required.", exception.Message);
    }

    [Fact]
    public void Multiple_failures_are_counted_and_listed()
    {
        var errors = new ValidationErrors();
        errors.AddAt("a", new RequiredError());
        errors.AddAt("b", new NotEmptyError());

        var exception = new DomainValidationException(errors);

        Assert.Equal(
            "Validation failed with 2 errors: a: [required] A value is required.; "
            + "b: [not_empty] The value must not be blank.",
            exception.Message);
    }

    [Fact]
    public void Typed_errors_support_when_guards()
    {
        try
        {
            var errors = new ValidationErrors();
            errors.AddAt("account", new LengthError(1, 64, 70));
            throw new DomainValidationException(errors);
        }
        catch (DomainValidationException exception) when (exception.Errors.Contains<LengthError>())
        {
            var failure = Assert.Single(exception.Errors.OfType<LengthError>());
            Assert.Equal(70, ((LengthError)failure.Error).Actual);
            return;
        }
    }

    [Fact]
    public void Empty_errors_are_rejected()
        => Assert.Throws<ArgumentException>(() => new DomainValidationException(new ValidationErrors()));
}
