namespace Typewright.Actors;

/// <summary>What a supervisor does with a child whose failure policy escalated.</summary>
public enum SupervisionDirective
{
    /// <summary>Recreate the child from its last committed state (the failed agent's queued requests have failed).</summary>
    Resume,

    /// <summary>Recreate the child from its definition's initial state.</summary>
    Restart,

    /// <summary>Stop the child and everything beneath its path.</summary>
    Stop,

    /// <summary>Stop the child and treat its parent as failed, for the grandparent to decide.</summary>
    Escalate,
}

/// <summary>A child's escalated failure, as seen by its supervisor.</summary>
public sealed class ChildFailure
{
    private readonly IReadOnlyList<long> _restarts;
    private readonly TimeProvider _time;

    internal ChildFailure(ActorPath child, Exception exception, IReadOnlyList<long> restarts, TimeProvider time)
    {
        Child = child;
        Exception = exception;
        _restarts = restarts;
        _time = time;
    }

    /// <summary>The failed child's path.</summary>
    public ActorPath Child { get; }

    /// <summary>The failure.</summary>
    public Exception Exception { get; }

    /// <summary>How often the child has been recreated (restarted or resumed) over its lifetime.</summary>
    public int TotalRestarts => _restarts.Count;

    /// <summary>How often the child has been recreated within the trailing <paramref name="window"/>.</summary>
    public int RestartsWithin(TimeSpan window)
    {
        var now = _time.GetTimestamp();
        return _restarts.Count(started => _time.GetElapsedTime(started, now) <= window);
    }
}

/// <summary>
/// Decides the fate of failed children, one child at a time (one-for-one). Set on a
/// parent's definition (<see cref="ActorDefinition{TState, TMessage}.ChildSupervision"/>)
/// or system-wide (<see cref="ActorSystemOptions.DefaultSupervision"/>).
/// </summary>
public abstract class SupervisionStrategy
{
    /// <summary>Recreate every failed child from its last committed state.</summary>
    public static SupervisionStrategy Resume { get; } = From(static _ => SupervisionDirective.Resume);

    /// <summary>Stop every failed child.</summary>
    public static SupervisionStrategy Stop { get; } = From(static _ => SupervisionDirective.Stop);

    /// <summary>Escalate every failure to the grandparent.</summary>
    public static SupervisionStrategy Escalate { get; } = From(static _ => SupervisionDirective.Escalate);

    /// <summary>Decides for <paramref name="failure"/>.</summary>
    public abstract SupervisionDirective Decide(ChildFailure failure);

    /// <summary>
    /// Restart failed children, at most <paramref name="maxRestarts"/> times within
    /// <paramref name="within"/> each; beyond that, apply <paramref name="otherwise"/>.
    /// </summary>
    public static SupervisionStrategy Restart(
        int maxRestarts = int.MaxValue,
        TimeSpan? within = null,
        SupervisionDirective otherwise = SupervisionDirective.Stop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRestarts);
        var window = within ?? TimeSpan.MaxValue;
        return From(failure => failure.RestartsWithin(window) < maxRestarts ? SupervisionDirective.Restart : otherwise);
    }

    /// <summary>A strategy from a delegate.</summary>
    public static SupervisionStrategy From(Func<ChildFailure, SupervisionDirective> decide)
    {
        ArgumentNullException.ThrowIfNull(decide);
        return new DelegateStrategy(decide);
    }

    private sealed class DelegateStrategy(Func<ChildFailure, SupervisionDirective> decide) : SupervisionStrategy
    {
        public override SupervisionDirective Decide(ChildFailure failure) => decide(failure);
    }
}

/// <summary>A child escalated a failure its supervisor also escalated; the parent is treated as failed.</summary>
public sealed class ActorEscalationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ActorEscalationException(ActorPath child, Exception cause)
        : base($"The child actor '{child}' escalated a failure: {cause.Message}", cause)
    {
        Child = child;
    }

    /// <summary>The child whose failure escalated.</summary>
    public ActorPath Child { get; }
}

/// <summary>An actor already exists at the path being spawned.</summary>
public sealed class ActorAlreadyExistsException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public ActorAlreadyExistsException(ActorPath path)
        : base($"An actor already exists at '{path}'.")
    {
        Path = path;
    }

    /// <summary>The occupied path.</summary>
    public ActorPath Path { get; }
}
