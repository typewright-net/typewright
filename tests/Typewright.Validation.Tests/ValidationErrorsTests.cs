using Typewright.Validation;
using Xunit;

namespace Typewright.Validation.Tests;

public class CombinePathTests
{
    [Theory]
    [InlineData("", "", "")]
    [InlineData("", "a", "a")]
    [InlineData("a", "", "a")]
    [InlineData("a", "b", "a.b")]
    [InlineData("a", "[2]", "a[2]")]
    [InlineData("", "[2]", "[2]")]
    [InlineData("lineItems[2]", "quantity", "lineItems[2].quantity")]
    public void Combines_fragments_with_json_path_convention(string prefix, string relative, string expected)
        => Assert.Equal(expected, ValidationErrors.CombinePath(prefix, relative));
}

public class ValidationErrorsTests
{
    [Fact]
    public void Add_at_root_scope_has_empty_path()
    {
        var errors = new ValidationErrors();
        errors.Add(new RequiredError());

        var failure = Assert.Single(errors);
        Assert.Equal("", failure.Path);
        Assert.IsType<RequiredError>(failure.Error);
        Assert.Equal("required", failure.Error.Code);
    }

    [Fact]
    public void PushPath_prefixes_added_errors_and_pops_on_dispose()
    {
        var errors = new ValidationErrors();
        using (errors.PushPath("lineItems[2]"))
        {
            errors.AddAt("quantity", new RangeError("1", "10", "0"));
            using (errors.PushPath("nested"))
            {
                errors.Add(new RequiredError());
            }
        }

        errors.Add(new NotEmptyError());

        Assert.Equal(3, errors.Count);
        Assert.Equal("lineItems[2].quantity", errors[0].Path);
        Assert.Equal("lineItems[2].nested", errors[1].Path);
        Assert.Equal("", errors[2].Path);
    }

    [Fact]
    public void Merge_with_prefix_rebases_child_failures()
    {
        var child = new ValidationErrors();
        child.Add(new RequiredError());
        child.AddAt("inner", new PatternError("^x$"));

        var parent = new ValidationErrors();
        parent.Merge("items[0]", child);

        Assert.Equal(2, parent.Count);
        Assert.Equal("items[0]", parent[0].Path);
        Assert.Equal("items[0].inner", parent[1].Path);
    }

    [Fact]
    public void Merge_without_prefix_respects_current_scope()
    {
        var child = new ValidationErrors();
        child.Add(new RequiredError());

        var parent = new ValidationErrors();
        using (parent.PushPath("outer"))
        {
            parent.Merge(child);
        }

        Assert.Equal("outer", Assert.Single(parent).Path);
    }

    [Fact]
    public void Contains_and_OfType_filter_by_error_type()
    {
        var errors = new ValidationErrors();
        errors.AddAt("a", new RequiredError());
        errors.AddAt("b", new LengthError(1, 64, 70));

        Assert.True(errors.Contains<RequiredError>());
        Assert.True(errors.Contains<LengthError>());
        Assert.False(errors.Contains<PatternError>());

        var lengthFailure = Assert.Single(errors.OfType<LengthError>());
        Assert.Equal("b", lengthFailure.Path);
        Assert.Equal(70, ((LengthError)lengthFailure.Error).Actual);
    }
}

public class DomainErrorCodeTests
{
    [Theory]
    [InlineData(typeof(RequiredError), "required")]
    [InlineData(typeof(NotEmptyError), "not_empty")]
    [InlineData(typeof(JsonInvalidError), "json_invalid")]
    public void Codes_derive_from_type_names(Type errorType, string expected)
        => Assert.Equal(expected, DomainErrorCode.For(errorType));

    private sealed record LegacyError : IDomainError
    {
        public string Code => "legacy.code";
    }

    [Fact]
    public void Types_can_override_the_derived_code()
        => Assert.Equal("legacy.code", ((IDomainError)new LegacyError()).Code);

    private sealed record BareFactError : IDomainError;

    [Fact]
    public void Default_message_falls_back_to_the_code()
        => Assert.Equal(
            "The value is invalid (bare_fact).",
            ((IDomainError)new BareFactError()).DefaultMessage);
}
