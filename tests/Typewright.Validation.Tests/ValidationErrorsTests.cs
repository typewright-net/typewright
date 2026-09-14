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
        errors.Add("required", "A value is required.");

        var error = Assert.Single(errors);
        Assert.Equal(new ValidationError("", "required", "A value is required."), error);
    }

    [Fact]
    public void PushPath_prefixes_added_errors_and_pops_on_dispose()
    {
        var errors = new ValidationErrors();
        using (errors.PushPath("lineItems[2]"))
        {
            errors.AddAt("quantity", "range", "Out of range.");
            using (errors.PushPath("nested"))
            {
                errors.Add("code", "message");
            }
        }

        errors.Add("outer", "Back at the root.");

        Assert.Equal(3, errors.Count);
        Assert.Equal("lineItems[2].quantity", errors[0].Path);
        Assert.Equal("lineItems[2].nested", errors[1].Path);
        Assert.Equal("", errors[2].Path);
    }

    [Fact]
    public void Merge_with_prefix_rebases_child_errors()
    {
        var child = new ValidationErrors();
        child.Add("required", "A value is required.");
        child.AddAt("inner", "pattern", "Bad pattern.");

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
        child.Add("code", "message");

        var parent = new ValidationErrors();
        using (parent.PushPath("outer"))
        {
            parent.Merge(child);
        }

        Assert.Equal("outer", Assert.Single(parent).Path);
    }
}
