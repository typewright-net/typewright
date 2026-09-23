using Typewright.Actors;
using Typewright.Validation;
using Xunit;

namespace Typewright.Actors.Tests;

public class ActorPathTests
{
    [Theory]
    [InlineData("/orders")]
    [InlineData("/orders/42")]
    [InlineData("/a.b/c~d_e-f/.hidden")]
    public void Accepts_rooted_slash_separated_paths(string path)
        => Assert.True(ActorPath.TryCreate(path).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("orders")]
    [InlineData("/orders/")]
    [InlineData("//orders")]
    [InlineData("/orders/./42")]
    [InlineData("/orders/../42")]
    [InlineData("/..")]
    [InlineData("/has space")]
    public void Rejects_everything_else(string path)
    {
        var result = ActorPath.TryCreate(path);

        Assert.False(result.IsValid);
        Assert.IsType<PatternError>(Assert.Single(result.Errors).Error);
    }

    [Fact]
    public void Navigates_parents_children_and_containment()
    {
        var order = ActorPath.Create("/orders/42");
        var orders = ActorPath.Create("/orders");

        Assert.Equal("42", order.Name);
        Assert.Equal(orders, order.Parent);
        Assert.Null(orders.Parent);
        Assert.Equal(2, order.Depth);
        Assert.Equal(order, orders.Child("42"));
        Assert.True(orders.IsAncestorOf(order));
        Assert.True(order.IsDescendantOf(orders));
        Assert.False(ActorPath.Create("/orders4").IsDescendantOf(orders));   // containment is by segment, not prefix
        Assert.False(orders.IsAncestorOf(orders));
        Assert.Throws<ArgumentException>(() => orders.Child("a/b"));
        Assert.Throws<DomainValidationException>(() => orders.Child(".."));
    }
}
