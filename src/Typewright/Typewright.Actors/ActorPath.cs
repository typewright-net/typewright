using Typewright.Validation;

namespace Typewright.Actors;

/// <summary>
/// An actor's address: a rooted, slash-separated path such as <c>/orders</c> or
/// <c>/orders/42</c>. Parent/child relationships are path containment. The same values
/// name Lattice resources and REST routes, so an address is validated exactly like any
/// other Typewright domain value.
/// </summary>
[DomainValue<string>]
[Pattern(@"^(/(?!\.{1,2}(?:/|$))[A-Za-z0-9._~-]+)+$")]
public readonly partial record struct ActorPath
{
    /// <summary>The last segment: <c>42</c> for <c>/orders/42</c>.</summary>
    public string Name => Value[(Value.LastIndexOf('/') + 1)..];

    /// <summary>The enclosing path, or null for a top-level path.</summary>
    public ActorPath? Parent
    {
        get
        {
            var separator = Value.LastIndexOf('/');
            return separator == 0 ? null : Create(Value[..separator]);
        }
    }

    /// <summary>The number of segments: 1 for <c>/orders</c>, 2 for <c>/orders/42</c>.</summary>
    public int Depth => Value.AsSpan().Count('/');

    /// <summary>The child path named <paramref name="name"/>; throws for an invalid segment.</summary>
    public ActorPath Child(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Contains('/'))
        {
            throw new ArgumentException("A child name is a single path segment and cannot contain '/'.", nameof(name));
        }

        return Create(Value + "/" + name);
    }

    /// <summary>Whether this path strictly contains <paramref name="other"/>.</summary>
    public bool IsAncestorOf(ActorPath other)
        => other.Value.Length > Value.Length
            && other.Value.StartsWith(Value, StringComparison.Ordinal)
            && other.Value[Value.Length] == '/';

    /// <summary>Whether <paramref name="other"/> strictly contains this path.</summary>
    public bool IsDescendantOf(ActorPath other) => other.IsAncestorOf(this);
}
