using Typewright.Validation;
using Xunit;

namespace Typewright.Validation.Tests;

public class OperationResultTests
{
    private sealed record NotFoundError(string Key) : IDomainError;

    private static OperationResult<int> Lookup(string key)
        => key == "known" ? 42 : OperationResult.Reject(new NotFoundError(key));

    [Fact]
    public void Values_and_rejections_convert_implicitly()
    {
        var found = Lookup("known");
        var missing = Lookup("other");

        Assert.True(found.IsSuccess);
        Assert.Equal(42, found.Value);
        Assert.False(missing.IsSuccess);
        Assert.Equal(new NotFoundError("other"), missing.Error);
        Assert.Equal("not_found", missing.Error.Code);
    }

    [Fact]
    public void Each_branch_hides_the_other()
    {
        Assert.Throws<InvalidOperationException>(() => Lookup("known").Error);
        Assert.Throws<InvalidOperationException>(() => Lookup("other").Value);
        Assert.True(Lookup("known").TryGetValue(out var value));
        Assert.Equal(42, value);
        Assert.True(Lookup("other").TryGetError(out var error));
        Assert.IsType<NotFoundError>(error);
    }

    [Fact]
    public void Match_folds_both_branches()
    {
        Assert.Equal("42", Lookup("known").Match(value => value.ToString(), error => error.Code));
        Assert.Equal("not_found", Lookup("other").Match(value => value.ToString(), error => error.Code));
    }

    [Fact]
    public void OrThrow_carries_the_typed_error()
    {
        var thrown = Assert.Throws<DomainErrorException>(() => Lookup("other").OrThrow());

        var notFound = Assert.IsType<NotFoundError>(thrown.Error);
        Assert.Equal("other", notFound.Key);
        Assert.Contains("not_found", thrown.Message);
    }

    [Fact]
    public void Default_instance_fails_loudly()
    {
        var result = default(OperationResult<int>);

        Assert.False(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }
}
