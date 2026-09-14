using Typewright.Validation;
using Xunit;

namespace Typewright.Validation.Tests;

public class DomainValidationExceptionTests
{
    [Fact]
    public void Single_error_message_names_path_code_and_message()
    {
        var exception = new DomainValidationException("invoiceNumber", "pattern", "Bad format.");

        Assert.Equal("Validation failed: invoiceNumber: [pattern] Bad format.", exception.Message);
        var error = Assert.Single(exception.Errors);
        Assert.Equal(new ValidationError("invoiceNumber", "pattern", "Bad format."), error);
    }

    [Fact]
    public void Root_level_error_omits_the_path()
    {
        var errors = new ValidationErrors();
        errors.Add("date_range.invalid", "End before begin.");

        var exception = new DomainValidationException(errors);

        Assert.Equal("Validation failed: [date_range.invalid] End before begin.", exception.Message);
    }

    [Fact]
    public void Multiple_errors_are_counted_and_listed()
    {
        var errors = new ValidationErrors();
        errors.AddAt("a", "one", "First.");
        errors.AddAt("b", "two", "Second.");

        var exception = new DomainValidationException(errors);

        Assert.Equal("Validation failed with 2 errors: a: [one] First.; b: [two] Second.", exception.Message);
    }

    [Fact]
    public void Empty_errors_are_rejected()
        => Assert.Throws<ArgumentException>(() => new DomainValidationException(new ValidationErrors()));
}
